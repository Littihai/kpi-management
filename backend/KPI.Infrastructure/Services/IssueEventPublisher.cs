using KPI.Application.Common;
using KPI.Application.Notifications;
using KPI.Application.Settings;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Services;

/// <summary>
/// Live push and e-mail after an issue changes. Never throws: a failed push or e-mail must not
/// undo or fail the change that already succeeded. Set Email:Enabled=false to switch e-mail off.
/// </summary>
public class IssueEventPublisher
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(8);

    private readonly IRealtimeNotifier _notifier;
    private readonly AppDbContext _context;
    private readonly IEmailService _email;
    private readonly ILogger<IssueEventPublisher> _logger;
    private readonly ISystemSettings _settings;
    private readonly bool _emailEnabled;
    private readonly int _pacingMs;

    public IssueEventPublisher(
        IRealtimeNotifier notifier,
        AppDbContext context,
        IEmailService email,
        IConfiguration config,
        ISystemSettings settings,
        ILogger<IssueEventPublisher> logger)
    {
        _settings = settings;
        _notifier = notifier;
        _context = context;
        _email = email;
        _logger = logger;
        _emailEnabled = !string.Equals(config["Email:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        _pacingMs = int.TryParse(config["Email:PacingMs"], out var ms) ? Math.Max(0, ms) : 550;
    }

    public async Task PublishAsync(
        Issue issue,
        string action,
        string? actorName,
        Guid? actorId,
        RealtimeNotification? toast = null,
        RealtimeAudience? audience = null,
        IssueEmailKind? email = null,
        string? detail = null,
        bool staffOnly = false)
    {
        // Stored before the push, so a client that reacts to the toast by fetching its list already finds the entry.
        if (toast != null && audience != null)
            await StoreNotificationsAsync(issue, action, toast, audience, actorId);

        try
        {
            await _notifier.PublishIssueChangedAsync(new IssueChangedEvent(
                issue.Id, issue.DisplayNumber, issue.Title, action, issue.Status, issue.Priority,
                issue.ReporterUserId, issue.AssigneeId, issue.DepartmentId, actorName, DateTime.UtcNow, staffOnly));

            if (toast != null && audience != null)
                await _notifier.NotifyAsync(toast, audience);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Real-time publish failed for issue {IssueId} ({Action})", issue.Id, action);
        }

        if (email is { } kind)
            await SendEmailsAsync(issue, kind, actorId, actorName, detail);
    }

    /// <summary>
    /// Keeps the toast for each person it was meant for, except the person who did it (they know). Only a toast with an audience
    /// is kept, so internal notes, quiet closes and SLA breaches never reach anyone's bell. A failure here is logged and the new
    /// rows are dropped from the context, so the change that already succeeded is neither failed nor blocked by a later save.
    /// </summary>
    private async Task StoreNotificationsAsync(Issue issue, string action, RealtimeNotification toast, RealtimeAudience audience, Guid? actorId)
    {
        var added = new List<Notification>();
        try
        {
            var recipients = new HashSet<Guid>(audience.UserIds);
            if (recipients.Count > 0)
            {
                var active = await _context.Users.AsNoTracking()
                    .Where(u => u.IsActive && recipients.Contains(u.Id)).Select(u => u.Id).ToListAsync();
                recipients.IntersectWith(active);
            }
            if (audience.ItSupport)
                recipients.UnionWith(await IssueQueue.Workers(_context).AsNoTracking().Select(u => u.Id).ToListAsync());
            if (actorId is { } actor) recipients.Remove(actor);
            if (recipients.Count == 0) return;

            var now = DateTime.UtcNow;
            foreach (var userId in recipients)
            {
                var row = new Notification
                {
                    UserId = userId,
                    IssueId = toast.IssueId ?? issue.Id,
                    Kind = action.Truncate(40)!,
                    Title = toast.Title.Truncate(200)!,
                    Message = toast.Message.Truncate(500)!,
                    Level = toast.Level.Truncate(10)!,
                    CreatedAt = now,
                };
                added.Add(row);
                _context.Notifications.Add(row);
            }
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not store notifications for issue {IssueId} ({Action})", issue.Id, action);
            foreach (var row in added)
                if (_context.Entry(row).State != EntityState.Detached)
                    _context.Entry(row).State = EntityState.Detached;
        }
    }

    private async Task SendEmailsAsync(Issue issue, IssueEmailKind kind, Guid? actorId, string? actorName, string? detail)
    {
        // Email:Enabled=false in configuration is a hard off switch; the setting is the one administrators use.
        if (!_emailEnabled || !await _settings.GetBoolAsync(SettingCatalog.Keys.EmailEnabled)) return;

        try
        {
            var recipients = await RecipientsAsync(issue, kind, actorId);
            var first = true;
            foreach (var (address, name) in recipients)
            {
                if (!first && _pacingMs > 0) await Task.Delay(_pacingMs);
                first = false;

                try
                {
                    using var timeout = new CancellationTokenSource(SendTimeout);
                    await _email.SendIssueAsync(new IssueEmailMessage(
                        kind, address, name, issue.Id, issue.DisplayNumber, issue.Title,
                        actorName, detail, issue.ResponseDueAt), timeout.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Issue e-mail '{Kind}' for {Issue} failed", kind, issue.DisplayNumber);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send '{Kind}' e-mails for issue {Issue}", kind, issue.DisplayNumber);
        }
    }

    private async Task<List<(string Email, string Name)>> RecipientsAsync(Issue issue, IssueEmailKind kind, Guid? actorId)
    {
        var people = new Dictionary<string, (string Email, string Name, Guid? UserId)>(StringComparer.OrdinalIgnoreCase);

        void Add(string? email, string name, Guid? userId)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            if (actorId is { } actor && userId == actor) return; // do not e-mail people about their own action
            people.TryAdd(email, (email, name, userId));
        }

        async Task AddReporterAsync()
        {
            if (issue.ReporterUserId is { } id)
            {
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
                if (user != null) Add(user.Email, user.FullName, user.Id);
            }
            else
            {
                Add(issue.ReporterEmail, issue.ReporterName, null);
            }
        }

        async Task AddItAsync()
        {
            var it = await _context.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role.Name != RoleNames.SuperAdmin
                    && u.Role.RolePermissions.Any(rp => rp.Permission.Name == Permissions.Issue.Manage)).ToListAsync();
            if (it.Count == 0)
                it = await _context.Users.AsNoTracking()
                    .Where(u => u.IsActive && u.Role.Name == RoleNames.SuperAdmin).ToListAsync();
            foreach (var u in it) Add(u.Email, u.FullName, u.Id);
        }

        async Task AddAssigneeOrItAsync()
        {
            if (issue.AssigneeId is { } id)
            {
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
                if (user != null) { Add(user.Email, user.FullName, user.Id); return; }
            }
            await AddItAsync();
        }

        switch (kind)
        {
            case IssueEmailKind.Received:
            case IssueEmailKind.NeedsInfo:
            case IssueEmailKind.StaffReplied:
            case IssueEmailKind.Resolved:
            case IssueEmailKind.Rejected:
                await AddReporterAsync();
                break;
            case IssueEmailKind.NewForIt:
                await AddItAsync();
                break;
            case IssueEmailKind.Assigned:
                await AddAssigneeOrItAsync();
                break;
            case IssueEmailKind.ReporterReplied:
            case IssueEmailKind.Reopened:
                await AddAssigneeOrItAsync();
                break;
        }

        return people.Values.Select(p => (p.Email, p.Name)).ToList();
    }
}

using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
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
    private readonly bool _emailEnabled;
    private readonly int _pacingMs;

    public IssueEventPublisher(
        IRealtimeNotifier notifier,
        AppDbContext context,
        IEmailService email,
        IConfiguration config,
        ILogger<IssueEventPublisher> logger)
    {
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

    private async Task SendEmailsAsync(Issue issue, IssueEmailKind kind, Guid? actorId, string? actorName, string? detail)
    {
        if (!_emailEnabled) return;

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

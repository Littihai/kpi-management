using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Services;

/// <summary>
/// Works out who should be e-mailed about a KPI event and sends one message each.
/// Best effort: it never throws, so a mail problem cannot undo or fail the change that triggered it.
/// Disable entirely with Email:Enabled=false.
/// </summary>
public class KpiEmailNotifier
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(8);

    private readonly AppDbContext _context;
    private readonly IEmailService _email;
    private readonly ILogger<KpiEmailNotifier> _logger;
    private readonly bool _enabled;
    private readonly int _pacingMs;

    public KpiEmailNotifier(
        AppDbContext context,
        IEmailService email,
        IConfiguration config,
        ILogger<KpiEmailNotifier> logger)
    {
        _context = context;
        _email = email;
        _logger = logger;
        _enabled = !string.Equals(config["Email:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        // Resend allows about two requests per second; space out multi-recipient sends.
        _pacingMs = int.TryParse(config["Email:PacingMs"], out var ms) ? Math.Max(0, ms) : 550;
    }

    public async Task SendAsync(Kpi kpi, KpiEmailKind kind, Guid? actorId, string? actorName)
    {
        if (!_enabled) return;

        try
        {
            var recipients = await RecipientsAsync(kpi, kind, actorId);
            var first = true;

            foreach (var user in recipients)
            {
                if (!first && _pacingMs > 0) await Task.Delay(_pacingMs);
                first = false;

                try
                {
                    using var timeout = new CancellationTokenSource(SendTimeout);
                    await _email.SendAsync(new KpiEmailMessage(
                        kind,
                        user.Email,
                        user.FullName,
                        kpi.Id,
                        kpi.Name,
                        kpi.Department?.Name,
                        kpi.Owner is null ? null : kpi.Owner.FullName,
                        actorName,
                        kpi.Progress,
                        kpi.Target,
                        kpi.DueDate,
                        kpi.RejectReason), timeout.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "E-mail '{Kind}' for KPI {KpiId} to user {UserId} failed", kind, kpi.Id, user.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send '{Kind}' e-mails for KPI {KpiId}", kind, kpi.Id);
        }
    }

    private async Task<List<User>> RecipientsAsync(Kpi kpi, KpiEmailKind kind, Guid? actorId)
    {
        var users = new Dictionary<Guid, User>();

        void Add(IEnumerable<User> found)
        {
            foreach (var u in found)
                users.TryAdd(u.Id, u);
        }

        switch (kind)
        {
            case KpiEmailKind.Created:
                Add(await DepartmentManagersAsync(kpi.DepartmentId));
                break;

            case KpiEmailKind.Submitted:
                var directors = await ActiveByRoleAsync(RoleNames.Director);
                // With no active Director nobody could act on it, so fall back to SuperAdmins.
                Add(directors.Count > 0 ? directors : await ActiveByRoleAsync(RoleNames.SuperAdmin));
                break;

            case KpiEmailKind.Approved:
            case KpiEmailKind.Rejected:
                Add(await ActiveByIdAsync(kpi.OwnerId));
                break;

            case KpiEmailKind.AtRisk:
            case KpiEmailKind.Delayed:
                Add(await ActiveByIdAsync(kpi.OwnerId));
                Add(await DepartmentManagersAsync(kpi.DepartmentId));
                break;

            case KpiEmailKind.Completed:
                Add(await DepartmentManagersAsync(kpi.DepartmentId));
                Add(await ActiveByRoleAsync(RoleNames.Director));
                break;
        }

        // The person who just did the thing does not need an e-mail about it.
        if (actorId is { } actor) users.Remove(actor);

        return users.Values.Where(u => !string.IsNullOrWhiteSpace(u.Email)).ToList();
    }

    private Task<List<User>> ActiveByIdAsync(Guid id) =>
        _context.Users.AsNoTracking().Where(u => u.Id == id && u.IsActive).ToListAsync();

    private Task<List<User>> ActiveByRoleAsync(string roleName) =>
        _context.Users.AsNoTracking().Where(u => u.IsActive && u.Role.Name == roleName).ToListAsync();

    private Task<List<User>> DepartmentManagersAsync(Guid departmentId) =>
        _context.Users.AsNoTracking()
            .Where(u => u.IsActive && u.DepartmentId == departmentId && u.Role.Name == RoleNames.Manager)
            .ToListAsync();
}

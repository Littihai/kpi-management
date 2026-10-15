using KPI.Application.Jobs;
using KPI.Application.Notifications;
using KPI.Domain.Entities;
using KPI.Domain.Enums;
using KPI.Domain.Kpis;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Jobs;

/// <summary>
/// Daily re-evaluation of every running KPI (Active, AtRisk, Delayed) with the shared
/// <see cref="KpiStateMachine"/> rules, so overdue KPIs become Delayed on their own and
/// KPIs that caught up return to Active.
/// </summary>
public class DelayDetectionJob : IDelayDetectionJob
{
    private readonly AppDbContext _context;
    private readonly KpiEventPublisher _events;
    private readonly ILogger<DelayDetectionJob> _logger;

    public DelayDetectionJob(
        AppDbContext context,
        KpiEventPublisher events,
        ILogger<DelayDetectionJob> logger)
    {
        _context = context;
        _events = events;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var now = DateTime.UtcNow;

        var running = await _context.Kpis
            .Include(k => k.Owner)
            .Include(k => k.Department)
            .Where(k => k.Status == KpiStatus.Active
                     || k.Status == KpiStatus.AtRisk
                     || k.Status == KpiStatus.Delayed)
            .ToListAsync();

        var changes = new List<(Kpi Kpi, KpiStatus Old)>();
        foreach (var kpi in running)
        {
            var next = KpiStateMachine.EvaluateHealth(
                kpi.Progress, kpi.Target, kpi.StartDate, kpi.DueDate, now);
            if (next == kpi.Status) continue;

            changes.Add((kpi, kpi.Status));
            _context.AuditLogs.Add(new AuditLog
            {
                Action = "AutoStatusChange",
                EntityName = "Kpi",
                EntityId = kpi.Id.ToString(),
                OldValues = kpi.Status.ToString(),
                NewValues = next.ToString(),
                UserEmail = "system",
            });
            kpi.Status = next;
            kpi.UpdatedAt = now;
            kpi.Version++;
        }

        // Persist first. Notifications are best-effort and must not undo or block the status changes.
        await _context.SaveChangesAsync();
        _logger.LogInformation("Delay detection: {Checked} KPIs checked, {Changed} changed", running.Count, changes.Count);

        foreach (var (kpi, old) in changes)
        {
            var warn = kpi.Status is KpiStatus.AtRisk or KpiStatus.Delayed;
            await _events.PublishAsync(
                kpi, "autoStatus", "System",
                warn ? new RealtimeNotification($"KPI {kpi.Status}", $"\"{kpi.Name}\" is now {kpi.Status}.", "warning", kpi.Id) : null,
                warn ? new RealtimeAudience([kpi.OwnerId], ManagersOfDepartment: kpi.DepartmentId) : null,
                KpiService.EmailForStatusChange(old, kpi.Status));
        }
    }
}

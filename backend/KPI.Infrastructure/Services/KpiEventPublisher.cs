using KPI.Application.Notifications;
using KPI.Domain.Entities;
using KPI.Domain.Kpis;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Services;

/// <summary>
/// Pushes real-time updates after a KPI changes. Never throws: a failed push must not
/// roll back or fail the business operation that already succeeded.
/// </summary>
public class KpiEventPublisher
{
    private readonly IRealtimeNotifier _notifier;
    private readonly ILogger<KpiEventPublisher> _logger;

    public KpiEventPublisher(IRealtimeNotifier notifier, ILogger<KpiEventPublisher> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    public async Task PublishAsync(
        Kpi kpi,
        string action,
        string? actorName,
        RealtimeNotification? notification = null,
        RealtimeAudience? audience = null)
    {
        try
        {
            await _notifier.PublishKpiChangedAsync(new KpiChangedEvent(
                KpiId: kpi.Id,
                Name: kpi.Name,
                Action: action,
                Status: kpi.Status,
                StatusLabel: kpi.Status.ToString(),
                TrafficLight: KpiStateMachine.TrafficLight(kpi.Status),
                Progress: kpi.Progress,
                DepartmentId: kpi.DepartmentId,
                OwnerId: kpi.OwnerId,
                ActorName: actorName,
                At: DateTime.UtcNow));

            if (notification != null && audience != null)
                await _notifier.NotifyAsync(notification, audience);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Real-time publish failed for KPI {KpiId} ({Action})", kpi.Id, action);
        }
    }
}

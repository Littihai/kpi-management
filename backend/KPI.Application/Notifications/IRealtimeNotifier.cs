using KPI.Domain.Enums;

namespace KPI.Application.Notifications;

/// <summary>Pushed to every client that may see the KPI so lists and dashboards can refresh.</summary>
public record KpiChangedEvent(
    Guid KpiId,
    string Name,
    string Action,
    KpiStatus Status,
    string StatusLabel,
    string TrafficLight,
    decimal Progress,
    Guid DepartmentId,
    Guid OwnerId,
    string? ActorName,
    DateTime At);

/// <summary>A toast-style message for specific people.</summary>
public record RealtimeNotification(
    string Title,
    string Message,
    string Level,       // info | success | warning | error
    Guid? KpiId);

/// <summary>Who should receive a <see cref="RealtimeNotification"/>.</summary>
public record RealtimeAudience(
    IReadOnlyList<Guid> UserIds,
    bool Approvers = false,                 // SuperAdmin + Director (users who see every department)
    Guid? ManagersOfDepartment = null);     // Managers of one department

public interface IRealtimeNotifier
{
    Task PublishKpiChangedAsync(KpiChangedEvent evt, CancellationToken ct = default);
    Task NotifyAsync(RealtimeNotification notification, RealtimeAudience audience, CancellationToken ct = default);
}

using KPI.Domain.Issues;

namespace KPI.Application.Notifications;

/// <summary>Pushed to everyone who may see the issue so queues and detail pages can refresh.</summary>
public record IssueChangedEvent(
    Guid IssueId,
    string Number,
    string Title,
    string Action,
    IssueStatus Status,
    IssuePriority Priority,
    Guid? ReporterUserId,
    Guid? AssigneeId,
    Guid? DepartmentId,
    string? ActorName,
    DateTime At);

/// <summary>A toast-style message for specific people.</summary>
public record RealtimeNotification(
    string Title,
    string Message,
    string Level,       // info | success | warning | error
    Guid? IssueId = null);

/// <summary>Who should receive a <see cref="RealtimeNotification"/>.</summary>
public record RealtimeAudience(
    IReadOnlyList<Guid> UserIds,
    bool ItSupport = false);                // the issue queue: IT Support and SuperAdmin

public interface IRealtimeNotifier
{
    Task PublishIssueChangedAsync(IssueChangedEvent evt, CancellationToken ct = default);
    Task NotifyAsync(RealtimeNotification notification, RealtimeAudience audience, CancellationToken ct = default);
}

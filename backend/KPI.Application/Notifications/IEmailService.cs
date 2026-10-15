namespace KPI.Application.Notifications;

public enum KpiEmailKind
{
    Created,    // a KPI was created by someone else in the department
    Submitted,  // waiting for approval
    Approved,
    Rejected,
    AtRisk,
    Delayed,
    Completed,
}

/// <summary>Everything needed to write one e-mail about a KPI to one person.</summary>
public record KpiEmailMessage(
    KpiEmailKind Kind,
    string ToEmail,
    string ToName,
    Guid KpiId,
    string KpiName,
    string? DepartmentName,
    string? OwnerName,
    string? ActorName,
    decimal Progress,
    decimal Target,
    DateTime DueDate,
    string? Reason);

public interface IEmailService
{
    Task SendAsync(KpiEmailMessage message, CancellationToken cancellationToken = default);
}

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

public enum IssueEmailKind
{
    Received,        // to the reporter: we got it, here is your number
    NewForIt,        // to IT: a new issue is waiting
    Assigned,        // to the assignee
    NeedsInfo,       // to the reporter: IT needs more information
    ReporterReplied, // to the assignee / IT
    StaffReplied,    // to the reporter
    Resolved,        // to the reporter: fixed, please confirm
    Rejected,        // to the reporter
    Reopened,        // to the assignee / IT
}

public record IssueEmailMessage(
    IssueEmailKind Kind,
    string ToEmail,
    string ToName,
    Guid IssueId,
    string Number,
    string Title,
    string? ActorName,
    string? Detail,          // reason / question / reply / resolution, depending on the kind
    DateTime ResponseDueAt);

public interface IEmailService
{
    Task SendAsync(KpiEmailMessage message, CancellationToken cancellationToken = default);
    Task SendIssueAsync(IssueEmailMessage message, CancellationToken cancellationToken = default);
}

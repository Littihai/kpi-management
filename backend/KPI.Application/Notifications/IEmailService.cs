namespace KPI.Application.Notifications;

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
    Task SendIssueAsync(IssueEmailMessage message, CancellationToken cancellationToken = default);
}

using KPI.Domain.Issues;

namespace KPI.Application.Issues;

public record IssueListItemDto(
    Guid Id,
    string Number,
    string Title,
    IssueCategory Category,
    IssueModule Module,
    IssueImpact Impact,
    IssuePriority Priority,
    IssueStatus Status,
    string Tone,
    string ReporterName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime ResponseDueAt,
    DateTime ResolveDueAt,
    bool ResponseOverdue,
    bool ResolveOverdue,
    int CommentCount,
    int AttachmentCount,
    bool IsPublicSubmission);

public record IssueCommentDto(
    Guid Id, string AuthorName, bool IsStaff, bool IsInternal, string Body, DateTime CreatedAt);

public record IssueAttachmentDto(
    Guid Id, string FileName, string ContentType, int SizeBytes, DateTime CreatedAt);

/// <summary>One step in an issue's history, taken from the audit log.</summary>
public record IssueEventDto(
    string Action, string? From, string? To, string? ActorName, string? ActorRole, DateTime At);

public record IssueDetailDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    IssueCategory Category,
    IssueModule Module,
    IssueImpact Impact,
    IssuePriority Priority,
    IssueStatus Status,
    string Tone,
    Guid? ReporterUserId,
    string ReporterName,
    string? ReporterEmail,
    string? ReporterPhone,
    bool IsPublicSubmission,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime ResponseDueAt,
    DateTime? FirstResponseAt,
    DateTime ResolveDueAt,
    bool ResponseOverdue,
    bool ResolveOverdue,
    DateTime? WaitingSince,
    DateTime? ResolvedAt,
    DateTime? ClosedAt,
    int ReopenCount,
    IssueRootCause RootCause,
    string? Resolution,
    string? Prevention,
    string? FixReference,
    int? TimeSpentMinutes,
    string? RejectReason,
    Guid? DuplicateOfId,
    string? PageUrl,
    string? UserAgent,
    string? ClientInfo,
    string? ErrorRequestId,
    string? ReporterIp,
    IReadOnlyList<IssueCommentDto> Comments,
    IReadOnlyList<IssueAttachmentDto> Attachments,
    IReadOnlyList<IssueEventDto> Timeline,
    IReadOnlyList<string> AllowedActions);

public record IssueQuery(
    string? Search = null,
    IssueStatus? Status = null,
    bool? OpenOnly = null,
    IssuePriority? Priority = null,
    IssueModule? Module = null,
    Guid? DepartmentId = null,
    Guid? AssigneeId = null,
    bool? Mine = null,
    bool? Overdue = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 20,
    bool? InDescription = null);

public record IssueSummaryDto(
    int Open,
    int New,
    int InProgress,
    int WaitingForReporter,
    int Overdue,
    int ResolvedLast7Days,
    int Mine);

/// <summary>Reporter details come from the profile when signed in; visitors fill them in.</summary>
public record CreateIssueRequest(
    string Title,
    string Description,
    IssueCategory Category,
    IssueModule Module,
    IssueImpact Impact,
    string? PageUrl = null,
    string? UserAgent = null,
    string? ClientInfo = null,
    string? ErrorRequestId = null);

public record PublicIssueRequest(
    string ReporterName,
    string? ReporterEmail,
    string? ReporterPhone,
    Guid? DepartmentId,
    string Title,
    string Description,
    IssueCategory Category,
    IssueModule Module,
    IssueImpact Impact,
    string? UserAgent = null,
    string? ClientInfo = null);

public record PublicIssueReceipt(string Number, DateTime CreatedAt, DateTime ResponseDueAt);

public record ResolveIssueRequest(
    string Resolution,
    IssueRootCause RootCause,
    string? Prevention = null,
    string? FixReference = null,
    int? TimeSpentMinutes = null);

public record IssueUpload(string FileName, string ContentType, byte[] Data);

public record IssueAttachmentContent(string FileName, string ContentType, byte[] Data);

public record AssigneeDto(Guid Id, string Name, string Role, int OpenIssues);

/// <summary>Names returned in <see cref="IssueDetailDto.AllowedActions"/>.</summary>
public static class IssueActionNames
{
    public const string Assign = "assign";
    public const string Start = "start";
    public const string RequestInfo = "requestInfo";
    public const string Resolve = "resolve";
    public const string Confirm = "confirm";
    public const string Reopen = "reopen";
    public const string Reject = "reject";
    public const string Hold = "hold";
    public const string Resume = "resume";
    public const string SetPriority = "setPriority";
    public const string Comment = "comment";
    public const string InternalNote = "internalNote";
    public const string Attach = "attach";
}

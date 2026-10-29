using KPI.Application.Common;
using KPI.Domain.Issues;

namespace KPI.Application.Issues;

/// <summary>
/// Every method takes the acting user and enforces permission, scope (own / department / all) and status
/// rules, throwing <c>AppException</c> subclasses on failure.
/// </summary>
public interface IIssueService
{
    Task<IssueDetailDto> CreateAsync(CreateIssueRequest request, IReadOnlyList<IssueUpload> files, Guid userId, string? ip);

    /// <summary>For people who cannot sign in. Throws when the per-address limit is hit.</summary>
    Task<PublicIssueReceipt> CreatePublicAsync(PublicIssueRequest request, IReadOnlyList<IssueUpload> files, string? ip);

    Task<PagedResult<IssueListItemDto>> ListAsync(IssueQuery query, Guid userId);
    Task<IssueSummaryDto> SummaryAsync(Guid userId);

    /// <summary>The few best matches by number, title or reporter, inside what the person may see. Under two characters returns nothing.</summary>
    Task<IReadOnlyList<IssueHitDto>> QuickSearchAsync(string? term, Guid userId);

    /// <summary>What is waiting for this person right now; the number behind the red badge in the app.</summary>
    Task<IssueAttentionDto> AttentionAsync(Guid userId);
    Task<IssueDetailDto> GetAsync(Guid id, Guid userId);

    Task<IssueDetailDto> CommentAsync(Guid id, string body, bool internalNote, Guid userId);
    Task<IssueDetailDto> AssignAsync(Guid id, Guid assigneeId, Guid userId);
    Task<IssueDetailDto> StartAsync(Guid id, Guid userId);
    Task<IssueDetailDto> RequestInfoAsync(Guid id, string message, Guid userId);
    /// <summary>Park the issue (waiting for a vendor or a part). The resolve clock pauses until it is resumed.</summary>
    Task<IssueDetailDto> HoldAsync(Guid id, string reason, Guid userId);

    /// <summary>Continue from WaitingForReporter or OnHold. The note says why (for example: the reporter cannot reply).</summary>
    Task<IssueDetailDto> ResumeAsync(Guid id, string note, Guid userId);
    Task<IssueDetailDto> ResolveAsync(Guid id, ResolveIssueRequest request, Guid userId);
    Task<IssueDetailDto> ConfirmAsync(Guid id, Guid userId);
    Task<IssueDetailDto> ReopenAsync(Guid id, string reason, Guid userId);
    Task<IssueDetailDto> RejectAsync(Guid id, string reason, Guid? duplicateOfId, Guid userId);
    Task<IssueDetailDto> SetPriorityAsync(Guid id, IssuePriority priority, Guid userId);

    Task<IssueDetailDto> AddAttachmentsAsync(Guid id, IReadOnlyList<IssueUpload> files, Guid userId);
    Task<IssueAttachmentContent> GetAttachmentAsync(Guid id, Guid attachmentId, Guid userId);

    Task<List<AssigneeDto>> AssigneesAsync(Guid userId);

    /// <summary>Closes issues that were resolved and left unanswered. Returns how many were closed.</summary>
    Task<int> AutoCloseResolvedAsync(DateTime now);
}

using KPI.Application.Common;

namespace KPI.Application.Notifications;

public record NotificationDto(
    Guid Id,
    Guid? IssueId,
    string Kind,
    string Title,
    string Message,
    string Level,
    DateTime CreatedAt,
    bool IsRead);

public record NotificationQuery(bool UnreadOnly = false, int Page = 1, int PageSize = 20);

/// <summary>
/// A person's stored notifications. Every method acts for the caller only; an entry about an issue the caller
/// can no longer see (a role or department change) is hidden from the list and the count.
/// </summary>
public interface INotificationService
{
    /// <summary>The most the unread badge ever counts; the screens show it as "99+".</summary>
    public const int UnreadCap = 100;

    Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, Guid userId);

    /// <summary>Unread entries, counted up to <see cref="UnreadCap"/>.</summary>
    Task<int> UnreadCountAsync(Guid userId);

    /// <summary>Marks one entry read. Another person's entry is reported as not found.</summary>
    Task MarkReadAsync(Guid id, Guid userId);
    Task<int> MarkAllReadAsync(Guid userId);

    /// <summary>Marks every unread entry about one issue read (called when its page opens).</summary>
    Task<int> MarkIssueReadAsync(Guid issueId, Guid userId);

    /// <summary>Deletes read entries older than <paramref name="readCutoff"/> and unread ones older than <paramref name="unreadCutoff"/>.</summary>
    Task<int> PurgeAsync(DateTime readCutoff, DateTime unreadCutoff);
}

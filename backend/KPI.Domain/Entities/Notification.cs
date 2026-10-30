namespace KPI.Domain.Entities;

/// <summary>
/// One entry in a person's notification centre: the same message the live toast carried, kept so it can be read later.
/// Rows exist only where a toast with an audience exists, so an internal note never reaches the reporter's bell.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>A plain column, not a foreign key: the entry outlives the issue's visibility and is hidden by the same scope rule as the issue.</summary>
    public Guid? IssueId { get; set; }

    /// <summary>What happened to the issue ("comment", "resolved", ...), the same word the live event carries.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>info | success | warning | error</summary>
    public string Level { get; set; } = "info";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

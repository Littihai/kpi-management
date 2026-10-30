namespace KPI.Domain.Entities;

/// <summary>
/// A how-to or answer written by IT and read by everyone who can sign in. A draft is visible to the people who manage the
/// knowledge base only; publishing makes it visible to all. The body is plain text (blank lines separate paragraphs).
/// </summary>
public class KnowledgeArticle : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }

    /// <summary>The last editor. The link is cleared if the account is ever removed; the name stays.</summary>
    public Guid? AuthorId { get; set; }
    public User? Author { get; set; }
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Raised by every edit, publish and unpublish; an editor that saves with an older number is told someone else got there first.</summary>
    public int Version { get; set; }

    /// <summary>How many times a published article was opened. Not an edit, so it does not raise <see cref="Version"/>.</summary>
    public int ViewCount { get; set; }
}

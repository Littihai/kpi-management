using KPI.Domain.Issues;

namespace KPI.Domain.Entities;

/// <summary>A problem reported by a user (or by a visitor who could not sign in) and how it was fixed.</summary>
public class Issue : BaseEntity
{
    /// <summary>Running number within <see cref="NumberYear"/>; shown as ISS-2569-0001.</summary>
    public int Number { get; set; }
    /// <summary>Gregorian year the number belongs to (the display adds 543).</summary>
    public int NumberYear { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public IssueCategory Category { get; set; }
    public IssueModule Module { get; set; }
    public IssueImpact Impact { get; set; }
    public IssuePriority Priority { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.New;

    // Reporter. Signed-in users are linked; visitors only leave contact details.
    public Guid? ReporterUserId { get; set; }
    public User? ReporterUser { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string? ReporterEmail { get; set; }
    public string? ReporterPhone { get; set; }
    public bool IsPublicSubmission { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? AssigneeId { get; set; }
    public User? Assignee { get; set; }

    // Service levels
    public DateTime ResponseDueAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime ResolveDueAt { get; set; }
    /// <summary>Set while waiting for the reporter; the resolve deadline is pushed back by the waiting time.</summary>
    public DateTime? WaitingSince { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int ReopenCount { get; set; }

    // How it was fixed
    public IssueRootCause RootCause { get; set; }
    public string? Resolution { get; set; }
    public string? Prevention { get; set; }
    public string? FixReference { get; set; }
    public int? TimeSpentMinutes { get; set; }
    public string? RejectReason { get; set; }
    public Guid? DuplicateOfId { get; set; }

    // Captured automatically from the reporter's browser
    public string? PageUrl { get; set; }
    public string? UserAgent { get; set; }
    public string? ClientInfo { get; set; }
    public string? ErrorRequestId { get; set; }
    public string? ReporterIp { get; set; }

    public int Version { get; set; }

    public ICollection<IssueComment> Comments { get; set; } = new List<IssueComment>();
    public ICollection<IssueAttachment> Attachments { get; set; } = new List<IssueAttachment>();
    public IssueFeedback? Feedback { get; set; }

    public string DisplayNumber => FormatNumber(NumberYear, Number);

    public static string FormatNumber(int year, int number) => $"ISS-{year + 543}-{number:D4}";
}

public class IssueComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IssueId { get; set; }
    public Issue Issue { get; set; } = null!;

    public Guid? AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    /// <summary>True when the author works the issue queue (shown as "IT" to the reporter).</summary>
    public bool IsStaff { get; set; }
    /// <summary>Visible to IT only.</summary>
    public bool IsInternal { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An image attached to an issue. Stored in the database to avoid another service; small and capped.</summary>
public class IssueAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IssueId { get; set; }
    public Issue Issue { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public byte[] Data { get; set; } = [];

    public Guid? UploadedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// How satisfied the reporter was with the fix: one row per issue, overwritten when the issue is reopened and resolved again.
/// The rater and the assignee are plain columns (no foreign key): the rating stays attributable to whoever did the work at the
/// time, and a second cascading path from Users would be refused by SQL Server.
/// </summary>
public class IssueFeedback
{
    public Guid IssueId { get; set; }
    public Issue Issue { get; set; } = null!;

    /// <summary>1 (very unhappy) to 5 (very happy).</summary>
    public int Rating { get; set; }
    public string? Note { get; set; }

    public Guid RaterUserId { get; set; }
    /// <summary>Who held the issue when it was rated.</summary>
    public Guid? AssigneeId { get; set; }
    public DateTime RatedAt { get; set; } = DateTime.UtcNow;
}

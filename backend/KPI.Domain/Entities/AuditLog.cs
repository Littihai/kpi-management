namespace KPI.Domain.Entities;

/// <summary>A business event: who changed what, with before/after values and where from.</summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Filled in automatically from the current request when the row is saved.
    public string? UserName { get; set; }
    public string? UserRole { get; set; }
    public string? DepartmentName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestId { get; set; }
}

/// <summary>
/// One HTTP request worth keeping: every write, every refusal (401/403/429), every error and every sign-in.
/// Reads that succeed are not logged. Written in the background so requests never wait for it.
/// </summary>
public class RequestLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserRole { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestId { get; set; }
}

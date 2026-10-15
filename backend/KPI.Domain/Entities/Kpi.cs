using KPI.Domain.Enums;

namespace KPI.Domain.Entities;

public class Kpi : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal Target { get; set; }
    public decimal Progress { get; set; }
    public KpiStatus Status { get; set; } = KpiStatus.Draft;
    public DateTime StartDate { get; set; }
    public DateTime DueDate { get; set; }

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public Guid? ApprovedById { get; set; }
    public User? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public Guid? SubmittedById { get; set; }

    public string? RejectReason { get; set; }

    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedById { get; set; }

    /// <summary>Optimistic concurrency token, incremented on every update.</summary>
    public int Version { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<KpiProgressLog> ProgressLogs { get; set; } = new List<KpiProgressLog>();
}
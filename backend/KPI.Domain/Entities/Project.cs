using KPI.Domain.Enums;

namespace KPI.Domain.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal Progress { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;
    public DateTime StartDate { get; set; }
    public DateTime DueDate { get; set; }
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

    public Guid KpiId { get; set; }
    public Kpi Kpi { get; set; } = null!;

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
}
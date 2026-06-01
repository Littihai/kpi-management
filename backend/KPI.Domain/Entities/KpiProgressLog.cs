namespace KPI.Domain.Entities;

public class KpiProgressLog : BaseEntity
{
    public Guid KpiId { get; set; }
    public Kpi Kpi { get; set; } = null!;

    public decimal Progress { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }

    public Guid UpdatedById { get; set; }
    public User UpdatedBy { get; set; } = null!;
}
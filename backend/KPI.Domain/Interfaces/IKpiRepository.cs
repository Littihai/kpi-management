using KPI.Domain.Entities;

namespace KPI.Domain.Interfaces;

public interface IKpiRepository
{
    Task<List<Kpi>> GetAllAsync();
    Task<List<Kpi>> GetByDepartmentAsync(Guid departmentId);
    Task<Kpi?> GetByIdAsync(Guid id);
    Task<Kpi> CreateAsync(Kpi kpi);
    /// <summary>Saves changes to a tracked KPI; throws DbUpdateConcurrencyException if it changed meanwhile.</summary>
    Task<Kpi> UpdateAsync(Kpi kpi);
    Task DeleteAsync(Kpi kpi);
    Task AddProgressLogAsync(KpiProgressLog log);
}

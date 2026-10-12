namespace KPI.Application.Kpis;

/// <summary>
/// Every method takes the acting user and enforces permission, scope (department / ownership)
/// and KPI status rules, throwing <c>AppException</c> subclasses on failure.
/// </summary>
public interface IKpiService
{
    Task<List<KpiDto>> GetAllAsync(Guid userId);
    Task<KpiDto> GetByIdAsync(Guid id, Guid userId);
    Task<KpiDto> CreateAsync(CreateKpiRequest request, Guid userId);
    Task<KpiDto> UpdateAsync(Guid id, UpdateKpiRequest request, Guid userId);
    Task DeleteAsync(Guid id, Guid userId);
    Task<KpiDto> SubmitForApprovalAsync(Guid id, Guid userId);
    Task<KpiDto> ApproveAsync(Guid id, Guid userId);
    Task<KpiDto> RejectAsync(Guid id, string reason, Guid userId);
    Task<KpiDto> CloseAsync(Guid id, Guid userId);
    Task<KpiDto> UpdateProgressAsync(UpdateKpiProgressRequest request, Guid userId);
}

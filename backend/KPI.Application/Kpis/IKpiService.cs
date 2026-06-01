namespace KPI.Application.Kpis;

public interface IKpiService
{
    Task<List<KpiDto>> GetAllAsync(Guid userId);
    Task<KpiDto?> GetByIdAsync(Guid id);
    Task<KpiDto> CreateAsync(CreateKpiRequest request, Guid ownerId);
    Task<KpiDto> SubmitForApprovalAsync(Guid kpiId, Guid userId);
    Task<KpiDto> ApproveAsync(Guid kpiId, Guid approverId);
    Task<KpiDto> UpdateProgressAsync(UpdateKpiProgressRequest request, Guid userId);
}
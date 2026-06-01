using KPI.Application.Kpis;
using KPI.Domain.Entities;
using KPI.Domain.Enums;
using KPI.Domain.Interfaces;

namespace KPI.Infrastructure.Services;

public class KpiService : IKpiService
{
    private readonly IKpiRepository _kpiRepo;

    public KpiService(IKpiRepository kpiRepo)
    {
        _kpiRepo = kpiRepo;
    }

    public async Task<List<KpiDto>> GetAllAsync(Guid userId)
    {
        var kpis = await _kpiRepo.GetAllAsync();
        return kpis.Select(ToDto).ToList();
    }

    public async Task<KpiDto?> GetByIdAsync(Guid id)
    {
        var kpi = await _kpiRepo.GetByIdAsync(id);
        return kpi == null ? null : ToDto(kpi);
    }

    public async Task<KpiDto> CreateAsync(CreateKpiRequest request, Guid ownerId)
    {
        var kpi = new Kpi
{
			Name = request.Name,
			Description = request.Description,
			Year = request.Year,
			Target = request.Target,
			StartDate = DateTime.SpecifyKind(request.StartDate, DateTimeKind.Utc),
			DueDate = DateTime.SpecifyKind(request.DueDate, DateTimeKind.Utc),
			DepartmentId = request.DepartmentId,
			OwnerId = ownerId,
			Status = KpiStatus.Draft
		};

        var created = await _kpiRepo.CreateAsync(kpi);
        var full = await _kpiRepo.GetByIdAsync(created.Id);
        return ToDto(full!);
    }

    public async Task<KpiDto> SubmitForApprovalAsync(Guid kpiId, Guid userId)
    {
        var kpi = await _kpiRepo.GetByIdAsync(kpiId)
            ?? throw new Exception("KPI not found");

        if (kpi.Status != KpiStatus.Draft)
            throw new Exception("Only draft KPIs can be submitted");

        kpi.Status = KpiStatus.PendingApproval;
        await _kpiRepo.UpdateAsync(kpi);
        return ToDto(kpi);
    }

    public async Task<KpiDto> ApproveAsync(Guid kpiId, Guid approverId)
    {
        var kpi = await _kpiRepo.GetByIdAsync(kpiId)
            ?? throw new Exception("KPI not found");

        if (kpi.Status != KpiStatus.PendingApproval)
            throw new Exception("Only pending KPIs can be approved");

        kpi.Status = KpiStatus.Active;
        kpi.ApprovedById = approverId;
        kpi.ApprovedAt = DateTime.UtcNow;
        await _kpiRepo.UpdateAsync(kpi);
        return ToDto(kpi);
    }

    public async Task<KpiDto> UpdateProgressAsync(UpdateKpiProgressRequest request, Guid userId)
	{
		var kpi = await _kpiRepo.GetByIdAsync(request.KpiId)
			?? throw new Exception("KPI not found");

		kpi.Progress = request.Progress;
		kpi.Status = DetectStatus(kpi);

		await _kpiRepo.UpdateAsync(kpi);

		await _kpiRepo.AddProgressLogAsync(new KpiProgressLog
		{
			KpiId = kpi.Id,
			Progress = request.Progress,
			Notes = request.Notes,
			Month = DateTime.UtcNow.Month,
			Year = DateTime.UtcNow.Year,
			UpdatedById = userId
		});

		return ToDto(kpi);
	}

    // Progress Engine
    private static KpiStatus DetectStatus(Kpi kpi)
    {
        if (kpi.Progress >= kpi.Target) return KpiStatus.Completed;

        var today = DateTime.UtcNow;
        if (today > kpi.DueDate && kpi.Progress < kpi.Target)
            return KpiStatus.Delayed;

        var totalDays = (kpi.DueDate - kpi.StartDate).TotalDays;
        var elapsedDays = (today - kpi.StartDate).TotalDays;
        var expectedProgress = totalDays > 0
            ? (decimal)(elapsedDays / totalDays) * kpi.Target
            : 0;

        if (kpi.Progress < expectedProgress - 15) return KpiStatus.AtRisk;

        return KpiStatus.Active;
    }

    // Traffic Light
    private static string GetTrafficLight(Kpi kpi) => kpi.Status switch
    {
        KpiStatus.Active => "green",
        KpiStatus.AtRisk => "yellow",
        KpiStatus.Delayed => "red",
        KpiStatus.Completed => "green",
        _ => "gray"
    };

    private static KpiDto ToDto(Kpi kpi) => new(
        Id: kpi.Id,
        Name: kpi.Name,
        Description: kpi.Description,
        Year: kpi.Year,
        Target: kpi.Target,
        Progress: kpi.Progress,
        Status: kpi.Status,
        StatusLabel: kpi.Status.ToString(),
        TrafficLight: GetTrafficLight(kpi),
        StartDate: kpi.StartDate,
        DueDate: kpi.DueDate,
        DepartmentName: kpi.Department?.Name ?? "",
        OwnerName: kpi.Owner != null
            ? kpi.Owner.FirstName + " " + kpi.Owner.LastName
            : ""
    );
}
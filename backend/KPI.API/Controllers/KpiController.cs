using KPI.API.Security;
using KPI.Application.Kpis;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KpiController : ControllerBase
{
    private readonly IKpiService _kpiService;

    public KpiController(IKpiService kpiService)
    {
        _kpiService = kpiService;
    }

    private Guid GetUserId() => User.GetUserId();

    [HttpGet]
    [Authorize(Policy = Permissions.Kpi.View)]
    public async Task<IActionResult> GetAll()
        => Ok(await _kpiService.GetAllAsync(GetUserId()));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Kpi.View)]
    public async Task<IActionResult> GetById(Guid id)
        => Ok(await _kpiService.GetByIdAsync(id, GetUserId()));

    [HttpPost]
    [Authorize(Policy = Permissions.Kpi.Create)]
    public async Task<IActionResult> Create(CreateKpiRequest request)
    {
        var result = await _kpiService.CreateAsync(request, GetUserId());
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Kpi.Edit)]
    public async Task<IActionResult> Update(Guid id, UpdateKpiRequest request)
        => Ok(await _kpiService.UpdateAsync(id, request, GetUserId()));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Kpi.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _kpiService.DeleteAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = Permissions.Kpi.Submit)]
    public async Task<IActionResult> Submit(Guid id)
        => Ok(await _kpiService.SubmitForApprovalAsync(id, GetUserId()));

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = Permissions.Kpi.Approve)]
    public async Task<IActionResult> Approve(Guid id)
        => Ok(await _kpiService.ApproveAsync(id, GetUserId()));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Permissions.Kpi.Reject)]
    public async Task<IActionResult> Reject(Guid id, RejectKpiRequest request)
        => Ok(await _kpiService.RejectAsync(id, request.Reason, GetUserId()));

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = Permissions.Kpi.Close)]
    public async Task<IActionResult> Close(Guid id)
        => Ok(await _kpiService.CloseAsync(id, GetUserId()));

    [HttpPost("progress")]
    [Authorize(Policy = Permissions.Kpi.Progress)]
    public async Task<IActionResult> UpdateProgress(UpdateKpiProgressRequest request)
        => Ok(await _kpiService.UpdateProgressAsync(request, GetUserId()));
}

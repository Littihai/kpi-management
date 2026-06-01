using System.Security.Claims;
using KPI.Application.Kpis;
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

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _kpiService.GetAllAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _kpiService.GetByIdAsync(id);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateKpiRequest request)
    {
        try
        {
            var result = await _kpiService.CreateAsync(request, GetUserId());
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(Guid id)
    {
        try
        {
            var result = await _kpiService.SubmitForApprovalAsync(id, GetUserId());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(Guid id)
    {
        try
        {
            var result = await _kpiService.ApproveAsync(id, GetUserId());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("progress")]
    public async Task<IActionResult> UpdateProgress(UpdateKpiProgressRequest request)
    {
        try
        {
            var result = await _kpiService.UpdateProgressAsync(request, GetUserId());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
using KPI.Application.Admin;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

/// <summary>The system-wide overview and request log. SuperAdmin only.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Overview.View)]
public class AdminController : ControllerBase
{
    private readonly IOverviewService _overview;
    private readonly IPresenceTracker _presence;

    public AdminController(IOverviewService overview, IPresenceTracker presence)
    {
        _overview = overview;
        _presence = presence;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] int days = 7)
    {
        var result = await _overview.GetAsync(days);
        return Ok(result with { Online = _presence.Snapshot() });
    }

    [HttpGet("request-logs")]
    public async Task<IActionResult> RequestLogs([FromQuery] RequestLogQuery query)
        => Ok(await _overview.RequestLogsAsync(query));
}

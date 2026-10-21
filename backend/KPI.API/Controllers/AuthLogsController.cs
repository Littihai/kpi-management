using KPI.Application.Auth;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

/// <summary>Who signed in, who failed and why, and password changes. Read-only, for administrators.</summary>
[ApiController]
[Route("api/auth-logs")]
[Authorize(Policy = Permissions.Audit.View)]
public class AuthLogsController : ControllerBase
{
    private readonly IAuthLog _log;

    public AuthLogsController(IAuthLog log) => _log = log;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] AuthLogQuery query) => Ok(await _log.QueryAsync(query));

    [HttpGet("events")]
    public IActionResult Events() => Ok(AuthEvents.All);
}

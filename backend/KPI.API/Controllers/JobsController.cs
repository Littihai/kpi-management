using Hangfire;
using KPI.Application.Jobs;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Job.Trigger)]
public class JobsController : ControllerBase
{
    /// <summary>Runs the daily housekeeping now (auto-close of answered issues, purge of old request logs).</summary>
    [HttpPost("trigger-maintenance")]
    public IActionResult TriggerMaintenance()
    {
        BackgroundJob.Enqueue<IMaintenanceJob>(job => job.RunAsync());
        return Ok(new { message = "Maintenance job triggered" });
    }
}

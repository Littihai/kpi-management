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
    [HttpPost("trigger-delay-detection")]
    public IActionResult TriggerDelayDetection()
    {
        BackgroundJob.Enqueue<IDelayDetectionJob>(job => job.RunAsync());
        return Ok(new { message = "Delay detection job triggered" });
    }
}
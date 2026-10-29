using KPI.API.Security;
using KPI.Application.Issues;
using KPI.Application.Settings;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KPI.API.Controllers;

/// <summary>Endpoints that work without signing in, so someone who cannot log in can still ask for help.</summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    private const long MaxRequestBytes = 30 * 1024 * 1024;

    private readonly IIssueService _issues;
    private readonly AppDbContext _db;
    private readonly PublicSubmitThrottle _throttle;
    private readonly PublicIntakeBudget _budget;
    private readonly ISystemSettings _settings;
    private readonly string? _ipHeader;

    public PublicController(
        IIssueService issues,
        AppDbContext db,
        PublicSubmitThrottle throttle,
        PublicIntakeBudget budget,
        ISystemSettings settings,
        IOptions<LoginThrottleOptions> options)
    {
        _issues = issues;
        _settings = settings;
        _db = db;
        _throttle = throttle;
        _budget = budget;
        _ipHeader = options.Value.ClientIpHeader;
    }

    /// <summary>Department names for the report form's dropdown. Names only.</summary>
    [HttpGet("departments")]
    public async Task<IActionResult> Departments()
        => Ok(await _db.ListActiveAsync());

    [HttpPost("issues")]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> ReportIssue([FromForm] PublicIssueForm form)
    {
        var ip = LoginThrottle.ClientIp(HttpContext, _ipHeader);

        // Hidden field that people never fill in: anything in it is a bot. Answer as if it worked.
        if (!string.IsNullOrWhiteSpace(form.Website))
            return Ok(new PublicIssueReceipt("ISS-0000-0000", DateTime.UtcNow, DateTime.UtcNow));

        if (_throttle.TryAcquire(ip) is { } wait)
            throw new TooManyRequestsException(
                $"ส่งเรื่องบ่อยเกินไป กรุณาลองใหม่อีกครั้งใน {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} นาที", wait);

        var request = new PublicIssueRequest(
            form.ReporterName, form.ReporterEmail, form.ReporterPhone, form.DepartmentId,
            form.Title, form.Description, form.Category, form.Module, form.Impact,
            Request.Headers.UserAgent.ToString(), form.ClientInfo);

        // Ask for room before reading the files into memory, using the size the browser declared (a little more than the files
        // themselves); a chunked upload declares none, so it is counted by what was actually read.
        var declared = form.Files is { Count: > 0 } ? Request.ContentLength : null;
        var room = declared is { } d ? await _budget.ReserveAsync(d, ip) : null;
        try
        {
            var uploads = await UploadReader.ReadAsync(form.Files, _settings);
            // The room is held until the report is saved, so reports arriving together cannot all slip under the ceiling.
            room ??= await _budget.ReserveAsync(uploads.Sum(u => (long)u.Data.Length), ip);

            var receipt = await _issues.CreatePublicAsync(request, uploads, ip);
            room.MarkSaved();
            return Ok(receipt);
        }
        finally
        {
            room?.Dispose();
        }
    }

    public class PublicIssueForm
    {
        public string ReporterName { get; set; } = string.Empty;
        public string? ReporterEmail { get; set; }
        public string? ReporterPhone { get; set; }
        public Guid? DepartmentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IssueCategory Category { get; set; }
        public IssueModule Module { get; set; }
        public IssueImpact Impact { get; set; }
        public string? ClientInfo { get; set; }
        public string? Website { get; set; }   // honeypot
        public IFormFileCollection? Files { get; set; }
    }
}

using KPI.API.Security;
using KPI.Application.Issues;
using KPI.Application.Settings;
using KPI.Domain.Constants;
using KPI.Domain.Issues;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IssuesController : ControllerBase
{
    private const long MaxRequestBytes = 30 * 1024 * 1024;

    private readonly IIssueService _issues;
    private readonly ISystemSettings _settings;
    private readonly string? _ipHeader;

    public IssuesController(IIssueService issues, ISystemSettings settings, IOptions<LoginThrottleOptions> options)
    {
        _issues = issues;
        _settings = settings;
        _ipHeader = options.Value.ClientIpHeader;
    }

    private Guid UserId => User.GetUserId();

    [HttpGet]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> List([FromQuery] IssueQuery query)
        => Ok(await _issues.ListAsync(query, UserId));

    [HttpGet("summary")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Summary()
        => Ok(await _issues.SummaryAsync(UserId));

    [HttpGet("attention")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Attention()
        => Ok(await _issues.AttentionAsync(UserId));

    [HttpGet("assignees")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> Assignees()
        => Ok(await _issues.AssigneesAsync(UserId));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Get(Guid id)
        => Ok(await _issues.GetAsync(id, UserId));

    [HttpPost]
    [Authorize(Policy = Permissions.Issue.Create)]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Create([FromForm] IssueForm form)
    {
        var request = new CreateIssueRequest(
            form.Title, form.Description, form.Category, form.Module, form.Impact,
            form.PageUrl, Request.Headers.UserAgent.ToString(), form.ClientInfo, form.ErrorRequestId);

        var ip = LoginThrottle.ClientIp(HttpContext, _ipHeader);
        var created = await _issues.CreateAsync(request, await UploadReader.ReadAsync(form.Files, _settings), UserId, ip);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/comments")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Comment(Guid id, CommentRequest request)
        => Ok(await _issues.CommentAsync(id, request.Body, request.InternalNote, UserId));

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> Assign(Guid id, AssignRequest request)
        => Ok(await _issues.AssignAsync(id, request.AssigneeId, UserId));

    [HttpPost("{id:guid}/start")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> Start(Guid id)
        => Ok(await _issues.StartAsync(id, UserId));

    [HttpPost("{id:guid}/request-info")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> RequestInfo(Guid id, MessageRequest request)
        => Ok(await _issues.RequestInfoAsync(id, request.Message, UserId));

    [HttpPost("{id:guid}/hold")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> Hold(Guid id, ReasonRequest request)
        => Ok(await _issues.HoldAsync(id, request.Reason, UserId));

    [HttpPost("{id:guid}/resume")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> Resume(Guid id, MessageRequest request)
        => Ok(await _issues.ResumeAsync(id, request.Message, UserId));

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> Resolve(Guid id, ResolveIssueRequest request)
        => Ok(await _issues.ResolveAsync(id, request, UserId));

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Confirm(Guid id)
        => Ok(await _issues.ConfirmAsync(id, UserId));

    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Reopen(Guid id, ReasonRequest request)
        => Ok(await _issues.ReopenAsync(id, request.Reason, UserId));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> Reject(Guid id, RejectRequest request)
        => Ok(await _issues.RejectAsync(id, request.Reason, request.DuplicateOfId, UserId));

    [HttpPut("{id:guid}/priority")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> SetPriority(Guid id, PriorityRequest request)
        => Ok(await _issues.SetPriorityAsync(id, request.Priority, UserId));

    [HttpPost("{id:guid}/feedback")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Feedback(Guid id, FeedbackRequest request)
        => Ok(await _issues.FeedbackAsync(id, request.Rating, request.Note, UserId));

    [HttpGet("feedback/mine")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> MyFeedback()
        => Ok(await _issues.MyFeedbackAsync(UserId));

    [HttpGet("feedback")]
    [Authorize(Policy = Permissions.Issue.Manage)]
    public async Task<IActionResult> FeedbackReport([FromQuery] FeedbackReportQuery query)
        => Ok(await _issues.FeedbackReportAsync(query, UserId));

    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = Permissions.Issue.View)]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Attach(Guid id, [FromForm] IFormFileCollection files)
        => Ok(await _issues.AddAttachmentsAsync(id, await UploadReader.ReadAsync(files, _settings), UserId));

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Download(Guid id, Guid attachmentId)
    {
        var file = await _issues.GetAttachmentAsync(id, attachmentId, UserId);
        // Never let the browser guess a type or run it as a page.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(file.Data, file.ContentType, enableRangeProcessing: false);
    }

    public class IssueForm
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IssueCategory Category { get; set; }
        public IssueModule Module { get; set; }
        public IssueImpact Impact { get; set; }
        public string? PageUrl { get; set; }
        public string? ClientInfo { get; set; }
        public string? ErrorRequestId { get; set; }
        public IFormFileCollection? Files { get; set; }
    }

    public record CommentRequest(string Body, bool InternalNote = false);
    public record AssignRequest(Guid AssigneeId);
    public record MessageRequest(string Message);
    public record ReasonRequest(string Reason);
    public record RejectRequest(string Reason, Guid? DuplicateOfId = null);
    public record PriorityRequest(IssuePriority Priority);
    public record FeedbackRequest(int Rating, string? Note = null);
}

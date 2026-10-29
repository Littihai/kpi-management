using KPI.API.Security;
using KPI.Application.Notifications;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

/// <summary>The caller's own notification centre; nothing here can name another person.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    private Guid UserId => User.GetUserId();

    [HttpGet]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> List([FromQuery] NotificationQuery query)
        => Ok(await _notifications.ListAsync(query, UserId));

    [HttpGet("unread-count")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> UnreadCount()
        => Ok(new { count = await _notifications.UnreadCountAsync(UserId) });

    [HttpPost("{id:guid}/read")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        await _notifications.MarkReadAsync(id, UserId);
        return NoContent();
    }

    [HttpPost("read-all")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> MarkAllRead()
        => Ok(new { marked = await _notifications.MarkAllReadAsync(UserId) });

    [HttpPost("read-issue/{issueId:guid}")]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> MarkIssueRead(Guid issueId)
        => Ok(new { marked = await _notifications.MarkIssueReadAsync(issueId, UserId) });
}

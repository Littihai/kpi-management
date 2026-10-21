using KPI.API.Security;
using KPI.Application.Notifications;
using KPI.Application.Settings;
using KPI.Domain.Exceptions;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Settings.View)]
public class SettingsController : ControllerBase
{
    private readonly ISettingsAdminService _settings;
    private readonly IEmailService _email;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(ISettingsAdminService settings, IEmailService email, ILogger<SettingsController> logger)
    {
        _settings = settings;
        _email = email;
        _logger = logger;
    }

    /// <summary>Every setting with its current value, default and limits.</summary>
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _settings.ListAsync());

    [HttpGet("{key}/history")]
    public async Task<IActionResult> History(string key) => Ok(await _settings.HistoryAsync(key));

    [HttpPut("{key}")]
    [Authorize(Policy = Permissions.Settings.Manage)]
    public async Task<IActionResult> Update(string key, [FromBody] SaveSettingRequest request)
        => Ok(await _settings.UpdateAsync(User.GetUserId(), key, request));

    /// <summary>Puts a setting back to its default.</summary>
    [HttpDelete("{key}")]
    [Authorize(Policy = Permissions.Settings.Manage)]
    public async Task<IActionResult> Reset(string key) => Ok(await _settings.ResetAsync(User.GetUserId(), key));

    /// <summary>Sends a test message through the saved e-mail settings so an administrator can see they work.</summary>
    [HttpPost("email/test")]
    [Authorize(Policy = Permissions.Settings.Manage)]
    public async Task<IActionResult> SendTestEmail([FromBody] SendTestEmailRequest request, CancellationToken cancellationToken)
    {
        var to = (request.To ?? string.Empty).Trim();
        if (to.Length == 0 || !System.Net.Mail.MailAddress.TryCreate(to, out var address) || address.Address != to)
            throw new RequestValidationException("ใส่อีเมลผู้รับให้ถูกต้อง");

        try
        {
            await _email.SendTestAsync(to, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Test e-mail to {To} failed", to);
            // The reason from the mail server is what the administrator needs to fix the settings.
            throw new RequestValidationException("ส่งอีเมลทดสอบไม่สำเร็จ: " + (ex.Message.Length > 300 ? ex.Message[..300] : ex.Message));
        }
        return Ok(new { message = "ส่งอีเมลทดสอบแล้ว ตรวจสอบกล่องจดหมายของผู้รับ" });
    }
}

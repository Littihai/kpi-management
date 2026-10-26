using KPI.API.Security;
using KPI.Application.Notifications;
using KPI.Application.Settings;
using KPI.Domain.Exceptions;
using KPI.Domain.Constants;
using KPI.Domain.Settings;
using KPI.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Settings.View)]
public class SettingsController : ControllerBase
{
    private readonly ISettingsAdminService _settings;
    private readonly ISystemSettings _systemSettings;
    private readonly IEmailService _email;
    private readonly IEmailDiagnostics _diagnostics;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ISettingsAdminService settings, ISystemSettings systemSettings, IEmailService email, IEmailDiagnostics diagnostics,
        ILogger<SettingsController> logger)
    {
        _settings = settings;
        _systemSettings = systemSettings;
        _email = email;
        _diagnostics = diagnostics;
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
            // What to fix, in words, with the mail server's own reason in brackets for whoever has to dig further.
            throw new RequestValidationException("ส่งอีเมลทดสอบไม่สำเร็จ: " + await DescribeFailureAsync(ex));
        }
        return Ok(new { message = "ส่งอีเมลทดสอบแล้ว ตรวจสอบกล่องจดหมายของผู้รับ" });
    }

    /// <summary>
    /// Checks the saved e-mail settings step by step (name lookup, port, greeting, encryption) and says where it stops
    /// working. It never signs in and never sends anything, so it is safe to run at any time.
    /// </summary>
    [HttpPost("email/diagnose")]
    [Authorize(Policy = Permissions.Settings.Manage)]
    public async Task<IActionResult> DiagnoseEmail(CancellationToken cancellationToken)
        => Ok(await _diagnostics.DiagnoseAsync(cancellationToken));

    private async Task<string> DescribeFailureAsync(Exception exception) => EmailErrors.Describe(
        exception,
        await _systemSettings.GetAsync(SettingCatalog.Keys.SmtpHost),
        (int)await _systemSettings.GetNumberAsync(SettingCatalog.Keys.SmtpPort),
        await _systemSettings.GetAsync(SettingCatalog.Keys.SmtpSecurity));
}

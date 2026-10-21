using KPI.API.Security;
using KPI.Application.Settings;
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

    public SettingsController(ISettingsAdminService settings) => _settings = settings;

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
}

using System.Text.RegularExpressions;
using KPI.Application.Settings;
using KPI.Domain.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

/// <summary>Tells the desktop app which versions are allowed / current, before anyone signs in.</summary>
[ApiController]
[Route("api/client-config")]
[AllowAnonymous]
public partial class ClientConfigController : ControllerBase
{
    public const string DefaultMinimum = "1.0.0";

    private readonly IConfiguration _config;
    private readonly ISystemSettings _settings;

    public ClientConfigController(IConfiguration config, ISystemSettings settings)
    {
        _config = config;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            minDesktopVersion = NormalizeVersion(_config["Client:MinDesktopVersion"]) ?? DefaultMinimum,
            latestDesktopVersion = NormalizeVersion(_config["Client:LatestDesktopVersion"]),
            companyName = CompanyName(await _settings.GetAsync(SettingCatalog.Keys.CompanyName), _config),
        });
    }

    /// <summary>The name an administrator set in Settings, else the one the installer was given (App:CompanyName), else empty (the app then says just "Helpdesk"). One line, at most 80 characters.</summary>
    public static string CompanyName(string? setting, IConfiguration config)
    {
        foreach (var candidate in new[] { setting, config["App:CompanyName"] })
        {
            var value = (candidate ?? string.Empty).Trim();
            if (value.Length > 0 && !value.Any(char.IsControl)) return value.Length > 80 ? value[..80] : value;
        }
        return string.Empty;
    }

    /// <summary>"v1.2" / "1.2.3-beta.1" become "1.2.0" / "1.2.3"; anything else is ignored (null).</summary>
    public static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = VersionPattern().Match(raw.Trim());
        if (!m.Success) return null;
        string Part(string g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value).ToString() : "0";
        return $"{Part("a")}.{Part("b")}.{Part("c")}";
    }

    [GeneratedRegex(@"^v?(?<a>\d{1,5})(?:\.(?<b>\d{1,5}))?(?:\.(?<c>\d{1,5}))?(?:[-+][0-9A-Za-z.\-+]{1,40})?$")]
    private static partial Regex VersionPattern();
}

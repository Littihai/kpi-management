using System.Text.RegularExpressions;
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

    public ClientConfigController(IConfiguration config) => _config = config;

    [HttpGet]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            minDesktopVersion = NormalizeVersion(_config["Client:MinDesktopVersion"]) ?? DefaultMinimum,
            latestDesktopVersion = NormalizeVersion(_config["Client:LatestDesktopVersion"]),
        });
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

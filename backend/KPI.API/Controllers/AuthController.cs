using System.Security.Claims;
using KPI.Application.Auth;
using KPI.API.Security;
using KPI.Domain.Constants;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly LoginThrottle _throttle;
    private readonly LoginThrottleOptions _throttleOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        LoginThrottle throttle,
        IOptions<LoginThrottleOptions> throttleOptions,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _throttle = throttle;
        _throttleOptions = throttleOptions.Value;
        _logger = logger;
    }

    /// <summary>Creates a Staff account. No longer public: only users who may create users can call it.</summary>
    [HttpPost("register")]
    [Authorize(Policy = Permissions.User.Create)]
    public async Task<IActionResult> Register(RegisterRequest request)
        => Ok(await _authService.RegisterAsync(request));

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var ip = LoginThrottle.ClientIp(HttpContext, _throttleOptions.ClientIpHeader);
        var email = AuthService.NormalizeEmail(request.Email);

        if (_throttle.BlockedFor(ip, email) is { } wait)
        {
            _logger.LogWarning("Sign-in blocked for {Ip}: too many failed attempts, retry in {Seconds}s",
                ip, (int)wait.TotalSeconds);
            throw new TooManyRequestsException(
                $"Too many failed sign-in attempts. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minute(s).",
                wait);
        }

        try
        {
            var response = await _authService.LoginAsync(request);
            _throttle.RecordSuccess(ip, email);
            return Ok(response);
        }
        catch (RequestValidationException)
        {
            // LoginAsync only throws this for wrong credentials or a deactivated account.
            _throttle.RecordFailure(ip, email);
            throw;
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await _authService.GetProfileAsync(userId));
    }
}

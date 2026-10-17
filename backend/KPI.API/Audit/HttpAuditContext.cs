using System.Security.Claims;
using KPI.API.Security;
using KPI.Application.Audit;
using Microsoft.Extensions.Options;

namespace KPI.API.Audit;

/// <summary>Reads who and where from the current HTTP request. Empty outside a request (background jobs).</summary>
public sealed class HttpAuditContext : IAuditContext
{
    public const string DepartmentClaim = "dept";

    private readonly IHttpContextAccessor _accessor;
    private readonly string? _ipHeader;

    public HttpAuditContext(IHttpContextAccessor accessor, IOptions<LoginThrottleOptions> options)
    {
        _accessor = accessor;
        _ipHeader = options.Value.ClientIpHeader;
    }

    private HttpContext? Http => _accessor.HttpContext;

    public string? IpAddress => Http is null ? null : LoginThrottle.ClientIp(Http, _ipHeader);
    public string? UserAgent => Http?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
    public string? RequestId => Http?.TraceIdentifier;
    public string? UserName => Http?.User.FindFirstValue(ClaimTypes.Name);
    public string? UserRole => Http?.User.FindFirstValue(ClaimTypes.Role);
    public string? DepartmentName => Http?.User.FindFirstValue(DepartmentClaim);
}

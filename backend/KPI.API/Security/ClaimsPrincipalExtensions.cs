using System.Security.Claims;

namespace KPI.API.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The id of the signed-in user. Only call it behind [Authorize]; the token always carries it.</summary>
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

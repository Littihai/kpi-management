using System.Security.Claims;
using KPI.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace KPI.API.Authorization;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserRepository _userRepository;

    public PermissionHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id)) return;

        // Permissions are read from the database on every request, so role changes and
        // deactivation take effect immediately instead of when the 7-day JWT expires.
        var user = await _userRepository.GetByIdWithPermissionsAsync(id);
        if (user is not { IsActive: true }) return;

        var hasPermission = user.Role?.RolePermissions
            .Any(rp => rp.Permission.Name == requirement.Permission);

        if (hasPermission == true)
            context.Succeed(requirement);
    }
}

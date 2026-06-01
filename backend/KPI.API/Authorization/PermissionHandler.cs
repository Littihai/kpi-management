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
        if (userId == null) return;

		var user = await _userRepository.GetByIdWithPermissionsAsync(Guid.Parse(userId));
        if (user == null) return;

        var hasPermission = user.Role?.RolePermissions
            .Any(rp => rp.Permission.Name == requirement.Permission);

        if (hasPermission == true)
            context.Succeed(requirement);
    }
}
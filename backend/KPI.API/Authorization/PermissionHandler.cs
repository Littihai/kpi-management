using System.Security.Claims;
using KPI.Application.Settings;
using KPI.Domain.Entities;
using KPI.Domain.Interfaces;
using KPI.Domain.Settings;
using Microsoft.AspNetCore.Authorization;

namespace KPI.API.Authorization;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserRepository _userRepository;
    private readonly ISystemSettings _settings;
    private readonly TimeProvider _time;

    public PermissionHandler(IUserRepository userRepository, ISystemSettings settings, TimeProvider time)
    {
        _userRepository = userRepository;
        _settings = settings;
        _time = time;
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

        // Someone who must choose a new password (forced, or expired) can only reach /api/auth/me and
        // /api/auth/change-password, which need no permission, until they have done so.
        var maxAge = await _settings.GetNumberAsync(SettingCatalog.Keys.PasswordMaxAgeDays);
        if (PasswordAging.ChangeReason(user, maxAge, _time.GetUtcNow().UtcDateTime) is not null) return;

        var hasPermission = user.Role?.RolePermissions
            .Any(rp => rp.Permission.Name == requirement.Permission);

        if (hasPermission == true)
            context.Succeed(requirement);
    }
}

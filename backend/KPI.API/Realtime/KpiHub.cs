using System.Security.Claims;
using KPI.API.Security;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KPI.API.Realtime;

/// <summary>
/// Server-to-client push only. Clients connect with their JWT and are placed in groups that
/// decide which KPI/issue events and notifications they receive.
/// </summary>
[Authorize]
public class KpiHub : Hub
{
    private readonly AppDbContext _context;
    private readonly PresenceTracker _presence;
    private readonly string? _ipHeader;

    public KpiHub(AppDbContext context, PresenceTracker presence, IOptions<LoginThrottleOptions> options)
    {
        _context = context;
        _presence = presence;
        _ipHeader = options.Value.ClientIpHeader;
    }

    public static string UserGroup(Guid userId) => $"user:{userId}";
    public static string DepartmentGroup(Guid departmentId) => $"dept:{departmentId}";
    public static string DepartmentManagersGroup(Guid departmentId) => $"dept-managers:{departmentId}";
    public const string AllDepartmentsGroup = "all-departments";
    public const string ItSupportGroup = "it-support";

    public override async Task OnConnectedAsync()
    {
        var idClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(idClaim, out var userId))
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

            if (user is not null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.Id));

                if (RoleNames.SeesAllDepartments(user.Role.Name))
                    await Groups.AddToGroupAsync(Context.ConnectionId, AllDepartmentsGroup);

                // The issue queue: IT Support works it, SuperAdmin oversees it.
                if (user.Role.Name is RoleNames.ITSupport or RoleNames.SuperAdmin)
                    await Groups.AddToGroupAsync(Context.ConnectionId, ItSupportGroup);

                if (user.DepartmentId is { } departmentId)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, DepartmentGroup(departmentId));
                    if (user.Role.Name == RoleNames.Manager)
                        await Groups.AddToGroupAsync(Context.ConnectionId, DepartmentManagersGroup(departmentId));
                }

                var http = Context.GetHttpContext();
                _presence.Connected(Context.ConnectionId, user.Id, $"{user.FirstName} {user.LastName}".Trim(),
                    user.Role.Name, http is null ? null : LoginThrottle.ClientIp(http, _ipHeader));
            }
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

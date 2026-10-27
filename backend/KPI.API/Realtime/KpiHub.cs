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
    private readonly HubAccessSync _access;
    private readonly PresenceTracker _presence;
    private readonly HubConnectionRegistry _registry;
    private readonly string? _ipHeader;

    public KpiHub(HubAccessSync access, PresenceTracker presence, HubConnectionRegistry registry, IOptions<LoginThrottleOptions> options)
    {
        _access = access;
        _presence = presence;
        _registry = registry;
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
            // Registered before the groups are decided, so an access change from now on finds this connection. The groups
            // are then read and applied in the same one-at-a-time turn as such a change (see HubAccessSync), so a change
            // saved while this connection opens is not missed. A person who is deactivated still gets a (groupless)
            // connection, which is moved into place if they are reactivated while it stays open.
            _registry.Add(Context.ConnectionId, userId, Context);
            var user = await _access.AttachAsync(Context.ConnectionId);

            if (user is not null)
            {
                var http = Context.GetHttpContext();
                _presence.Connected(Context.ConnectionId, user.Id, user.FullName,
                    user.Role.Name, http is null ? null : LoginThrottle.ClientIp(http, _ipHeader));
            }
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _presence.Disconnected(Context.ConnectionId);
        _registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

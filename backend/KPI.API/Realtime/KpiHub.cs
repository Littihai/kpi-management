using System.Security.Claims;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Realtime;

/// <summary>
/// Server-to-client push only. Clients connect with their JWT and are placed in groups that
/// decide which KPI events and notifications they receive.
/// </summary>
[Authorize]
public class KpiHub : Hub
{
    private readonly AppDbContext _context;

    public KpiHub(AppDbContext context)
    {
        _context = context;
    }

    public static string UserGroup(Guid userId) => $"user:{userId}";
    public static string DepartmentGroup(Guid departmentId) => $"dept:{departmentId}";
    public static string DepartmentManagersGroup(Guid departmentId) => $"dept-managers:{departmentId}";
    public const string AllDepartmentsGroup = "all-departments";

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

                if (user.DepartmentId is { } departmentId)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, DepartmentGroup(departmentId));
                    if (user.Role.Name == RoleNames.Manager)
                        await Groups.AddToGroupAsync(Context.ConnectionId, DepartmentManagersGroup(departmentId));
                }
            }
        }

        await base.OnConnectedAsync();
    }
}

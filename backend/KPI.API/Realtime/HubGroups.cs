using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Realtime;

/// <summary>Which real-time groups a person belongs in right now. Used when they connect and again whenever their access changes.</summary>
public static class HubGroups
{
    /// <summary>The person (null when they do not exist or are deactivated) and the groups that go with them.</summary>
    public static async Task<(User? User, List<string> Groups)> ForUserAsync(AppDbContext db, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .Include(u => u.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (user is null) return (null, []);

        var groups = new List<string> { KpiHub.UserGroup(user.Id) };

        if (user.Role.SeesAllDepartments)
            groups.Add(KpiHub.AllDepartmentsGroup);

        // The issue queue: whoever may work the issues (IT Support, SuperAdmin, or a custom role with that right).
        if (await db.RolePermissions.AnyAsync(rp => rp.RoleId == user.RoleId && rp.Permission.Name == Permissions.Issue.Manage, cancellationToken))
            groups.Add(KpiHub.ItSupportGroup);

        if (user.DepartmentId is { } departmentId)
        {
            groups.Add(KpiHub.DepartmentGroup(departmentId));
            if (user.Role.IssueVisibility == IssueVisibility.Department)
                groups.Add(KpiHub.DepartmentManagersGroup(departmentId));
        }

        return (user, groups);
    }
}

using KPI.Domain.Constants;
using KPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

/// <summary>
/// Who works the issue queue: anyone whose role holds <c>issue.manage</c> (IT Support, SuperAdmin, or a custom role with that
/// right). One definition, used by the live push group and by the stored notifications, so the bell and the toast reach the same people.
/// </summary>
public static class IssueQueue
{
    public static Task<bool> RoleWorksAsync(AppDbContext db, Guid roleId) =>
        db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.Permission.Name == Permissions.Issue.Manage);

    /// <summary>Active people who work the queue.</summary>
    public static IQueryable<User> Workers(AppDbContext db) =>
        db.Users.Where(u => u.IsActive
            && u.Role.RolePermissions.Any(rp => rp.Permission.Name == Permissions.Issue.Manage));
}

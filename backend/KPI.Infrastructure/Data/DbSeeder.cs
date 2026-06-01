using KPI.Domain.Constants;
using KPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        await SeedRolesAsync(context);
        await SeedPermissionsAsync(context);
    }

    private static async Task SeedRolesAsync(AppDbContext context)
    {
        if (await context.Roles.AnyAsync()) return;

        var roles = new List<Role>
        {
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "SuperAdmin",  Description = "Full system access" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Director",    Description = "View all departments" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "Manager",     Description = "Manage department KPI" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000004"), Name = "TeamLeader",  Description = "Manage projects" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000005"), Name = "Staff",       Description = "Update assigned tasks" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000006"), Name = "Viewer",      Description = "Read only" },
        };

        context.Roles.AddRange(roles);
        await context.SaveChangesAsync();
    }

    private static async Task SeedPermissionsAsync(AppDbContext context)
    {
        if (await context.Permissions.AnyAsync()) return;

        var allPerms = new[]
        {
            Permissions.Kpi.View, Permissions.Kpi.Create,
            Permissions.Kpi.Edit, Permissions.Kpi.Delete, Permissions.Kpi.Approve,
            Permissions.Project.View, Permissions.Project.Create,
            Permissions.Project.Edit, Permissions.Project.Delete,
            Permissions.User.View, Permissions.User.Create,
            Permissions.User.Edit, Permissions.User.Delete,
            Permissions.Department.View, Permissions.Department.Manage,
        };

        var permissions = allPerms.Select(p => new Permission
        {
            Name = p,
            Module = p.Split('.')[0]
        }).ToList();

        context.Permissions.AddRange(permissions);
        await context.SaveChangesAsync();

        await SeedRolePermissionsAsync(context, permissions);
    }

    private static async Task SeedRolePermissionsAsync(
        AppDbContext context, List<Permission> permissions)
    {
        if (await context.RolePermissions.AnyAsync()) return;

        Permission Find(string name) =>
            permissions.First(p => p.Name == name);

        var rolePermissions = new List<RolePermission>();

        // SuperAdmin — ทุกอย่าง
        var superAdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        rolePermissions.AddRange(permissions.Select(p =>
            new RolePermission { RoleId = superAdminId, PermissionId = p.Id }));

        // Director — view ทุกอย่าง + approve kpi
        var directorId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var directorPerms = new[] {
            Permissions.Kpi.View, Permissions.Kpi.Approve,
            Permissions.Project.View, Permissions.User.View,
            Permissions.Department.View
        };
        rolePermissions.AddRange(directorPerms.Select(p =>
            new RolePermission { RoleId = directorId, PermissionId = Find(p).Id }));

        // Manager — manage kpi + view project
        var managerId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var managerPerms = new[] {
            Permissions.Kpi.View, Permissions.Kpi.Create, Permissions.Kpi.Edit,
            Permissions.Project.View, Permissions.Project.Create,
            Permissions.Department.View, Permissions.Department.Manage,
            Permissions.User.View
        };
        rolePermissions.AddRange(managerPerms.Select(p =>
            new RolePermission { RoleId = managerId, PermissionId = Find(p).Id }));

        // Staff — view + edit assigned
        var staffId = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var staffPerms = new[] {
            Permissions.Kpi.View,
            Permissions.Project.View,
            Permissions.Department.View
        };
        rolePermissions.AddRange(staffPerms.Select(p =>
            new RolePermission { RoleId = staffId, PermissionId = Find(p).Id }));

        context.RolePermissions.AddRange(rolePermissions);
        await context.SaveChangesAsync();
    }
}
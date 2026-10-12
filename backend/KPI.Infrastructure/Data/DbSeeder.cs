using KPI.Domain.Constants;
using KPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        await SeedRolesAsync(context);
        await SyncPermissionsAsync(context);
    }

    private static async Task SeedRolesAsync(AppDbContext context)
    {
        if (await context.Roles.AnyAsync()) return;

        var roles = new List<Role>
        {
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = RoleNames.SuperAdmin,  Description = "Full system access" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = RoleNames.Director,    Description = "View all departments" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = RoleNames.Manager,     Description = "Manage department KPI" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000004"), Name = RoleNames.TeamLeader,  Description = "Manage projects" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000005"), Name = RoleNames.Staff,       Description = "Update assigned tasks" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000006"), Name = RoleNames.Viewer,      Description = "Read only" },
        };

        context.Roles.AddRange(roles);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Idempotent: makes the Permissions table and each built-in role's RolePermissions match
    /// <see cref="RolePermissionMap.Defaults"/>. Runs at every startup, so adding a permission or
    /// changing a role's grants only needs a code change. Custom roles are left untouched.
    /// </summary>
    public static async Task SyncPermissionsAsync(AppDbContext context)
    {
        var existing = await context.Permissions.ToListAsync();
        var missing = Permissions.All
            .Where(name => existing.All(p => p.Name != name))
            .Select(name => new Permission { Name = name, Module = name.Split('.')[0] })
            .ToList();

        if (missing.Count > 0)
        {
            context.Permissions.AddRange(missing);
            await context.SaveChangesAsync();
            existing.AddRange(missing);
        }

        var permissionIdByName = existing.ToDictionary(p => p.Name, p => p.Id);
        var roles = await context.Roles.Include(r => r.RolePermissions).ToListAsync();

        foreach (var (roleName, wanted) in RolePermissionMap.Defaults)
        {
            var role = roles.FirstOrDefault(r => r.Name == roleName);
            if (role is null) continue;

            var wantedIds = wanted.Select(name => permissionIdByName[name]).ToHashSet();

            var toRemove = role.RolePermissions.Where(rp => !wantedIds.Contains(rp.PermissionId)).ToList();
            context.RolePermissions.RemoveRange(toRemove);

            var haveIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
            foreach (var permissionId in wantedIds.Where(id => !haveIds.Contains(id)))
                context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
        }

        await context.SaveChangesAsync();
    }
}

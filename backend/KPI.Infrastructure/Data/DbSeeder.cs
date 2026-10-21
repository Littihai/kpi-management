using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        await SeedRolesAsync(context);
        await EnsureRolesAsync(context);
        await SyncPermissionsAsync(context);
    }

    /// <summary>
    /// Creates the first SuperAdmin from the Admin__* settings so a brand-new database can be signed in to
    /// (public registration is closed). Does nothing when the settings are empty or an active SuperAdmin
    /// already exists, so leaving the settings in place later is harmless. Run it after <see cref="SeedAsync"/>.
    /// </summary>
    /// <returns>True when an account was created.</returns>
    /// <exception cref="InvalidOperationException">The settings are present but unusable.</exception>
    public static async Task<bool> SeedInitialAdminAsync(
        AppDbContext context, string? email, string? password, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrEmpty(password)) return false;

        if (await context.Users.AnyAsync(u => u.IsActive && u.Role.Name == RoleNames.SuperAdmin)) return false;

        var normalized = AuthService.NormalizeEmail(email);
        if (normalized.Length == 0 || !normalized.Contains('@'))
            throw new InvalidOperationException("Admin:Email must be a valid e-mail address.");

        try
        {
            AuthService.EnsurePasswordPolicy(password);
        }
        catch (RequestValidationException ex)
        {
            throw new InvalidOperationException($"Admin:Password is not usable. {ex.Message}");
        }

        if (await context.Users.AnyAsync(u => u.Email == normalized))
            throw new InvalidOperationException(
                "Admin:Email already belongs to a user who is not an active SuperAdmin. Use a different address.");

        var role = await context.Roles.FirstAsync(r => r.Name == RoleNames.SuperAdmin);
        context.Users.Add(new User
        {
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "System" : firstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(lastName) ? "Admin" : lastName.Trim(),
            Email = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = role.Id,
        });
        await context.SaveChangesAsync();
        return true;
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

    /// <summary>Adds built-in roles that older databases do not have yet (e.g. ITSupport), by name.</summary>
    private static async Task EnsureRolesAsync(AppDbContext context)
    {
        var existing = await context.Roles.Select(r => r.Name).ToListAsync();
        if (!existing.Contains(RoleNames.ITSupport))
        {
            context.Roles.Add(new Role
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000007"),
                Name = RoleNames.ITSupport,
                Description = "Works the issue queue",
            });
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Idempotent: makes the Permissions table and each built-in role's RolePermissions match
    /// <see cref="RolePermissionMap.Defaults"/>. Runs at every startup, so adding a permission or
    /// changing a role's grants only needs a code change. Custom roles are left untouched.
    /// </summary>
    public static async Task SyncPermissionsAsync(AppDbContext context)
    {
        var existing = await context.Permissions.ToListAsync();

        // Rights of features that were removed (kpi.*, project.*, export.view) must not linger in the role editor.
        var stale = existing.Where(p => !Permissions.All.Contains(p.Name)).ToList();
        if (stale.Count > 0)
        {
            var staleIds = stale.Select(p => p.Id).ToList();
            context.RolePermissions.RemoveRange(await context.RolePermissions.Where(rp => staleIds.Contains(rp.PermissionId)).ToListAsync());
            context.Permissions.RemoveRange(stale);
            existing.RemoveAll(p => staleIds.Contains(p.Id));
            await context.SaveChangesAsync();
        }
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
            if (RolePermissionMap.Scopes.TryGetValue(roleName, out var scope))
            {
                role.IssueVisibility = scope.Issues;
                role.SeesAllDepartments = scope.AllDepartments;
            }

            var toRemove = role.RolePermissions.Where(rp => !wantedIds.Contains(rp.PermissionId)).ToList();
            context.RolePermissions.RemoveRange(toRemove);

            var haveIds = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
            foreach (var permissionId in wantedIds.Where(id => !haveIds.Contains(id)))
                context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
        }

        await context.SaveChangesAsync();
    }
}

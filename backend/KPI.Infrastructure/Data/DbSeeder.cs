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

    /// <summary>A role the system ships with. The ids are fixed so a database created by SQL script and one created by the app agree.</summary>
    public sealed record BuiltInRole(Guid Id, string Name, string Description);

    public static readonly IReadOnlyList<BuiltInRole> BuiltInRoles =
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), RoleNames.SuperAdmin, "ผู้ดูแลระบบสูงสุด: ทำได้ทุกอย่าง"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), RoleNames.Director, "ผู้อำนวยการ: ดูได้ทุกแผนก"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), RoleNames.Manager, "ผู้จัดการ: ดูเรื่องและผู้ใช้ของแผนกตัวเอง"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000004"), RoleNames.TeamLeader, "หัวหน้าทีม: แจ้งและติดตามเรื่อง"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000005"), RoleNames.Staff, "พนักงาน: แจ้งและติดตามเรื่องของตัวเอง"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000006"), RoleNames.Viewer, "ผู้ชม: แจ้งและติดตามเรื่องของตัวเอง"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000007"), RoleNames.ITSupport, "ทีม IT: ทำงานในคิวแจ้งปัญหา"),
    ];

    /// <summary>The id a permission gets when it is created by the SQL seed script; derived from its name so it never changes.</summary>
    public static Guid PermissionIdFor(string name)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("helpdesk-permission:" + name));
        return new Guid(hash);
    }

    /// <summary>Adds built-in roles that the database does not have yet, by name (older databases lack some).</summary>
    private static async Task SeedRolesAsync(AppDbContext context)
    {
        var existing = await context.Roles.Select(r => r.Name).ToListAsync();
        var missing = BuiltInRoles.Where(r => !existing.Contains(r.Name)).ToList();
        if (missing.Count == 0) return;

        foreach (var role in missing)
            context.Roles.Add(new Role { Id = role.Id, Name = role.Name, Description = role.Description });
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
            .Select(name => new Permission { Id = PermissionIdFor(name), Name = name, Module = name.Split('.')[0] })
            .ToList();

        var addedNow = missing.Select(p => p.Name).ToHashSet();
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
            if (BuiltInRoles.FirstOrDefault(r => r.Name == roleName) is { } builtIn) role.Description = builtIn.Description;
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

        // A right that did not exist before is granted once to the custom roles that hold the right it grew out of, so they do not
        // lose a screen they had. It must happen in the pass that creates the right: later passes leave custom roles alone, which
        // is what lets an administrator take it away again.
        foreach (var (inherited, source) in InheritedOnce.Where(i => addedNow.Contains(i.Key)))
        {
            var sourceId = permissionIdByName[source];
            var inheritedId = permissionIdByName[inherited];
            foreach (var role in roles.Where(r => !RolePermissionMap.Defaults.ContainsKey(r.Name)
                                                  && r.RolePermissions.Any(rp => rp.PermissionId == sourceId)))
                context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = inheritedId });
        }

        await context.SaveChangesAsync();
    }

    /// <summary>New right -> the right a custom role must already hold to receive it automatically (once, when the right is created).</summary>
    private static readonly IReadOnlyDictionary<string, string> InheritedOnce = new Dictionary<string, string>
    {
        [Permissions.Knowledge.View] = Permissions.Issue.View,
        [Permissions.Knowledge.Manage] = Permissions.Issue.Manage,
    };
}

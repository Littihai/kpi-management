using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

public class PermissionMatrixTests
{
    private static HashSet<string> For(string role) => RolePermissionMap.Defaults[role].ToHashSet();

    [Fact]
    public void Every_built_in_role_is_mapped()
    {
        Assert.Equal(RoleNames.All.OrderBy(n => n), RolePermissionMap.Defaults.Keys.OrderBy(n => n));
    }

    [Fact]
    public void Mapped_permissions_all_exist_and_have_no_duplicates()
    {
        foreach (var (role, permissions) in RolePermissionMap.Defaults)
        {
            Assert.Equal(permissions.Length, permissions.Distinct().Count());
            Assert.All(permissions, p => Assert.Contains(p, Permissions.All));
        }
    }

    [Fact]
    public void Permission_names_are_unique()
    {
        Assert.Equal(Permissions.All.Length, Permissions.All.Distinct().Count());
    }

    [Fact]
    public void SuperAdmin_holds_every_permission()
    {
        Assert.Equal(Permissions.All.OrderBy(n => n), For(RoleNames.SuperAdmin).OrderBy(n => n));
    }

    [Fact]
    public void No_role_is_left_without_permissions()
    {
        // Regression: TeamLeader and Viewer had zero permissions, which would lock them out.
        foreach (var role in RoleNames.All)
            Assert.NotEmpty(RolePermissionMap.Defaults[role]);
    }

    [Fact]
    public void Only_Director_and_SuperAdmin_can_approve_reject_and_close()
    {
        foreach (var permission in new[] { Permissions.Kpi.Approve, Permissions.Kpi.Reject, Permissions.Kpi.Close })
        {
            var holders = RoleNames.All.Where(r => For(r).Contains(permission)).ToHashSet();
            Assert.Equal([RoleNames.SuperAdmin, RoleNames.Director], holders);
        }
    }

    [Fact]
    public void Only_Manager_and_SuperAdmin_can_create_edit_and_submit()
    {
        foreach (var permission in new[] { Permissions.Kpi.Create, Permissions.Kpi.Edit, Permissions.Kpi.Submit })
        {
            var holders = RoleNames.All.Where(r => For(r).Contains(permission)).ToHashSet();
            Assert.Equal([RoleNames.SuperAdmin, RoleNames.Manager], holders);
        }
    }

    [Theory]
    [InlineData(Permissions.Kpi.Delete)]
    [InlineData(Permissions.User.Create)]
    [InlineData(Permissions.User.Edit)]
    [InlineData(Permissions.User.Delete)]
    [InlineData(Permissions.Role.View)]
    [InlineData(Permissions.Department.Manage)]
    [InlineData(Permissions.Job.Trigger)]
    [InlineData(Permissions.Audit.View)]
    [InlineData(Permissions.Overview.View)]
    public void Administrative_permissions_belong_to_SuperAdmin_only(string permission)
    {
        var holders = RoleNames.All.Where(r => For(r).Contains(permission)).ToList();
        Assert.Equal([RoleNames.SuperAdmin], holders);
    }

    [Fact]
    public void Viewer_is_read_only_apart_from_reporting_problems()
    {
        Assert.All(For(RoleNames.Viewer).Where(p => p != Permissions.Issue.Create),
            p => Assert.EndsWith(".view", p));
    }

    [Fact]
    public void Everyone_who_signs_in_can_report_and_follow_their_problems()
    {
        foreach (var role in RoleNames.All)
        {
            Assert.Contains(Permissions.Issue.Create, For(role));
            Assert.Contains(Permissions.Issue.View, For(role));
        }
    }

    [Fact]
    public void Only_IT_Support_and_SuperAdmin_work_the_issue_queue()
    {
        Assert.Equal([RoleNames.SuperAdmin, RoleNames.ITSupport],
            RoleNames.All.Where(r => For(r).Contains(Permissions.Issue.Manage)).ToHashSet());
    }

    [Fact]
    public void Only_SuperAdmin_sees_the_overview_and_audit_log()
    {
        foreach (var permission in new[] { Permissions.Overview.View, Permissions.Audit.View })
            Assert.Equal([RoleNames.SuperAdmin], RoleNames.All.Where(r => For(r).Contains(permission)).ToList());
    }

    [Fact]
    public void Who_sees_every_issue()
    {
        Assert.Equal([RoleNames.SuperAdmin, RoleNames.Director, RoleNames.ITSupport],
            RoleNames.All.Where(RoleNames.SeesAllIssues).ToHashSet());
    }

    [Fact]
    public void Audit_and_export_visibility_matches_the_agreed_table()
    {
        Assert.Equal([RoleNames.SuperAdmin],
            RoleNames.All.Where(r => For(r).Contains(Permissions.Audit.View)).ToHashSet());
        Assert.Equal([RoleNames.SuperAdmin, RoleNames.Director, RoleNames.Manager],
            RoleNames.All.Where(r => For(r).Contains(Permissions.Export.View)).ToHashSet());
    }

    [Fact]
    public void Only_SuperAdmin_and_Director_see_every_department()
    {
        Assert.Equal([RoleNames.SuperAdmin, RoleNames.Director],
            RoleNames.All.Where(RoleNames.SeesAllDepartments).ToHashSet());
    }

    // ── Seeder keeps the database in line with the map ──

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Dictionary<string, HashSet<string>>> StoredAsync(AppDbContext db)
    {
        var rows = await db.RolePermissions
            .Select(rp => new { Role = db.Roles.First(r => r.Id == rp.RoleId).Name, Permission = db.Permissions.First(p => p.Id == rp.PermissionId).Name })
            .ToListAsync();
        return rows.GroupBy(r => r.Role).ToDictionary(g => g.Key, g => g.Select(r => r.Permission).ToHashSet());
    }

    [Fact]
    public async Task Seeding_creates_every_permission_and_matches_the_map()
    {
        await using var db = NewContext();
        await DbSeeder.SeedAsync(db);

        Assert.Equal(Permissions.All.Length, await db.Permissions.CountAsync());
        var stored = await StoredAsync(db);
        foreach (var (role, permissions) in RolePermissionMap.Defaults)
            Assert.Equal(permissions.OrderBy(n => n), stored[role].OrderBy(n => n));
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var db = NewContext();
        await DbSeeder.SeedAsync(db);
        var before = await db.RolePermissions.CountAsync();

        await DbSeeder.SeedAsync(db);

        Assert.Equal(before, await db.RolePermissions.CountAsync());
        Assert.Equal(Permissions.All.Length, await db.Permissions.CountAsync());
    }

    [Fact]
    public async Task Sync_repairs_drift_on_an_existing_database()
    {
        await using var db = NewContext();
        await DbSeeder.SeedAsync(db);

        // Simulate the production state: Manager wrongly holds department.manage, lost kpi.submit,
        // and TeamLeader has nothing at all.
        var manager = await db.Roles.FirstAsync(r => r.Name == RoleNames.Manager);
        var manage = await db.Permissions.FirstAsync(p => p.Name == Permissions.Department.Manage);
        var submit = await db.Permissions.FirstAsync(p => p.Name == Permissions.Kpi.Submit);
        db.RolePermissions.Add(new() { RoleId = manager.Id, PermissionId = manage.Id });
        db.RolePermissions.Remove(await db.RolePermissions.FirstAsync(rp => rp.RoleId == manager.Id && rp.PermissionId == submit.Id));
        var teamLeader = await db.Roles.FirstAsync(r => r.Name == RoleNames.TeamLeader);
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(rp => rp.RoleId == teamLeader.Id));
        await db.SaveChangesAsync();

        await DbSeeder.SyncPermissionsAsync(db);

        var stored = await StoredAsync(db);
        Assert.DoesNotContain(Permissions.Department.Manage, stored[RoleNames.Manager]);
        Assert.Contains(Permissions.Kpi.Submit, stored[RoleNames.Manager]);
        Assert.Equal(RolePermissionMap.Defaults[RoleNames.TeamLeader].Length, stored[RoleNames.TeamLeader].Count);
    }

    [Fact]
    public async Task Sync_leaves_custom_roles_alone()
    {
        await using var db = NewContext();
        await DbSeeder.SeedAsync(db);
        var custom = new KPI.Domain.Entities.Role { Name = "Auditor", Description = "custom" };
        db.Roles.Add(custom);
        var view = await db.Permissions.FirstAsync(p => p.Name == Permissions.Kpi.View);
        db.RolePermissions.Add(new() { RoleId = custom.Id, PermissionId = view.Id });
        await db.SaveChangesAsync();

        await DbSeeder.SyncPermissionsAsync(db);

        Assert.Equal(1, await db.RolePermissions.CountAsync(rp => rp.RoleId == custom.Id));
    }
}

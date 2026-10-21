using KPI.Application.Admin;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

public class RoleAdminTests
{
    private static async Task<(AppDbContext Db, RoleAdminService Service)> CreateAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DbSeeder.SeedAsync(db);
        return (db, new RoleAdminService(db));
    }

    private static async Task<User> AddUserAsync(AppDbContext db, string name, string role)
    {
        var user = new User
        {
            FirstName = name, LastName = "Tester", Email = $"{name.ToLower()}@example.com", PasswordHash = "x",
            RoleId = (await db.Roles.FirstAsync(r => r.Name == role)).Id, IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static SaveRoleRequest Request(string name = "หัวหน้าไอที", params string[] permissions) =>
        new(name, "ดูแลคิวงาน", permissions.Length == 0 ? [Permissions.Issue.Create, Permissions.Issue.View] : permissions);

    // ───────────── catalog and built-ins ─────────────

    [Fact]
    public void Every_permission_has_a_plain_language_description()
    {
        foreach (var name in Permissions.All)
        {
            var entry = PermissionCatalog.Find(name);
            Assert.True(entry is not null && entry.Label.Length > 0 && entry.Description.Length > 0, $"{name} has no description");
        }
        Assert.Equal(Permissions.All.Length, PermissionCatalog.Entries.Count);
    }

    [Fact]
    public async Task The_built_in_roles_are_listed_first_and_marked_as_system_with_their_scope()
    {
        var (_, service) = await CreateAsync();

        var roles = await service.ListAsync();

        Assert.Equal(RoleNames.All, roles.Take(RoleNames.All.Length).Select(r => r.Name));
        Assert.All(roles, r => Assert.True(r.IsSystem));
        var manager = roles.Single(r => r.Name == RoleNames.Manager);
        Assert.Equal(IssueVisibility.Department, manager.IssueVisibility);
        Assert.Equal(IssueVisibility.All, roles.Single(r => r.Name == RoleNames.ITSupport).IssueVisibility);
        Assert.True(roles.Single(r => r.Name == RoleNames.Director).SeesAllDepartments);
        Assert.False(roles.Single(r => r.Name == RoleNames.ITSupport).SeesAllDepartments);
        Assert.Equal(Permissions.All.OrderBy(n => n, StringComparer.Ordinal), roles.Single(r => r.Name == RoleNames.SuperAdmin).Permissions);
    }

    [Fact]
    public void The_scope_table_matches_the_role_name_helpers_it_replaces()
    {
        foreach (var role in RoleNames.All)
        {
            var (issues, departments) = RolePermissionMap.Scopes[role];
            Assert.Equal(RoleNames.SeesAllIssues(role), issues == IssueVisibility.All);
            Assert.Equal(RoleNames.SeesAllDepartments(role), departments);
        }
    }

    [Fact]
    public void Role_management_belongs_to_SuperAdmin_only()
    {
        var holders = RoleNames.All.Where(r => RolePermissionMap.Defaults[r].Contains(Permissions.Role.Manage)).ToList();
        Assert.Equal([RoleNames.SuperAdmin], holders);
    }

    // ───────────── create ─────────────

    [Fact]
    public async Task A_custom_role_is_created_with_its_permissions_and_scope()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);

        var created = await service.CreateAsync(admin.Id, new SaveRoleRequest(" หัวหน้าไอที ", " ดูแลคิวงาน ",
            [Permissions.Issue.View, Permissions.Issue.Create, Permissions.Issue.Manage, Permissions.Issue.View],
            IssueVisibility.All, true));

        Assert.Equal("หัวหน้าไอที", created.Name);
        Assert.Equal("ดูแลคิวงาน", created.Description);
        Assert.False(created.IsSystem);
        Assert.Equal([Permissions.Issue.Create, Permissions.Issue.Manage, Permissions.Issue.View], created.Permissions);
        Assert.Equal(IssueVisibility.All, created.IssueVisibility);
        Assert.True(created.SeesAllDepartments);
        Assert.Equal(0, created.UserCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ก")]
    [InlineData("   ")]
    public async Task A_name_that_is_too_short_is_refused(string name)
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(admin.Id, Request(name)));
    }

    [Fact]
    public async Task A_name_cannot_repeat_a_built_in_or_another_custom_role_whatever_the_case()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        await service.CreateAsync(admin.Id, Request("ผู้ตรวจสอบ"));

        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(admin.Id, Request("ผู้ตรวจสอบ")));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(admin.Id, Request("staff")));
    }

    [Fact]
    public async Task Permissions_must_be_chosen_and_must_exist()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(admin.Id, new SaveRoleRequest("บทบาทว่าง", null, [])));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(admin.Id, new SaveRoleRequest("บทบาทว่าง", null, null)));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(admin.Id, new SaveRoleRequest("บทบาทแปลก", null, ["kpi.view"])));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.CreateAsync(admin.Id, new SaveRoleRequest("บทบาทแปลก", null, [Permissions.Issue.View], (IssueVisibility)9)));
    }

    // ───────────── nobody hands out more than they hold ─────────────

    [Fact]
    public async Task Someone_cannot_give_a_role_rights_they_do_not_hold_themselves()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        // a delegate who may manage roles but works no issues and sees only their own
        var delegateRole = await service.CreateAsync(admin.Id, new SaveRoleRequest("ผู้ช่วยแอดมิน", null,
            [Permissions.Issue.Create, Permissions.Issue.View, Permissions.Role.View, Permissions.Role.Manage]));
        var helper = new User
        {
            FirstName = "helper", LastName = "T", Email = "helper@example.com", PasswordHash = "x", RoleId = delegateRole.Id, IsActive = true,
        };
        db.Users.Add(helper);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(helper.Id,
            new SaveRoleRequest("ทำงานในคิว", null, [Permissions.Issue.View, Permissions.Issue.Manage])));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(helper.Id,
            new SaveRoleRequest("เห็นทุกเรื่อง", null, [Permissions.Issue.View], IssueVisibility.All)));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(helper.Id,
            new SaveRoleRequest("เห็นทุกแผนก", null, [Permissions.Issue.View], IssueVisibility.Own, true)));

        // what they do hold, they may hand on
        var ok = await service.CreateAsync(helper.Id, new SaveRoleRequest("ผู้แจ้ง", null, [Permissions.Issue.Create, Permissions.Issue.View]));
        Assert.Equal("ผู้แจ้ง", ok.Name);
    }

    [Fact]
    public async Task A_role_with_more_rights_than_the_editor_cannot_be_edited_or_deleted_by_them()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        var strong = await service.CreateAsync(admin.Id, Request("ทีมไอที", Permissions.Issue.Create, Permissions.Issue.View, Permissions.Issue.Manage));
        var delegateRole = await service.CreateAsync(admin.Id, new SaveRoleRequest("ผู้ช่วยแอดมิน", null,
            [Permissions.Issue.Create, Permissions.Issue.View, Permissions.Role.View, Permissions.Role.Manage]));
        var helper = new User
        {
            FirstName = "helper", LastName = "T", Email = "helper@example.com", PasswordHash = "x", RoleId = delegateRole.Id, IsActive = true,
        };
        db.Users.Add(helper);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(helper.Id, strong.Id, Request("ทีมไอที", Permissions.Issue.View)));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(helper.Id, strong.Id));
    }

    [Fact]
    public async Task Assigning_a_role_needs_the_assigner_to_cover_it()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        var manager = await AddUserAsync(db, "boss", RoleNames.Manager);
        var roleId = async (string name) => (await db.Roles.FirstAsync(r => r.Name == name)).Id;

        await service.EnsureMayAssignAsync(admin.Id, await roleId(RoleNames.SuperAdmin));
        await service.EnsureMayAssignAsync(manager.Id, await roleId(RoleNames.Staff));          // a manager may make staff
        await Assert.ThrowsAsync<ForbiddenException>(async () => await service.EnsureMayAssignAsync(manager.Id, await roleId(RoleNames.ITSupport)));
        await Assert.ThrowsAsync<ForbiddenException>(async () => await service.EnsureMayAssignAsync(manager.Id, await roleId(RoleNames.SuperAdmin)));
    }

    // ───────────── update and delete ─────────────

    [Fact]
    public async Task Updating_a_custom_role_replaces_its_permissions_and_takes_effect_for_its_users()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        var created = await service.CreateAsync(admin.Id, Request("ทีมไอที", Permissions.Issue.Create, Permissions.Issue.View));
        var member = new User { FirstName = "m", LastName = "T", Email = "m@example.com", PasswordHash = "x", RoleId = created.Id, IsActive = true };
        db.Users.Add(member);
        await db.SaveChangesAsync();

        var updated = await service.UpdateAsync(admin.Id, created.Id, new SaveRoleRequest("ทีมไอที 2", "ใหม่",
            [Permissions.Issue.Create, Permissions.Issue.Manage], IssueVisibility.Department));

        Assert.Equal("ทีมไอที 2", updated.Name);
        Assert.Equal([Permissions.Issue.Create, Permissions.Issue.Manage], updated.Permissions);
        Assert.Equal(IssueVisibility.Department, updated.IssueVisibility);
        Assert.Equal(1, updated.UserCount);
        var held = await db.RolePermissions.Where(rp => rp.RoleId == created.Id).Select(rp => rp.Permission.Name).ToListAsync();
        Assert.Equal([Permissions.Issue.Create, Permissions.Issue.Manage], held.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Built_in_roles_cannot_be_changed_or_deleted()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        var staffId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id;

        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(admin.Id, staffId, Request("Staff")));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(admin.Id, staffId));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(admin.Id, Guid.NewGuid(), Request()));
    }

    [Fact]
    public async Task A_role_that_still_has_people_cannot_be_deleted_but_an_empty_one_can()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        var created = await service.CreateAsync(admin.Id, Request("ชั่วคราว"));
        var member = new User { FirstName = "m", LastName = "T", Email = "m@example.com", PasswordHash = "x", RoleId = created.Id, IsActive = true };
        db.Users.Add(member);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidStateException>(() => service.DeleteAsync(admin.Id, created.Id));

        member.RoleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id;
        await db.SaveChangesAsync();
        await service.DeleteAsync(admin.Id, created.Id);

        Assert.DoesNotContain(await service.ListAsync(), r => r.Id == created.Id);
        Assert.Empty(await db.RolePermissions.Where(rp => rp.RoleId == created.Id).ToListAsync());
    }

    [Fact]
    public async Task The_startup_sync_leaves_custom_roles_alone()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        var created = await service.CreateAsync(admin.Id, Request("ทีมไอที", Permissions.Issue.Create, Permissions.Issue.Manage, Permissions.Issue.View));

        await DbSeeder.SyncPermissionsAsync(db);

        var again = (await service.ListAsync()).Single(r => r.Id == created.Id);
        Assert.Equal(created.Permissions, again.Permissions);
    }
}

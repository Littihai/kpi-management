using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Admin;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

public class OrgAdminServiceTests
{
    private static async Task<(AppDbContext Db, OrgAdminService Service)> CreateAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DbSeeder.SeedAsync(db);
        return (db, new OrgAdminService(db));
    }

    private static async Task<User> AddUserAsync(AppDbContext db, string name, string role = RoleNames.Staff,
        Guid? departmentId = null, bool active = true, string? email = null)
    {
        var roleId = (await db.Roles.FirstAsync(r => r.Name == role)).Id;
        var user = new User
        {
            FirstName = name, LastName = "Tester", Email = email ?? $"{name.ToLower()}@example.com", PasswordHash = "x",
            RoleId = roleId, DepartmentId = departmentId, IsActive = active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<User> ActorAsync(AppDbContext db) =>
        await AddUserAsync(db, "actor", RoleNames.SuperAdmin);

    // ───────────── departments ─────────────

    [Fact]
    public async Task A_department_is_created_with_a_trimmed_name_and_an_upper_case_code()
    {
        var (db, service) = await CreateAsync();

        var created = await service.CreateDepartmentAsync(new SaveDepartmentRequest(" it ", "  ฝ่ายไอที  ", null));

        Assert.Equal("IT", created.Code);
        Assert.Equal("ฝ่ายไอที", created.Name);
        Assert.True(created.IsActive);
        Assert.Equal(0, created.UserCount);
        Assert.Single(await db.Departments.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ก")]
    public async Task A_name_that_is_too_short_is_refused(string name)
    {
        var (_, service) = await CreateAsync();
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateDepartmentAsync(new SaveDepartmentRequest(null, name, null)));
    }

    [Fact]
    public async Task A_name_or_code_that_is_too_long_is_refused()
    {
        var (_, service) = await CreateAsync();
        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateDepartmentAsync(new SaveDepartmentRequest(null, new string('ก', OrgAdminService.MaxNameLength + 1), null)));
        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateDepartmentAsync(new SaveDepartmentRequest(new string('x', OrgAdminService.MaxCodeLength + 1), "Valid name", null)));
    }

    [Fact]
    public async Task Two_departments_cannot_share_a_name_or_a_code_ignoring_case()
    {
        var (_, service) = await CreateAsync();
        await service.CreateDepartmentAsync(new SaveDepartmentRequest("fin", "Finance", null));

        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "FINANCE", null)));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateDepartmentAsync(new SaveDepartmentRequest("FIN", "Another", null)));
        await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "No code one", null));
        await service.CreateDepartmentAsync(new SaveDepartmentRequest("", "No code two", null)); // empty codes never clash
    }

    [Fact]
    public async Task The_head_of_a_department_must_be_an_active_person()
    {
        var (db, service) = await CreateAsync();
        var active = await AddUserAsync(db, "boss");
        var gone = await AddUserAsync(db, "gone", active: false);

        var created = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Maintenance", active.Id));
        Assert.Equal(active.Id, created.ManagerId);
        Assert.Equal("boss Tester", created.ManagerName);

        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Other", gone.Id)));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Other", Guid.NewGuid())));
    }

    [Fact]
    public async Task Updating_changes_the_fields_but_not_the_other_departments()
    {
        var (_, service) = await CreateAsync();
        var a = await service.CreateDepartmentAsync(new SaveDepartmentRequest("A", "Alpha", null));
        await service.CreateDepartmentAsync(new SaveDepartmentRequest("B", "Beta", null));

        var renamed = await service.UpdateDepartmentAsync(a.Id, new SaveDepartmentRequest("a2", "Alpha 2", null));
        Assert.Equal(("A2", "Alpha 2"), (renamed.Code, renamed.Name));

        // keeping its own name is fine, taking someone else's is not
        await service.UpdateDepartmentAsync(a.Id, new SaveDepartmentRequest("A2", "Alpha 2", null));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateDepartmentAsync(a.Id, new SaveDepartmentRequest("A2", "beta", null)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateDepartmentAsync(Guid.NewGuid(), new SaveDepartmentRequest(null, "Nope", null)));
    }

    [Fact]
    public async Task A_deactivated_department_leaves_the_pickers_but_keeps_its_people_and_can_return()
    {
        var (db, service) = await CreateAsync();
        var created = await service.CreateDepartmentAsync(new SaveDepartmentRequest("QA", "Quality", null));
        var member = await AddUserAsync(db, "member", departmentId: created.Id);

        var off = await service.UpdateDepartmentAsync(created.Id, new SaveDepartmentRequest("QA", "Quality", null, IsActive: false));

        Assert.False(off.IsActive);
        Assert.Empty(await db.ListActiveAsync());                      // pickers
        Assert.Single(await service.ListDepartmentsAsync());           // the admin list still shows it
        Assert.Equal(created.Id, (await db.Users.FindAsync(member.Id))!.DepartmentId);

        await service.UpdateDepartmentAsync(created.Id, new SaveDepartmentRequest("QA", "Quality", null, IsActive: true));
        Assert.Single(await db.ListActiveAsync());
    }

    [Fact]
    public async Task The_admin_list_shows_active_departments_first_with_their_active_head_count()
    {
        var (db, service) = await CreateAsync();
        var old = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Archived", null, IsActive: false));
        var live = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Zeta", null));
        await AddUserAsync(db, "one", departmentId: live.Id);
        await AddUserAsync(db, "two", departmentId: live.Id);
        await AddUserAsync(db, "off", departmentId: live.Id, active: false);

        var list = await service.ListDepartmentsAsync();

        Assert.Equal(["Zeta", "Archived"], list.Select(d => d.Name));
        Assert.Equal(2, list[0].UserCount);
        Assert.Equal(old.Id, list[1].Id);
    }

    // ───────────── users ─────────────

    [Fact]
    public async Task Searching_users_returns_one_page_with_the_total()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        for (var i = 0; i < 25; i++) await AddUserAsync(db, $"user{i:D2}");

        var first = await service.SearchUsersAsync(admin.Id, new UserQuery(PageSize: 10));
        var last = await service.SearchUsersAsync(admin.Id, new UserQuery(Page: 3, PageSize: 10));
        var absurd = await service.SearchUsersAsync(admin.Id, new UserQuery(Page: -4, PageSize: 99999));

        Assert.Equal(26, first.Total);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(6, last.Items.Count);
        Assert.Equal(1, absurd.Page);
        Assert.Equal(200, absurd.PageSize);
    }

    [Fact]
    public async Task Users_can_be_searched_by_name_or_email_and_filtered()
    {
        var (db, service) = await CreateAsync();
        var admin = await AddUserAsync(db, "admin", RoleNames.SuperAdmin);
        var dept = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Dept", null));
        await AddUserAsync(db, "Somchai", departmentId: dept.Id, email: "somchai@factory.example");
        await AddUserAsync(db, "Malee", RoleNames.ITSupport, email: "malee@factory.example");
        await AddUserAsync(db, "Retired", active: false);

        async Task<List<string>> Names(UserQuery q) =>
            (await service.SearchUsersAsync(admin.Id, q)).Items.Select(u => u.FirstName).ToList();

        Assert.Equal(["Somchai"], await Names(new UserQuery(Search: "SOMCHAI")));
        Assert.Equal(["Malee"], await Names(new UserQuery(Search: "malee@factory")));
        Assert.Equal(["Malee"], await Names(new UserQuery(Role: RoleNames.ITSupport)));
        Assert.Equal(["Somchai"], await Names(new UserQuery(DepartmentId: dept.Id)));
        Assert.Equal(["Retired"], await Names(new UserQuery(Active: false)));
    }

    [Fact]
    public async Task A_manager_only_finds_people_of_their_own_department()
    {
        var (db, service) = await CreateAsync();
        var a = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Dept A", null));
        var b = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Dept B", null));
        var manager = await AddUserAsync(db, "manager", RoleNames.Manager, a.Id);
        await AddUserAsync(db, "mate", departmentId: a.Id);
        await AddUserAsync(db, "stranger", departmentId: b.Id);
        var loner = await AddUserAsync(db, "loner", RoleNames.Manager);

        var seen = (await service.SearchUsersAsync(manager.Id, new UserQuery())).Items.Select(u => u.FirstName).ToList();
        Assert.Equal(["manager", "mate"], seen);
        Assert.DoesNotContain("stranger", seen);
        // asking for another department does not widen the scope
        Assert.DoesNotContain("stranger", (await service.SearchUsersAsync(manager.Id, new UserQuery(DepartmentId: b.Id))).Items.Select(u => u.FirstName));
        Assert.Empty((await service.SearchUsersAsync(loner.Id, new UserQuery())).Items); // a manager with no department sees no one
    }

    [Fact]
    public async Task Editing_a_user_changes_name_email_and_department()
    {
        var (db, service) = await CreateAsync();
        var actor = await ActorAsync(db);
        var dept = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Dept", null));
        var user = await AddUserAsync(db, "old", email: "old@example.com");

        var row = await service.UpdateUserAsync(actor.Id, user.Id, new UpdateUserRequest("  New ", " Name ", " New@Example.com ", dept.Id));

        Assert.Equal(("New", "Name", "new@example.com", "Dept"), (row.FirstName, row.LastName, row.Email, row.Department));
        var cleared = await service.UpdateUserAsync(actor.Id, user.Id, new UpdateUserRequest("New", "Name", null, null));
        Assert.Null(cleared.DepartmentId);
        Assert.Equal("new@example.com", cleared.Email); // a missing e-mail leaves it alone
    }

    [Fact]
    public async Task Editing_a_user_refuses_bad_input()
    {
        var (db, service) = await CreateAsync();
        var actor = await ActorAsync(db);
        var inactiveDept = await service.CreateDepartmentAsync(new SaveDepartmentRequest(null, "Closed", null, IsActive: false));
        await AddUserAsync(db, "taken", email: "taken@example.com");
        var user = await AddUserAsync(db, "me", email: "me@example.com");

        Task<UserRowDto> Edit(string? first = "Me", string? email = null, Guid? dept = null) =>
            service.UpdateUserAsync(actor.Id, user.Id, new UpdateUserRequest(first, "T", email, dept));

        await Assert.ThrowsAsync<RequestValidationException>(() => Edit(first: " "));
        await Assert.ThrowsAsync<RequestValidationException>(() => Edit(email: "not-an-email"));
        await Assert.ThrowsAsync<RequestValidationException>(() => Edit(email: "TAKEN@example.com"));
        await Assert.ThrowsAsync<RequestValidationException>(() => Edit(dept: inactiveDept.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => Edit(dept: Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateUserAsync(actor.Id, Guid.NewGuid(), new UpdateUserRequest("A", "B", null, null)));
        await Edit(email: "ME@example.com"); // the same address in another case is not a clash with oneself
    }

    [Fact]
    public async Task A_deactivated_user_can_be_reactivated()
    {
        var (db, service) = await CreateAsync();
        var actor = await ActorAsync(db);
        var user = await AddUserAsync(db, "back", active: false);

        Assert.True((await service.ReactivateUserAsync(actor.Id, user.Id)).IsActive);
        Assert.True((await service.ReactivateUserAsync(actor.Id, user.Id)).IsActive); // idempotent
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReactivateUserAsync(actor.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Resetting_a_password_stores_a_new_hash_and_enforces_the_policy()
    {
        var (db, service) = await CreateAsync();
        var actor = await ActorAsync(db);
        var user = await AddUserAsync(db, "forgot");
        var before = user.PasswordHash;

        await Assert.ThrowsAsync<RequestValidationException>(() => service.ResetPasswordAsync(actor.Id, user.Id, new ResetPasswordRequest("short")));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.ResetPasswordAsync(actor.Id, user.Id, new ResetPasswordRequest(null)));
        Assert.Equal(before, (await db.Users.FindAsync(user.Id))!.PasswordHash);

        await service.ResetPasswordAsync(actor.Id, user.Id, new ResetPasswordRequest("A-brand-new-one"));

        var stored = (await db.Users.FindAsync(user.Id))!.PasswordHash;
        Assert.NotEqual("A-brand-new-one", stored);
        Assert.True(BCrypt.Net.BCrypt.Verify("A-brand-new-one", stored));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ResetPasswordAsync(actor.Id, Guid.NewGuid(), new ResetPasswordRequest("A-brand-new-one")));
    }
}

public class OrgAdminPrivilegeTests
{
    private static async Task<(AppDbContext Db, OrgAdminService Service, User SuperAdmin, User Agent, User Target)> CreateAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DbSeeder.SeedAsync(db);
        async Task<User> Add(string name, string role)
        {
            var roleId = (await db.Roles.FirstAsync(r => r.Name == role)).Id;
            var user = new User { FirstName = name, LastName = "T", Email = $"{name}@example.com", PasswordHash = "x", RoleId = roleId };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user;
        }
        return (db, new OrgAdminService(db), await Add("root", RoleNames.SuperAdmin), await Add("agent", RoleNames.ITSupport), await Add("target", RoleNames.SuperAdmin));
    }

    [Fact]
    public async Task Someone_below_SuperAdmin_cannot_edit_reactivate_or_reset_a_SuperAdmin()
    {
        var (db, service, _, agent, target) = await CreateAsync();
        var before = (await db.Users.FindAsync(target.Id))!.PasswordHash;

        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateUserAsync(agent.Id, target.Id, new UpdateUserRequest("Hacked", "T", "hacked@example.com", null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ReactivateUserAsync(agent.Id, target.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ResetPasswordAsync(agent.Id, target.Id, new ResetPasswordRequest("Takeover-12345")));

        var after = (await db.Users.FindAsync(target.Id))!;
        Assert.Equal(("target", before), (after.FirstName, after.PasswordHash));
    }

    [Fact]
    public async Task A_SuperAdmin_may_manage_another_SuperAdmin_and_anyone_else_may_be_managed_by_a_lower_actor()
    {
        var (db, service, root, agent, target) = await CreateAsync();
        await service.ResetPasswordAsync(root.Id, target.Id, new ResetPasswordRequest("Changed-by-root-1"));
        Assert.Equal("Renamed", (await service.UpdateUserAsync(root.Id, target.Id, new UpdateUserRequest("Renamed", "T", null, null))).FirstName);

        // an ordinary account is not protected this way (the endpoint permission already limits who can call it)
        var staffRoleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id;
        var staff = new User { FirstName = "staff", LastName = "T", Email = "staff@example.com", PasswordHash = "x", RoleId = staffRoleId };
        db.Users.Add(staff);
        await db.SaveChangesAsync();
        await service.ResetPasswordAsync(agent.Id, staff.Id, new ResetPasswordRequest("Changed-by-agent-1"));
    }
}

/// <summary>The same rules over HTTP: permissions, the public department list, paging and sign-in after a reset.</summary>
public class OrgAdminApiTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public OrgAdminApiTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task An_admin_creates_a_department_and_the_public_form_lists_it_until_it_is_deactivated()
    {
        var admin = await _factory.ClientAsync("admin");
        var created = await admin.PostAsJsonAsync("/api/departments", new { code = "pln", name = "Planning HTTP" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var anonymous = _factory.CreateClient();
        var listed = await Json(await anonymous.GetAsync("/api/public/departments"));
        Assert.Contains(listed.EnumerateArray(), d => d.GetProperty("id").GetGuid() == id);

        var off = await admin.PutAsJsonAsync($"/api/departments/{id}", new { code = "PLN", name = "Planning HTTP", isActive = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        listed = await Json(await anonymous.GetAsync("/api/public/departments"));
        Assert.DoesNotContain(listed.EnumerateArray(), d => d.GetProperty("id").GetGuid() == id);

        var all = await Json(await admin.GetAsync("/api/departments/all"));
        Assert.Contains(all.EnumerateArray(), d => d.GetProperty("id").GetGuid() == id && !d.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Only_a_SuperAdmin_can_manage_departments_or_edit_users()
    {
        var someId = Guid.NewGuid();
        foreach (var user in new[] { "manager-a", "staff-a", "it-a", "director" })
        {
            var client = await _factory.ClientAsync(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/departments/all")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/departments", new { name = "Nope" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/departments/{someId}", new { name = "Nope" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/users/{someId}", new { firstName = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync($"/api/users/{someId}/reactivate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/users/{someId}/reset-password", new { newPassword = "Whatever123" })).StatusCode);
        }
    }

    [Fact]
    public async Task Validation_errors_are_400_with_a_message_and_unknown_ids_are_404()
    {
        var admin = await _factory.ClientAsync("admin");

        var invalid = await admin.PostAsJsonAsync("/api/departments", new { name = "ก" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False(string.IsNullOrEmpty((await Json(invalid)).GetProperty("message").GetString()));

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync($"/api/departments/{Guid.NewGuid()}", new { name = "Whatever" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsync($"/api/users/{Guid.NewGuid()}/reactivate", null)).StatusCode);
    }

    [Fact]
    public async Task The_users_endpoint_returns_a_page_and_honours_search_and_paging()
    {
        var admin = await _factory.ClientAsync("admin");

        var page = await Json(await admin.GetAsync("/api/users?pageSize=3&page=1"));
        Assert.Equal(3, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("total").GetInt32() > 3);
        Assert.Equal(3, page.GetProperty("pageSize").GetInt32());

        var found = await Json(await admin.GetAsync("/api/users?search=VIEWER-A"));
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        Assert.Equal("viewer-a@example.com", found.GetProperty("items")[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task After_a_reset_the_person_signs_in_with_the_new_password_only()
    {
        var admin = await _factory.ClientAsync("admin");
        var id = _factory.UserId("viewer-a");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = "Reset-123-OK" })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync("viewer-a@example.com", ApiFactory.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("viewer-a@example.com", "Reset-123-OK")).StatusCode);

        // restore, other tests sign this account in with the shared password
        await admin.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = ApiFactory.Password });
    }

    [Fact]
    public async Task A_deactivated_user_cannot_sign_in_until_reactivated()
    {
        var admin = await _factory.ClientAsync("admin");
        var id = _factory.UserId("staff-b");

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/users/{id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync("staff-b@example.com", ApiFactory.Password)).StatusCode);

        var back = await admin.PutAsync($"/api/users/{id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.True((await Json(back)).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("staff-b@example.com", ApiFactory.Password)).StatusCode);
    }
}

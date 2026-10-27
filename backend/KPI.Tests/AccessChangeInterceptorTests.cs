using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

/// <summary>Which saves tell the real-time hub that someone's access changed, and which do not.</summary>
public class AccessChangeInterceptorTests
{
    private sealed class RecordingListener : IAccessChangeListener
    {
        public List<(Guid[] Users, Guid[] Roles)> Calls { get; } = [];
        public bool Throw { get; set; }

        public Task AccessChangedAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default)
        {
            Calls.Add((userIds.ToArray(), roleIds.ToArray()));
            if (Throw) throw new InvalidOperationException("hub is down");
            return Task.CompletedTask;
        }
    }

    private sealed class World
    {
        public required AppDbContext Db { get; init; }
        public required DbContextOptions<AppDbContext> Options { get; init; }
        public required RecordingListener Listener { get; init; }
        public required User Person { get; init; }
        public required Department DeptA { get; init; }
        public required Department DeptB { get; init; }
        public required Role Staff { get; init; }
        public required Role Manager { get; init; }
    }

    private static async Task<World> CreateAsync()
    {
        var listener = new RecordingListener();
        var interceptor = new AccessChangeInterceptor(listener, NullLogger<AccessChangeInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        var db = new AppDbContext(options);
        await DbSeeder.SeedAsync(db);

        var deptA = new Department { Name = "Dept A", Code = "A" };
        var deptB = new Department { Name = "Dept B", Code = "B" };
        db.Departments.AddRange(deptA, deptB);
        var staff = await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff);
        var manager = await db.Roles.FirstAsync(r => r.Name == RoleNames.Manager);
        var person = new User
        {
            FirstName = "Somchai", LastName = "Tester", Email = "somchai@example.com", PasswordHash = "x",
            RoleId = staff.Id, DepartmentId = deptA.Id, IsActive = true,
        };
        db.Users.Add(person);
        await db.SaveChangesAsync();

        listener.Calls.Clear(); // setting the world up is not the thing under test
        return new World { Db = db, Options = options, Listener = listener, Person = person, DeptA = deptA, DeptB = deptB, Staff = staff, Manager = manager };
    }

    [Fact]
    public async Task Deactivating_a_person_is_reported()
    {
        var w = await CreateAsync();
        w.Person.IsActive = false;
        await w.Db.SaveChangesAsync();

        var call = Assert.Single(w.Listener.Calls);
        Assert.Equal([w.Person.Id], call.Users);
        Assert.Empty(call.Roles);
    }

    [Fact]
    public async Task Changing_the_role_of_a_person_is_reported()
    {
        var w = await CreateAsync();
        w.Person.RoleId = w.Manager.Id;
        await w.Db.SaveChangesAsync();

        Assert.Equal([w.Person.Id], Assert.Single(w.Listener.Calls).Users);
    }

    [Fact]
    public async Task Moving_a_person_to_another_department_is_reported()
    {
        var w = await CreateAsync();
        w.Person.DepartmentId = w.DeptB.Id;
        await w.Db.SaveChangesAsync();

        Assert.Equal([w.Person.Id], Assert.Single(w.Listener.Calls).Users);
    }

    [Fact]
    public async Task Removing_a_person_from_every_department_is_reported()
    {
        var w = await CreateAsync();
        w.Person.DepartmentId = null;
        await w.Db.SaveChangesAsync();

        Assert.Equal([w.Person.Id], Assert.Single(w.Listener.Calls).Users);
    }

    [Fact]
    public async Task Ordinary_edits_to_a_person_are_not_reported()
    {
        var w = await CreateAsync();
        w.Person.FirstName = "Somsak";
        w.Person.Email = "somsak@example.com";
        w.Person.PasswordHash = "y";
        w.Person.FailedLoginCount = 3;
        w.Person.UpdatedAt = DateTime.UtcNow;
        await w.Db.SaveChangesAsync();

        Assert.Empty(w.Listener.Calls);
    }

    [Fact]
    public async Task Saving_the_same_values_again_is_not_reported()
    {
        var w = await CreateAsync();
        w.Person.DepartmentId = w.DeptA.Id; // already there
        w.Person.RoleId = w.Staff.Id;       // already that
        w.Person.IsActive = true;           // already so
        await w.Db.SaveChangesAsync();

        Assert.Empty(w.Listener.Calls);
    }

    [Fact]
    public async Task A_new_person_is_not_reported_because_nobody_can_be_connected_yet()
    {
        var w = await CreateAsync();
        w.Db.Users.Add(new User
        {
            FirstName = "New", LastName = "Person", Email = "new@example.com", PasswordHash = "x", RoleId = w.Staff.Id, IsActive = true,
        });
        await w.Db.SaveChangesAsync();

        Assert.Empty(w.Listener.Calls);
    }

    [Fact]
    public async Task Several_changes_in_one_save_arrive_as_one_report()
    {
        var w = await CreateAsync();
        var other = new User
        {
            FirstName = "Other", LastName = "Person", Email = "other@example.com", PasswordHash = "x", RoleId = w.Staff.Id, IsActive = true,
        };
        w.Db.Users.Add(other);
        await w.Db.SaveChangesAsync();
        w.Listener.Calls.Clear();

        w.Person.IsActive = false;
        w.Person.DepartmentId = w.DeptB.Id;
        other.RoleId = w.Manager.Id;
        w.Manager.IssueVisibility = IssueVisibility.All;
        await w.Db.SaveChangesAsync();

        var call = Assert.Single(w.Listener.Calls);
        Assert.Equal(new[] { w.Person.Id, other.Id }.Order(), call.Users.Order()); // exactly these two, nobody else
        Assert.Equal([w.Manager.Id], call.Roles);
    }

    // ───────────── roles ─────────────

    [Fact]
    public async Task Changing_what_a_role_sees_is_reported_for_the_role()
    {
        var w = await CreateAsync();
        w.Staff.SeesAllDepartments = !w.Staff.SeesAllDepartments;
        await w.Db.SaveChangesAsync();

        var call = Assert.Single(w.Listener.Calls);
        Assert.Equal([w.Staff.Id], call.Roles);
        Assert.Empty(call.Users);
    }

    [Fact]
    public async Task Changing_the_issue_visibility_of_a_role_is_reported()
    {
        var w = await CreateAsync();
        w.Staff.IssueVisibility = w.Staff.IssueVisibility == IssueVisibility.All ? IssueVisibility.Own : IssueVisibility.All;
        await w.Db.SaveChangesAsync();

        Assert.Equal([w.Staff.Id], Assert.Single(w.Listener.Calls).Roles);
    }

    [Fact]
    public async Task Editing_only_the_name_of_a_role_is_not_reported()
    {
        var w = await CreateAsync();
        w.Staff.Description = "Renamed for clarity";
        w.Staff.UpdatedAt = DateTime.UtcNow;
        await w.Db.SaveChangesAsync();

        Assert.Empty(w.Listener.Calls);
    }

    [Fact]
    public async Task Adding_and_removing_a_right_of_a_role_is_reported()
    {
        var w = await CreateAsync();
        var manage = await w.Db.Permissions.FirstAsync(p => p.Name == Permissions.Issue.Manage);
        var existing = await w.Db.RolePermissions.Where(rp => rp.RoleId == w.Staff.Id && rp.PermissionId == manage.Id).ToListAsync();
        w.Db.RolePermissions.RemoveRange(existing);
        await w.Db.SaveChangesAsync();
        w.Listener.Calls.Clear();

        w.Db.RolePermissions.Add(new RolePermission { RoleId = w.Staff.Id, PermissionId = manage.Id });
        await w.Db.SaveChangesAsync();
        Assert.Equal([w.Staff.Id], Assert.Single(w.Listener.Calls).Roles);

        w.Listener.Calls.Clear();
        w.Db.RolePermissions.RemoveRange(await w.Db.RolePermissions.Where(rp => rp.RoleId == w.Staff.Id && rp.PermissionId == manage.Id).ToListAsync());
        await w.Db.SaveChangesAsync();
        Assert.Equal([w.Staff.Id], Assert.Single(w.Listener.Calls).Roles);
    }

    // ───────────── safety ─────────────

    [Fact]
    public async Task A_listener_that_fails_does_not_undo_or_fail_the_save()
    {
        var w = await CreateAsync();
        w.Listener.Throw = true;

        w.Person.IsActive = false;
        await w.Db.SaveChangesAsync(); // must not throw

        Assert.Single(w.Listener.Calls);
        w.Db.ChangeTracker.Clear();
        Assert.False((await w.Db.Users.FirstAsync(u => u.Id == w.Person.Id)).IsActive);
    }

    [Fact]
    public async Task A_failed_save_reports_nothing_and_leaves_nothing_behind_for_the_next_one()
    {
        var w = await CreateAsync();

        // Another context stores a role first, so this context's attempt to add the same key fails when it saves.
        var clash = Guid.NewGuid();
        await using (var other = new AppDbContext(w.Options))
        {
            other.Roles.Add(new Role { Id = clash, Name = "Taken" });
            await other.SaveChangesAsync();
        }
        w.Listener.Calls.Clear();

        w.Person.IsActive = false;
        w.Db.Roles.Add(new Role { Id = clash, Name = "Same key again" });

        await Assert.ThrowsAnyAsync<Exception>(() => w.Db.SaveChangesAsync());
        Assert.Empty(w.Listener.Calls);

        w.Db.ChangeTracker.Clear();
        var person = await w.Db.Users.FirstAsync(u => u.Id == w.Person.Id);
        person.FirstName = "Edited afterwards";
        await w.Db.SaveChangesAsync();

        Assert.Empty(w.Listener.Calls); // the earlier, failed deactivation was not remembered
    }

    [Fact]
    public async Task The_synchronous_save_reports_too()
    {
        var w = await CreateAsync();
        w.Person.IsActive = false;
        w.Db.SaveChanges();

        Assert.Equal([w.Person.Id], Assert.Single(w.Listener.Calls).Users);
    }
}

using System.Security.Claims;
using KPI.API.Realtime;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

/// <summary>The group rules, and how live connections are moved when a person's access changes (no real SignalR involved).</summary>
public class HubAccessSyncTests
{
    // ───────────── test doubles ─────────────

    private sealed class FakeGroups : IGroupManager
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, HashSet<string>> _members = new();

        /// <summary>Return a task here to hold a call (or throw from it) for a given connection and group.</summary>
        public Func<string, string, bool, Task>? Before { get; set; }

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Do(connectionId, groupName, add: true);

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Do(connectionId, groupName, add: false);

        private async Task Do(string connectionId, string groupName, bool add)
        {
            if (Before is not null) await Before(connectionId, groupName, add);
            lock (_gate)
            {
                if (!_members.TryGetValue(connectionId, out var groups)) _members[connectionId] = groups = [];
                if (add) groups.Add(groupName); else groups.Remove(groupName);
            }
        }

        public HashSet<string> Of(string connectionId)
        {
            lock (_gate) return _members.TryGetValue(connectionId, out var groups) ? [.. groups] : [];
        }
    }

    private sealed class FakeHub : IHubContext<KpiHub>
    {
        public FakeGroups FakeGroups { get; } = new();
        public IHubClients Clients => throw new NotSupportedException();
        public IGroupManager Groups => FakeGroups;
    }

    private sealed class FakeCaller(string connectionId) : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class Rig
    {
        public required ServiceProvider Services { get; init; }
        public required HubConnectionRegistry Registry { get; init; }
        public required FakeHub Hub { get; init; }
        public required HubAccessSync Sync { get; init; }
        public required Department DeptA { get; init; }
        public required Department DeptB { get; init; }
        public required Department DeptC { get; init; }

        public async Task<Guid> AddUserAsync(Role role, Department? department, bool active = true)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var name = $"u{Guid.NewGuid():N}"[..10];
            var user = new User
            {
                FirstName = name, LastName = "Tester", Email = $"{name}@example.com", PasswordHash = "x",
                RoleId = role.Id, DepartmentId = department?.Id, IsActive = active,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        }

        public async Task<Role> AddRoleAsync(bool seesAll = false, IssueVisibility visibility = IssueVisibility.Own, bool manage = false)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = new Role { Name = $"Role {Guid.NewGuid():N}"[..14], SeesAllDepartments = seesAll, IssueVisibility = visibility };
            db.Roles.Add(role);
            if (manage)
            {
                var permission = await db.Permissions.FirstAsync(p => p.Name == Permissions.Issue.Manage);
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }
            await db.SaveChangesAsync();
            return role;
        }

        public async Task ChangeAsync(Action<AppDbContext> change)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            change(db);
            await db.SaveChangesAsync();
        }

        /// <summary>Opens a connection the way the hub does: registered first, then put into its groups.</summary>
        public async Task<(string Id, FakeCaller Caller)> OpenAsync(Guid userId)
        {
            var id = $"conn-{Guid.NewGuid():N}";
            var caller = new FakeCaller(id);
            Registry.Add(id, userId, caller);
            await Sync.AttachAsync(id);
            return (id, caller);
        }

        public HashSet<string> GroupsOf(string connectionId) => Hub.FakeGroups.Of(connectionId);
    }

    /// <summary>Lets the interceptor be built before the sync it reports to (the sync needs the services that need the interceptor).</summary>
    private sealed class ForwardingListener : KPI.Application.Notifications.IAccessChangeListener
    {
        public KPI.Application.Notifications.IAccessChangeListener? Target { get; set; }

        public Task AccessChangedAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default) =>
            Target?.AccessChangedAsync(userIds, roleIds, cancellationToken) ?? Task.CompletedTask;
    }

    private static async Task<Rig> CreateAsync()
    {
        var store = Guid.NewGuid().ToString();
        var forwarding = new ForwardingListener();
        var interceptor = new AccessChangeInterceptor(forwarding, NullLogger<AccessChangeInterceptor>.Instance);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(store).AddInterceptors(interceptor));
        var provider = services.BuildServiceProvider();

        Department a, b, c;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DbSeeder.SeedAsync(db);
            a = new Department { Name = "A", Code = "A" };
            b = new Department { Name = "B", Code = "B" };
            c = new Department { Name = "C", Code = "C" };
            db.Departments.AddRange(a, b, c);
            await db.SaveChangesAsync();
        }

        var registry = new HubConnectionRegistry();
        var hub = new FakeHub();
        var sync = new HubAccessSync(registry, hub, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<HubAccessSync>.Instance);
        forwarding.Target = sync;
        return new Rig { Services = provider, Registry = registry, Hub = hub, Sync = sync, DeptA = a, DeptB = b, DeptC = c };
    }

    /// <summary>Exactly these groups, no more: Assert.Equivalent only checks that the expected ones are present.</summary>
    private static void AssertGroups(IEnumerable<string> expected, HashSet<string> actual) =>
        Assert.Equal(expected.OrderBy(g => g, StringComparer.Ordinal), actual.OrderBy(g => g, StringComparer.Ordinal));

    // ───────────── which groups a person belongs in ─────────────

    [Fact]
    public async Task An_ordinary_person_hears_their_own_events_and_their_departments_but_not_the_queue()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(), r.DeptA);
        var (id, _) = await r.OpenAsync(person);

        AssertGroups(new[] { KpiHub.UserGroup(person), KpiHub.DepartmentGroup(r.DeptA.Id) }, r.GroupsOf(id));
    }

    [Fact]
    public async Task A_role_that_sees_every_department_joins_all_departments()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(seesAll: true), null);
        var (id, _) = await r.OpenAsync(person);

        AssertGroups(new[] { KpiHub.UserGroup(person), KpiHub.AllDepartmentsGroup }, r.GroupsOf(id));
    }

    [Fact]
    public async Task A_role_with_department_visibility_joins_the_managers_of_its_department()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(visibility: IssueVisibility.Department), r.DeptB);
        var (id, _) = await r.OpenAsync(person);

        Assert.Contains(KpiHub.DepartmentManagersGroup(r.DeptB.Id), r.GroupsOf(id));
        Assert.Contains(KpiHub.DepartmentGroup(r.DeptB.Id), r.GroupsOf(id));
    }

    [Fact]
    public async Task A_role_that_may_work_issues_joins_the_queue_wherever_the_person_sits()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(manage: true), null);
        var (id, _) = await r.OpenAsync(person);

        AssertGroups(new[] { KpiHub.UserGroup(person), KpiHub.ItSupportGroup }, r.GroupsOf(id));
    }

    [Fact]
    public async Task A_deactivated_person_gets_a_connection_in_no_group_at_all()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(seesAll: true, manage: true), r.DeptA, active: false);
        var (id, caller) = await r.OpenAsync(person);

        Assert.Empty(r.GroupsOf(id));
        Assert.False(caller.Aborted); // refused quietly, as before
    }

    // ───────────── changes while connected ─────────────

    [Fact]
    public async Task Changing_what_a_role_sees_moves_connected_people_of_that_role_and_nobody_else()
    {
        var r = await CreateAsync();
        var role = await r.AddRoleAsync(seesAll: false);
        var member = await r.AddUserAsync(role, r.DeptA);
        var bystander = await r.AddUserAsync(await r.AddRoleAsync(seesAll: false), r.DeptA);
        var (memberConn, _) = await r.OpenAsync(member);
        var (bystanderConn, _) = await r.OpenAsync(bystander);
        var bystanderBefore = r.GroupsOf(bystanderConn);
        Assert.DoesNotContain(KpiHub.AllDepartmentsGroup, r.GroupsOf(memberConn));

        await r.ChangeAsync(db => db.Roles.First(x => x.Id == role.Id).SeesAllDepartments = true);
        Assert.Contains(KpiHub.AllDepartmentsGroup, r.GroupsOf(memberConn));
        AssertGroups(bystanderBefore, r.GroupsOf(bystanderConn));

        await r.ChangeAsync(db => db.Roles.First(x => x.Id == role.Id).SeesAllDepartments = false);
        Assert.DoesNotContain(KpiHub.AllDepartmentsGroup, r.GroupsOf(memberConn));
    }

    [Fact]
    public async Task Changing_the_issue_visibility_of_a_role_joins_and_leaves_the_department_managers()
    {
        var r = await CreateAsync();
        var role = await r.AddRoleAsync(visibility: IssueVisibility.Own);
        var person = await r.AddUserAsync(role, r.DeptA);
        var (id, _) = await r.OpenAsync(person);
        Assert.DoesNotContain(KpiHub.DepartmentManagersGroup(r.DeptA.Id), r.GroupsOf(id));

        await r.ChangeAsync(db => db.Roles.First(x => x.Id == role.Id).IssueVisibility = IssueVisibility.Department);
        Assert.Contains(KpiHub.DepartmentManagersGroup(r.DeptA.Id), r.GroupsOf(id));

        await r.ChangeAsync(db => db.Roles.First(x => x.Id == role.Id).IssueVisibility = IssueVisibility.Own);
        Assert.DoesNotContain(KpiHub.DepartmentManagersGroup(r.DeptA.Id), r.GroupsOf(id));
    }

    [Fact]
    public async Task A_person_deactivated_while_connected_loses_every_group_and_is_disconnected_on_all_connections()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(seesAll: true, manage: true), r.DeptA);
        var (first, firstCaller) = await r.OpenAsync(person);
        var (second, secondCaller) = await r.OpenAsync(person);
        Assert.NotEmpty(r.GroupsOf(first));

        await r.ChangeAsync(db => db.Users.First(u => u.Id == person).IsActive = false);

        Assert.Empty(r.GroupsOf(first));
        Assert.Empty(r.GroupsOf(second));
        Assert.True(firstCaller.Aborted && secondCaller.Aborted);
    }

    [Fact]
    public async Task A_connection_opened_while_deactivated_is_picked_up_when_the_person_is_reactivated()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(manage: true), r.DeptA, active: false);
        var (id, caller) = await r.OpenAsync(person);
        Assert.Empty(r.GroupsOf(id));

        await r.ChangeAsync(db => db.Users.First(u => u.Id == person).IsActive = true);

        Assert.Contains(KpiHub.ItSupportGroup, r.GroupsOf(id));
        Assert.Contains(KpiHub.UserGroup(person), r.GroupsOf(id));
        Assert.False(caller.Aborted);
    }

    [Fact]
    public async Task A_connection_that_dropped_before_its_groups_were_decided_is_ignored()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(), r.DeptA);
        var caller = new FakeCaller("gone");
        r.Registry.Add("gone", person, caller);
        r.Registry.Remove("gone");

        Assert.Null(await r.Sync.AttachAsync("gone"));
        Assert.Empty(r.GroupsOf("gone"));
    }

    // ───────────── failures and ordering ─────────────

    [Fact]
    public async Task One_connection_failing_does_not_stop_the_others_and_a_deactivated_person_is_still_disconnected()
    {
        var r = await CreateAsync();
        var role = await r.AddRoleAsync(manage: true);
        var unlucky = await r.AddUserAsync(role, r.DeptA);
        var fine = await r.AddUserAsync(role, r.DeptA);
        var (unluckyConn, unluckyCaller) = await r.OpenAsync(unlucky);
        var (fineConn, fineCaller) = await r.OpenAsync(fine);

        r.Hub.FakeGroups.Before = (connection, _, _) =>
            connection == unluckyConn ? throw new InvalidOperationException("hub hiccup") : Task.CompletedTask;
        await r.ChangeAsync(db =>
        {
            db.Users.First(u => u.Id == unlucky).IsActive = false;
            db.Users.First(u => u.Id == fine).IsActive = false;
        });

        Assert.Empty(r.GroupsOf(fineConn));
        Assert.True(fineCaller.Aborted);
        Assert.True(unluckyCaller.Aborted); // the failed group call did not keep them connected
    }

    [Fact]
    public async Task Two_changes_in_a_row_leave_the_connection_in_the_groups_of_the_newest_data()
    {
        var r = await CreateAsync();
        var person = await r.AddUserAsync(await r.AddRoleAsync(), r.DeptA);
        var (id, _) = await r.OpenAsync(person);

        // The first refresh is held part-way through (its group call waits), while a second change is saved behind it.
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldOnce = 0;
        r.Hub.FakeGroups.Before = async (_, _, _) =>
        {
            if (Interlocked.Increment(ref heldOnce) == 1)
            {
                held.SetResult();
                await release.Task;
            }
        };

        var firstRefresh = r.ChangeAsync(db => db.Users.First(u => u.Id == person).DepartmentId = r.DeptB.Id);
        await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondRefresh = r.ChangeAsync(db => db.Users.First(u => u.Id == person).DepartmentId = r.DeptC.Id);
        await Task.Delay(150); // give the second one every chance to jump the queue
        release.SetResult();
        await Task.WhenAll(firstRefresh, secondRefresh).WaitAsync(TimeSpan.FromSeconds(5));

        AssertGroups(new[] { KpiHub.UserGroup(person), KpiHub.DepartmentGroup(r.DeptC.Id) }, r.GroupsOf(id));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KPI.Tests;

/// <summary>
/// A person who is already connected to the real-time hub when their access changes must stop hearing about issues they
/// may no longer see (and start hearing about ones they now may), without waiting for the connection to drop.
/// </summary>
public class HubAccessRevocationTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private readonly ApiFactory _factory;
    private readonly List<HubConnection> _open = [];

    public HubAccessRevocationTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (!_seeded) { await _factory.SeedAsync(); _seeded = true; }
        }
        finally { SeedLock.Release(); }
    }

    public async Task DisposeAsync()
    {
        foreach (var connection in _open) await connection.DisposeAsync();
    }

    // ───────────── helpers ─────────────

    private sealed class Watcher
    {
        private readonly List<string> _titles = [];
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HubConnection Connection { get; init; } = null!;

        public void Add(string title) { lock (_titles) _titles.Add(title); }
        public bool Heard(string title) { lock (_titles) return _titles.Contains(title); }
    }

    private async Task<Watcher> WatchAsync(string userName, string? token = null)
    {
        token ??= await _factory.TokenAsync(userName);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/kpi"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var watcher = new Watcher { Connection = connection };
        connection.On<JsonElement>("IssueChanged", e => watcher.Add(e.GetProperty("title").GetString()!));
        connection.Closed += _ => { watcher.Closed.TrySetResult(); return Task.CompletedTask; };
        await connection.StartAsync();
        _open.Add(connection);
        return watcher;
    }

    private async Task<string> NewUserAsync(string roleName, Guid? departmentId, Guid? roleId = null)
    {
        var name = $"live-{Guid.NewGuid():N}"[..14];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User
        {
            FirstName = name, LastName = "Tester", Email = $"{name}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(ApiFactory.Password, 4),
            RoleId = roleId ?? (await db.Roles.FirstAsync(r => r.Name == roleName)).Id,
            DepartmentId = departmentId, IsActive = true,
        });
        await db.SaveChangesAsync();
        return name;
    }

    private async Task<string> ReportAsync(string reporter, string titlePrefix)
    {
        var title = $"{titlePrefix} {Guid.NewGuid():N}"[..40];
        var client = await _factory.ClientAsync(reporter);
        var form = new MultipartFormDataContent
        {
            { new StringContent(title), "Title" },
            { new StringContent("Something is not working and this is the description of it"), "Description" },
            { new StringContent("Bug"), "Category" },
            { new StringContent("Login"), "Module" },
            { new StringContent("Degraded"), "Impact" },
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/issues", form)).StatusCode);
        return title;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Did not happen in time: {what}");
            await Task.Delay(25);
        }
    }

    /// <summary>Reports an issue and waits until the control connection has heard it, then lets stragglers arrive.</summary>
    private async Task<string> ReportAndSettleAsync(string reporter, Watcher control)
    {
        var title = await ReportAsync(reporter, "Live check");
        await WaitUntilAsync(() => control.Heard(title), "the control connection hears the new issue");
        await Task.Delay(200);
        return title;
    }

    private async Task<HttpResponseMessage> AsAdminAsync(HttpMethod method, string url, object? body = null)
    {
        var admin = await _factory.ClientAsync("admin");
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await admin.SendAsync(request);
    }

    private Guid IdOf(string userName) => _factory.UserId(userName);

    // ───────────── deactivation ─────────────

    [Fact]
    public async Task A_deactivated_person_is_disconnected_at_once_on_every_connection_and_hears_nothing_more()
    {
        var control = await WatchAsync("it-a");
        var person = await NewUserAsync(RoleNames.ITSupport, _factory.DeptB.Id);
        var firstTab = await WatchAsync(person);
        var secondTab = await WatchAsync(person);

        var before = await ReportAndSettleAsync("staff-a", control);
        Assert.True(firstTab.Heard(before) && secondTab.Heard(before)); // an IT person hears every new issue

        Assert.Equal(HttpStatusCode.OK, (await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(person)}/deactivate")).StatusCode);

        await Task.WhenAll(firstTab.Closed.Task, secondTab.Closed.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HubConnectionState.Disconnected, firstTab.Connection.State);
        Assert.Equal(HubConnectionState.Disconnected, secondTab.Connection.State);

        var after = await ReportAndSettleAsync("staff-a", control);
        Assert.False(firstTab.Heard(after) || secondTab.Heard(after));
    }

    [Fact]
    public async Task A_person_who_reconnects_while_deactivated_hears_events_again_once_reactivated()
    {
        var control = await WatchAsync("it-a");
        var person = await NewUserAsync(RoleNames.ITSupport, _factory.DeptB.Id);
        var token = await _factory.TokenAsync(person); // still cryptographically valid after the account is switched off
        var firstConnection = await WatchAsync(person, token);

        Assert.Equal(HttpStatusCode.OK, (await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(person)}/deactivate")).StatusCode);
        await firstConnection.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var whileOff = await WatchAsync(person, token); // a page reload: the hub accepts the token but puts it in no group
        var duringSuspension = await ReportAndSettleAsync("staff-a", control);
        Assert.False(whileOff.Heard(duringSuspension));

        Assert.Equal(HttpStatusCode.OK, (await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(person)}/reactivate")).StatusCode);

        var afterwards = await ReportAndSettleAsync("staff-a", control);
        Assert.True(whileOff.Heard(afterwards));
        Assert.Equal(HubConnectionState.Connected, whileOff.Connection.State);
    }

    // ───────────── role ─────────────

    [Fact]
    public async Task A_person_moved_out_of_IT_support_stops_hearing_every_issue_but_stays_connected()
    {
        var control = await WatchAsync("it-a");
        var person = await NewUserAsync(RoleNames.ITSupport, _factory.DeptB.Id);
        var watcher = await WatchAsync(person);
        Assert.True(watcher.Connection.State == HubConnectionState.Connected);
        var before = await ReportAndSettleAsync("staff-a", control);
        Assert.True(watcher.Heard(before));

        Assert.Equal(HttpStatusCode.OK,
            (await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(person)}/role", new { roleName = RoleNames.Staff })).StatusCode);

        var after = await ReportAndSettleAsync("staff-a", control);
        Assert.False(watcher.Heard(after));
        Assert.Equal(HubConnectionState.Connected, watcher.Connection.State);
    }

    [Fact]
    public async Task A_person_promoted_to_IT_support_starts_hearing_issues_without_reconnecting()
    {
        var control = await WatchAsync("it-a");
        var person = await NewUserAsync(RoleNames.Staff, _factory.DeptB.Id);
        var watcher = await WatchAsync(person);
        var before = await ReportAndSettleAsync("staff-a", control);
        Assert.False(watcher.Heard(before)); // a requester in another department hears nothing about it

        Assert.Equal(HttpStatusCode.OK,
            (await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(person)}/role", new { roleName = RoleNames.ITSupport })).StatusCode);

        var after = await ReportAndSettleAsync("staff-a", control);
        Assert.True(watcher.Heard(after));
        Assert.Equal(HubConnectionState.Connected, watcher.Connection.State);
    }

    // ───────────── department ─────────────

    [Fact]
    public async Task A_manager_moved_to_another_department_hears_that_department_instead()
    {
        var control = await WatchAsync("it-a");
        var manager = await NewUserAsync(RoleNames.Manager, _factory.DeptA.Id);
        var watcher = await WatchAsync(manager);
        var inA = await ReportAndSettleAsync("staff-a", control);
        Assert.True(watcher.Heard(inA));

        var move = await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(manager)}",
            new { firstName = manager, lastName = "Tester", departmentId = _factory.DeptB.Id });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        var stillInA = await ReportAndSettleAsync("staff-a", control);
        var nowInB = await ReportAndSettleAsync("staff-b", control);
        Assert.False(watcher.Heard(stillInA));
        Assert.True(watcher.Heard(nowInB));
    }

    // ───────────── rights of a role ─────────────

    [Fact]
    public async Task Taking_the_right_to_work_issues_from_a_role_takes_effect_for_people_already_connected()
    {
        var control = await WatchAsync("it-a");
        var (roleId, personName) = await NewCustomRoleUserAsync(withManage: true);
        var watcher = await WatchAsync(personName);
        var before = await ReportAndSettleAsync("staff-a", control);
        Assert.True(watcher.Heard(before));

        await SetManageAsync(roleId, granted: false);

        var withdrawn = await ReportAndSettleAsync("staff-a", control);
        Assert.False(watcher.Heard(withdrawn));

        await SetManageAsync(roleId, granted: true);

        var restored = await ReportAndSettleAsync("staff-a", control);
        Assert.True(watcher.Heard(restored));
        Assert.Equal(HubConnectionState.Connected, watcher.Connection.State);
    }

    private async Task<(Guid RoleId, string UserName)> NewCustomRoleUserAsync(bool withManage)
    {
        Guid roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = new Role { Name = $"Custom {Guid.NewGuid():N}"[..20], Description = "test", IssueVisibility = IssueVisibility.Own };
            db.Roles.Add(role);
            var names = new List<string> { Permissions.Issue.View, Permissions.Issue.Create };
            if (withManage) names.Add(Permissions.Issue.Manage);
            foreach (var permission in await db.Permissions.Where(p => names.Contains(p.Name)).ToListAsync())
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            await db.SaveChangesAsync();
            roleId = role.Id;
        }
        return (roleId, await NewUserAsync("unused", _factory.DeptB.Id, roleId));
    }

    private async Task SetManageAsync(Guid roleId, bool granted)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manage = await db.Permissions.FirstAsync(p => p.Name == Permissions.Issue.Manage);
        var existing = await db.RolePermissions.Where(rp => rp.RoleId == roleId && rp.PermissionId == manage.Id).ToListAsync();
        if (granted && existing.Count == 0) db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = manage.Id });
        if (!granted) db.RolePermissions.RemoveRange(existing);
        await db.SaveChangesAsync();
    }

    // ───────────── nothing else should be disturbed ─────────────

    [Fact]
    public async Task Editing_a_persons_name_does_not_disturb_their_connection_or_what_they_hear()
    {
        var control = await WatchAsync("it-a");
        var person = await NewUserAsync(RoleNames.ITSupport, _factory.DeptB.Id);
        var watcher = await WatchAsync(person);

        var edit = await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(person)}",
            new { firstName = "Renamed", lastName = "Person", departmentId = _factory.DeptB.Id });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var after = await ReportAndSettleAsync("staff-a", control);
        Assert.True(watcher.Heard(after));
        Assert.Equal(HubConnectionState.Connected, watcher.Connection.State);
    }

    [Fact]
    public async Task Changing_someone_else_does_not_disturb_a_person_who_is_connected()
    {
        var control = await WatchAsync("it-a");
        var bystander = await WatchAsync(await NewUserAsync(RoleNames.ITSupport, _factory.DeptB.Id));
        var other = await NewUserAsync(RoleNames.ITSupport, _factory.DeptB.Id);

        Assert.Equal(HttpStatusCode.OK, (await AsAdminAsync(HttpMethod.Put, $"/api/users/{IdOf(other)}/deactivate")).StatusCode);

        var after = await ReportAndSettleAsync("staff-a", control);
        Assert.True(bystander.Heard(after));
        Assert.Equal(HubConnectionState.Connected, bystander.Connection.State);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Kpis;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Enums;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KPI.Tests;

/// <summary>Boots the real ASP.NET pipeline (auth, policies, exception handler, SignalR) on an in-memory database.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Passw0rd!";
    private readonly string _dbName = Guid.NewGuid().ToString();

    public Department DeptA { get; private set; } = null!;
    public Department DeptB { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-0123456789-abcdef");
        builder.UseSetting("Jwt:Issuer", "kpi-test");
        builder.UseSetting("Jwt:Audience", "kpi-test");
        builder.UseSetting("Resend:ApiKey", "re_test");
        builder.UseSetting("Email:Enabled", "false"); // never call the real mail provider from tests
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused");

        builder.ConfigureServices(services =>
        {
            // Swap Npgsql for an in-memory store (EF 9+ keeps provider setup in two descriptors).
            foreach (var descriptor in services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>))
                .ToList())
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DbSeeder.SeedAsync(db); // Program already ran it; this is a no-op safety net

        DeptA = new Department { Name = "Dept A", Code = "A" };
        DeptB = new Department { Name = "Dept B", Code = "B" };
        db.Departments.AddRange(DeptA, DeptB);
        await db.SaveChangesAsync();

        await AddUserAsync(db, "admin", RoleNames.SuperAdmin, null);
        await AddUserAsync(db, "admin2", RoleNames.SuperAdmin, null);
        await AddUserAsync(db, "director", RoleNames.Director, null);
        await AddUserAsync(db, "manager-a", RoleNames.Manager, DeptA.Id);
        await AddUserAsync(db, "manager-b", RoleNames.Manager, DeptB.Id);
        await AddUserAsync(db, "staff-a", RoleNames.Staff, DeptA.Id);
        await AddUserAsync(db, "staff-b", RoleNames.Staff, DeptB.Id);
        await AddUserAsync(db, "viewer-a", RoleNames.Viewer, DeptA.Id);
        await AddUserAsync(db, "retired", RoleNames.Manager, DeptA.Id, active: false);
    }

    private static async Task AddUserAsync(AppDbContext db, string name, string role, Guid? departmentId, bool active = true)
    {
        var roleId = (await db.Roles.FirstAsync(r => r.Name == role)).Id;
        db.Users.Add(new User
        {
            FirstName = name,
            LastName = "Tester",
            Email = $"{name}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 4),
            RoleId = roleId,
            DepartmentId = departmentId,
            IsActive = active,
        });
        await db.SaveChangesAsync();
    }

    public Guid UserId(string name)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Users.First(u => u.Email == $"{name}@example.com").Id;
    }

    public async Task<string> TokenAsync(string name)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = $"{name}@example.com", password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    public async Task<HttpClient> ClientAsync(string name)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(name));
        return client;
    }
}

public class ApiIntegrationTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    public ApiIntegrationTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (!_seeded) { await _factory.SeedAsync(); _seeded = true; }
        }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object NewKpi(Guid departmentId, string name = "HTTP KPI") => new
    {
        name,
        description = "d",
        year = 2026,
        target = 100,
        startDate = "2026-01-01",
        dueDate = "2026-12-31",
        departmentId,
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // ───────────── Authentication ─────────────

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/kpi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Public_self_registration_is_closed()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { firstName = "A", lastName = "B", email = "new@example.com", password = "Passw0rd!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_returns_a_token_with_the_roles_permissions()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "Manager-A@Example.com", password = ApiFactory.Password }); // case-insensitive
        response.EnsureSuccessStatusCode();
        var body = await JsonAsync(response);

        Assert.Equal("Manager", body.GetProperty("role").GetString());
        var permissions = body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(Permissions.Kpi.Create, permissions);
        Assert.DoesNotContain(Permissions.Kpi.Approve, permissions);
    }

    [Fact]
    public async Task Wrong_password_and_deactivated_accounts_get_the_same_error()
    {
        var client = _factory.CreateClient();
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "manager-a@example.com", password = "nope" });
        var inactive = await client.PostAsJsonAsync("/api/auth/login", new { email = "retired@example.com", password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal((await JsonAsync(wrong)).GetProperty("message").GetString(),
                     (await JsonAsync(inactive)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Me_returns_the_profile_and_permissions()
    {
        var client = await _factory.ClientAsync("staff-a");
        var body = await JsonAsync(await client.GetAsync("/api/auth/me"));

        Assert.Equal("Staff", body.GetProperty("role").GetString());
        Assert.Equal(_factory.DeptA.Id, body.GetProperty("departmentId").GetGuid());
        Assert.Contains(Permissions.Kpi.Progress, body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    // ───────────── Authorization over HTTP ─────────────

    [Fact]
    public async Task Staff_cannot_reach_administrative_or_approval_endpoints()
    {
        var staff = await _factory.ClientAsync("staff-a");
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/export/kpi/excel")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync("/api/jobs/trigger-delay-detection", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/kpi/{someId}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync($"/api/users/{_factory.UserId("staff-a")}/role", new { roleName = "SuperAdmin" })).StatusCode);
    }

    [Fact]
    public async Task A_user_can_no_longer_promote_themselves_to_SuperAdmin()
    {
        var staff = await _factory.ClientAsync("staff-b");
        var response = await staff.PutAsJsonAsync($"/api/users/{_factory.UserId("staff-b")}/role", new { roleName = "SuperAdmin" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Users.Where(u => u.Email == "staff-b@example.com").Select(u => u.Role.Name).FirstAsync();
        Assert.Equal("Staff", role);
    }

    [Fact]
    public async Task Viewer_can_read_but_not_write()
    {
        var viewer = await _factory.ClientAsync("viewer-a");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/kpi")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id))).StatusCode);
    }

    [Fact]
    public async Task A_token_stops_working_the_moment_the_account_is_deactivated()
    {
        var admin = await _factory.ClientAsync("admin");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id;
            db.Users.Add(new User
            {
                FirstName = "temp", LastName = "T", Email = "temp@example.com", RoleId = role,
                DepartmentId = _factory.DeptA.Id, PasswordHash = BCrypt.Net.BCrypt.HashPassword(ApiFactory.Password, 4),
            });
            await db.SaveChangesAsync();
        }
        var temp = await _factory.ClientAsync("temp");
        Assert.Equal(HttpStatusCode.OK, (await temp.GetAsync("/api/kpi")).StatusCode);

        var tempId = _factory.UserId("temp");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/users/{tempId}/deactivate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await temp.GetAsync("/api/kpi")).StatusCode); // same, still-valid JWT
    }

    // ───────────── Error mapping ─────────────

    [Fact]
    public async Task Errors_use_the_right_status_codes_and_keep_a_message_field()
    {
        var manager = await _factory.ClientAsync("manager-a");

        var notFound = await manager.GetAsync($"/api/kpi/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.False(string.IsNullOrEmpty((await JsonAsync(notFound)).GetProperty("message").GetString()));

        var invalid = await manager.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id, name: ""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("Name", (await JsonAsync(invalid)).GetProperty("message").GetString());

        var wrongDept = await manager.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptB.Id));
        Assert.Equal(HttpStatusCode.Forbidden, wrongDept.StatusCode);

        var badGuid = await manager.GetAsync("/api/kpi/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, badGuid.StatusCode); // route constraint {id:guid}
    }

    // ───────────── The whole workflow, over HTTP ─────────────

    [Fact]
    public async Task A_kpi_travels_from_draft_to_closed_and_each_step_is_policed()
    {
        var manager = await _factory.ClientAsync("manager-a");
        var director = await _factory.ClientAsync("director");
        var otherManager = await _factory.ClientAsync("manager-b");

        // create → Draft
        var created = await manager.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id, "Flow KPI"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var kpi = await JsonAsync(created);
        var id = kpi.GetProperty("id").GetGuid();
        Assert.Equal("Draft", kpi.GetProperty("statusLabel").GetString());

        // progress on a draft is refused (used to silently promote it to Active)
        var early = await manager.PostAsJsonAsync("/api/kpi/progress", new { kpiId = id, progress = 10, notes = "" });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        // another department cannot even see it
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync($"/api/kpi/{id}")).StatusCode);

        // submit → PendingApproval; a manager cannot approve
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync($"/api/kpi/{id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/kpi/{id}/approve", null)).StatusCode);

        // reject needs a reason, then sends it back
        var noReason = await director.PostAsJsonAsync($"/api/kpi/{id}/reject", new { reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var rejected = await JsonAsync(await director.PostAsJsonAsync($"/api/kpi/{id}/reject", new { reason = "Needs a clearer target" }));
        Assert.Equal("Draft", rejected.GetProperty("statusLabel").GetString());
        Assert.Equal("Needs a clearer target", rejected.GetProperty("rejectReason").GetString());

        // resubmit → approve → Active
        await manager.PostAsync($"/api/kpi/{id}/submit", null);
        var approved = await JsonAsync(await director.PostAsync($"/api/kpi/{id}/approve", null));
        Assert.Equal("Active", approved.GetProperty("statusLabel").GetString());

        // progress up to the target completes it
        var done = await JsonAsync(await manager.PostAsJsonAsync("/api/kpi/progress", new { kpiId = id, progress = 100, notes = "done" }));
        Assert.Equal("Completed", done.GetProperty("statusLabel").GetString());

        // the manager sees "updateProgress" but not "close"; the director the reverse
        var asManager = (await JsonAsync(await manager.GetAsync($"/api/kpi/{id}"))).GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ToList();
        var asDirector = (await JsonAsync(await director.GetAsync($"/api/kpi/{id}"))).GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ToList();
        Assert.Contains("updateProgress", asManager);
        Assert.DoesNotContain("close", asManager);
        Assert.Contains("close", asDirector);
        Assert.DoesNotContain("updateProgress", asDirector);

        // close → frozen
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/kpi/{id}/close", null)).StatusCode);
        var closed = await JsonAsync(await director.PostAsync($"/api/kpi/{id}/close", null));
        Assert.Equal("Closed", closed.GetProperty("statusLabel").GetString());
        var afterClose = await manager.PostAsJsonAsync("/api/kpi/progress", new { kpiId = id, progress = 50, notes = "" });
        Assert.Equal(HttpStatusCode.Conflict, afterClose.StatusCode);
    }

    [Fact]
    public async Task Export_only_contains_the_callers_department()
    {
        var managerA = await _factory.ClientAsync("manager-a");
        var managerB = await _factory.ClientAsync("manager-b");
        await managerA.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id, "Only in A"));
        await managerB.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptB.Id, "Only in B"));

        var excel = await managerA.GetAsync("/api/export/kpi/excel");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        using var workbook = new ClosedXML.Excel.XLWorkbook(await excel.Content.ReadAsStreamAsync());
        var names = workbook.Worksheet(1).RowsUsed().Skip(1).Select(r => r.Cell(1).GetString()).ToList();

        Assert.Contains("Only in A", names);
        Assert.DoesNotContain("Only in B", names);
    }

    // ───────────── Users ─────────────

    [Fact]
    public async Task Users_cannot_deactivate_or_demote_themselves_and_the_last_SuperAdmin_is_protected()
    {
        var admin = await _factory.ClientAsync("admin");
        var adminId = _factory.UserId("admin");

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/users/{adminId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync($"/api/users/{adminId}/role", new { roleName = "Staff" })).StatusCode);

        // admin2 is the other SuperAdmin: demoting it is fine only while admin remains
        var admin2Id = _factory.UserId("admin2");
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/users/{admin2Id}/role", new { roleName = "Manager" })).StatusCode);
        // restore for other tests
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/users/{admin2Id}/role", new { roleName = "SuperAdmin" })).StatusCode);
    }

    [Fact]
    public async Task Creating_a_user_enforces_the_password_policy_and_unique_email()
    {
        var admin = await _factory.ClientAsync("admin");
        string roleId;
        using (var scope = _factory.Services.CreateScope())
            roleId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Roles.FirstAsync(r => r.Name == "Staff")).Id.ToString();

        object Body(string email, string password) => new { firstName = "N", lastName = "U", email, password, roleId, departmentId = (string?)null };

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", Body("short@example.com", "abc"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/users", Body("Fresh@Example.com", "LongEnough1"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", Body("fresh@example.com", "LongEnough1"))).StatusCode);
    }

    [Fact]
    public async Task A_manager_only_sees_users_of_their_own_department()
    {
        var manager = await _factory.ClientAsync("manager-a");
        var users = await JsonAsync(await manager.GetAsync("/api/users"));
        var emails = users.EnumerateArray().Select(u => u.GetProperty("email").GetString()).ToList();

        Assert.Contains("staff-a@example.com", emails);
        Assert.DoesNotContain("staff-b@example.com", emails);
        Assert.DoesNotContain("admin@example.com", emails);
    }

    // ───────────── CORS ─────────────

    [Fact]
    public async Task Cors_allows_credentials_only_for_listed_origins()
    {
        var client = _factory.CreateClient();

        HttpRequestMessage Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/kpi/negotiate");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "authorization,x-signalr-user-agent");
            return request;
        }

        var allowed = await client.SendAsync(Preflight("http://localhost:5173"));
        Assert.Equal("http://localhost:5173", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        var blocked = await client.SendAsync(Preflight("https://evil.example"));
        Assert.False(blocked.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ───────────── SignalR ─────────────

    private HubConnection Connect(string token, HttpTransportType transport = HttpTransportType.LongPolling) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/kpi"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = transport;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

    [Fact]
    public async Task The_hub_requires_a_token_and_accepts_one_in_the_query_string()
    {
        var client = _factory.CreateClient();

        var anonymous = await client.PostAsync("/hubs/kpi/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Browsers cannot set headers on a WebSocket, so the JS client sends ?access_token=...
        var token = await _factory.TokenAsync("director");
        var viaQuery = await client.PostAsync($"/hubs/kpi/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(HttpStatusCode.OK, viaQuery.StatusCode);

        // The query-string token is honoured on hub URLs only.
        var elsewhere = await client.GetAsync($"/api/kpi?access_token={token}");
        Assert.Equal(HttpStatusCode.Unauthorized, elsewhere.StatusCode);
    }

    [Fact]
    public async Task Submitting_pushes_a_notification_to_approvers_only_and_a_change_event_to_the_right_people()
    {
        await using var director = Connect(await _factory.TokenAsync("director"));
        await using var viewerA = Connect(await _factory.TokenAsync("viewer-a"));
        await using var staffB = Connect(await _factory.TokenAsync("staff-b"));

        var directorNotifications = new List<JsonElement>();
        var directorChanges = new List<JsonElement>();
        var viewerChanges = new List<JsonElement>();
        var viewerNotifications = new List<JsonElement>();
        var otherDeptChanges = new List<JsonElement>();

        director.On<JsonElement>("Notification", n => { lock (directorNotifications) directorNotifications.Add(n); });
        director.On<JsonElement>("KpiChanged", e => { lock (directorChanges) directorChanges.Add(e); });
        viewerA.On<JsonElement>("KpiChanged", e => { lock (viewerChanges) viewerChanges.Add(e); });
        viewerA.On<JsonElement>("Notification", n => { lock (viewerNotifications) viewerNotifications.Add(n); });
        staffB.On<JsonElement>("KpiChanged", e => { lock (otherDeptChanges) otherDeptChanges.Add(e); });

        await director.StartAsync();
        await viewerA.StartAsync();
        await staffB.StartAsync();

        var manager = await _factory.ClientAsync("manager-a");
        var created = await JsonAsync(await manager.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id, "Live KPI")));
        var id = created.GetProperty("id").GetGuid();
        await manager.PostAsync($"/api/kpi/{id}/submit", null);

        await WaitUntilAsync(() => { lock (directorNotifications) return directorNotifications.Count >= 1; });
        await WaitUntilAsync(() => { lock (viewerChanges) return viewerChanges.Count >= 2; });

        // Director (sees every department) is told approval is needed, and gets both change events.
        lock (directorNotifications)
        {
            var notification = Assert.Single(directorNotifications);
            Assert.Equal("Approval needed", notification.GetProperty("title").GetString());
            Assert.Equal(id, notification.GetProperty("kpiId").GetGuid());
        }
        lock (directorChanges) Assert.Contains(directorChanges, e => e.GetProperty("action").GetString() == "submitted");

        // A viewer in the same department refreshes their list but is not pinged for approval.
        lock (viewerChanges) Assert.Contains(viewerChanges, e => e.GetProperty("kpiId").GetGuid() == id);
        lock (viewerNotifications) Assert.Empty(viewerNotifications);

        // Someone in another department hears nothing about it.
        await Task.Delay(300);
        lock (otherDeptChanges) Assert.Empty(otherDeptChanges);
    }

    [Fact]
    public async Task Approval_notifies_only_the_owner()
    {
        await using var ownerHub = Connect(await _factory.TokenAsync("manager-a"));
        await using var bystander = Connect(await _factory.TokenAsync("staff-a"));
        var ownerMessages = new List<JsonElement>();
        var bystanderMessages = new List<JsonElement>();
        ownerHub.On<JsonElement>("Notification", n => { lock (ownerMessages) ownerMessages.Add(n); });
        bystander.On<JsonElement>("Notification", n => { lock (bystanderMessages) bystanderMessages.Add(n); });
        await ownerHub.StartAsync();
        await bystander.StartAsync();

        var manager = await _factory.ClientAsync("manager-a");
        var director = await _factory.ClientAsync("director");
        var id = (await JsonAsync(await manager.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id, "Approve me")))).GetProperty("id").GetGuid();
        await manager.PostAsync($"/api/kpi/{id}/submit", null);
        await director.PostAsync($"/api/kpi/{id}/approve", null);

        await WaitUntilAsync(() => { lock (ownerMessages) return ownerMessages.Count >= 1; });
        lock (ownerMessages) Assert.Contains(ownerMessages, m => m.GetProperty("title").GetString() == "KPI approved");
        await Task.Delay(300);
        lock (bystanderMessages) Assert.Empty(bystanderMessages);
    }

    [Fact]
    public async Task A_deactivated_user_connecting_to_the_hub_receives_nothing()
    {
        await using var retired = Connect(await TokenForInactiveAsync());
        var received = new List<JsonElement>();
        retired.On<JsonElement>("KpiChanged", e => { lock (received) received.Add(e); });
        await retired.StartAsync(); // the token is still cryptographically valid

        var manager = await _factory.ClientAsync("manager-a");
        await manager.PostAsJsonAsync("/api/kpi", NewKpi(_factory.DeptA.Id, "Nobody told retired"));

        await Task.Delay(400);
        lock (received) Assert.Empty(received);
    }

    private async Task<string> TokenForInactiveAsync()
    {
        // Issue a token while the account is active, then deactivate it (mirrors a JWT outliving access).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == "retired@example.com");
        user.IsActive = true;
        await db.SaveChangesAsync();
        var token = await _factory.TokenAsync("retired");
        user.IsActive = false;
        await db.SaveChangesAsync();
        return token;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Expected real-time message did not arrive.");
            await Task.Delay(25);
        }
    }
}

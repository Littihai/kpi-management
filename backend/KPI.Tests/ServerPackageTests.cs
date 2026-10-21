using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.API.Startup;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KPI.Tests;

/// <summary>The same app, with a built copy of the web app next to it, the way the server package ships it.</summary>
public class FrontendFactory : ApiFactory
{
    public string WebRoot { get; } = Path.Combine(Path.GetTempPath(), "helpdesk-web-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Path.Combine(WebRoot, "assets"));
        File.WriteAllText(Path.Combine(WebRoot, "index.html"), "<!doctype html><title>Helpdesk</title><div id=\"root\"></div>");
        File.WriteAllText(Path.Combine(WebRoot, "assets", "app-abc123.js"), "console.log('app')");
        File.WriteAllText(Path.Combine(WebRoot, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        base.ConfigureWebHost(builder);
        builder.UseWebRoot(WebRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(WebRoot)) Directory.Delete(WebRoot, recursive: true);
    }
}

public class FrontendServingTests : IClassFixture<FrontendFactory>
{
    private readonly FrontendFactory _factory;

    public FrontendServingTests(FrontendFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/")]
    [InlineData("/dashboard")]
    [InlineData("/issues/3f2a9c1e-0000-0000-0000-000000000001")]
    [InlineData("/settings/security")]
    public async Task Any_page_of_the_app_gets_index_html_so_a_refresh_keeps_the_route(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("<div id=\"root\">", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public async Task Built_files_are_served_and_hashed_assets_are_cached_for_good()
    {
        var client = _factory.CreateClient();

        var asset = await client.GetAsync("/assets/app-abc123.js");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl?.ToString() ?? "");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/favicon.svg")).StatusCode);
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/hubs/nothing-here")]
    [InlineData("/health/nothing-here")]
    [InlineData("/assets/missing-file.js")]
    public async Task A_missing_api_route_or_file_is_a_404_not_the_web_page(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<div id=\"root\">", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_api_is_unchanged_next_to_the_pages()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/issues")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/client-config")).StatusCode);
    }
}

public class AdminCommandTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public AdminCommandTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Func<string, string?> Env(string? email, string? password, string? first = "First", string? last = "Last") =>
        name => name switch
        {
            "HELPDESK_ADMIN_EMAIL" => email,
            "HELPDESK_ADMIN_PASSWORD" => password,
            "HELPDESK_ADMIN_FIRST_NAME" => first,
            "HELPDESK_ADMIN_LAST_NAME" => last,
            _ => null,
        };

    private async Task<(int Code, string Output)> RunAsync(Func<string, string?> env)
    {
        var output = new StringWriter();
        var code = await AdminCommand.RunAsync(_factory.Services, env, output);
        return (code, output.ToString());
    }

    private async Task<bool> UserExistsAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Email == email);
    }

    // The seeded test world already has administrators, so these use a factory-wide switch: remove them for the test.
    private async Task WithNoAdministratorAsync(Func<Task> test)
    {
        List<Guid> ids;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admins = await db.Users.Where(u => u.Role.Name == RoleNames.SuperAdmin && u.IsActive).ToListAsync();
            ids = admins.Select(a => a.Id).ToList();
            foreach (var admin in admins) admin.IsActive = false;
            await db.SaveChangesAsync();
        }
        try { await test(); }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var user in await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync()) user.IsActive = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task It_creates_a_super_admin_who_can_sign_in_with_the_password_that_was_given()
    {
        await WithNoAdministratorAsync(async () =>
        {
            var (code, output) = await RunAsync(Env(" Boss@Company.TH ", "A-Long-Admin-Pass-1", "สมศักดิ์", "ผู้ดูแล"));

            Assert.Equal(0, code);
            Assert.Contains("boss@company.th", output);
            Assert.DoesNotContain("A-Long-Admin-Pass-1", output);
            var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "boss@company.th", password = "A-Long-Admin-Pass-1" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var body = await login.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("SuperAdmin", body.GetProperty("role").GetString());
            Assert.Equal("สมศักดิ์ ผู้ดูแล", body.GetProperty("fullName").GetString());
            Assert.Contains(Permissions.Role.Manage, body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        });
    }

    [Fact]
    public async Task It_does_nothing_when_an_administrator_already_exists_and_says_so()
    {
        var (code, output) = await RunAsync(Env("second.admin@company.th", "A-Long-Admin-Pass-1"));

        Assert.Equal(0, code);
        Assert.Contains("already exists", output);
        Assert.False(await UserExistsAsync("second.admin@company.th"));
    }

    [Theory]
    [InlineData(null, "A-Long-Admin-Pass-1", "First")]
    [InlineData("a@b.co", null, "First")]
    [InlineData("a@b.co", "A-Long-Admin-Pass-1", "")]
    public async Task It_stops_with_code_2_when_something_is_missing(string? email, string? password, string? first)
    {
        var (code, _) = await RunAsync(Env(email, password, first));
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task It_refuses_a_bad_address_and_a_password_the_policy_rejects_and_creates_nothing()
    {
        await WithNoAdministratorAsync(async () =>
        {
            Assert.Equal(3, (await RunAsync(Env("not-an-address", "A-Long-Admin-Pass-1"))).Code);

            var (code, output) = await RunAsync(Env("weak@company.th", "short"));
            Assert.Equal(3, code);
            Assert.Contains("รหัสผ่าน", output);
            Assert.False(await UserExistsAsync("weak@company.th"));
        });
    }

    [Fact]
    public async Task It_refuses_an_address_that_belongs_to_someone_who_is_not_an_administrator()
    {
        await WithNoAdministratorAsync(async () =>
        {
            var (code, output) = await RunAsync(Env("staff-a@example.com", "A-Long-Admin-Pass-1"));

            Assert.Equal(4, code);
            Assert.Contains("not an administrator", output);
        });
    }
}

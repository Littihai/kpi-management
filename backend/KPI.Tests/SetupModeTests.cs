using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.API.Startup.Setup;
using KPI.Application.Database;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "helpdesk-setup-" + Guid.NewGuid().ToString("N")[..12]);
    public TempFolder() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { /* leftovers in %TEMP% are harmless */ } }
}

internal sealed class MovableClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public class SetupKeyTests
{
    [Fact]
    public void A_generated_key_is_long_and_easy_to_read_out()
    {
        var key = SetupKey.Generate();

        Assert.Equal(24, key.Length);
        Assert.All(key, c => Assert.DoesNotContain(c, "0O1lI"));   // the characters that are mistaken for each other
        Assert.NotEqual(key, SetupKey.Generate());
    }

    [Fact]
    public void A_key_in_the_configuration_is_used_as_it_is_and_no_file_is_written()
    {
        using var folder = new TempFolder();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Setup:Key"] = "from-the-config-1" }).Build();

        var key = SetupKey.Resolve(config, folder.File("database.json"));

        Assert.True(key.Matches("from-the-config-1"));
        Assert.Null(key.File);
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void Without_one_in_the_configuration_a_key_is_written_beside_the_settings_file_and_reused_after_a_restart()
    {
        using var folder = new TempFolder();
        var config = new ConfigurationBuilder().Build();

        var first = SetupKey.Resolve(config, folder.File("database.json"));
        var second = SetupKey.Resolve(config, folder.File("database.json"));

        Assert.Equal(folder.File("setup-key.txt"), first.File);
        Assert.True(first.Matches(File.ReadAllText(first.File!).Trim()));
        Assert.Equal(first.Reveal(), second.Reveal());
    }

    [Fact]
    public void A_key_that_is_too_short_in_the_configuration_is_ignored()
    {
        using var folder = new TempFolder();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Setup:Key"] = "abc" }).Build();

        var key = SetupKey.Resolve(config, folder.File("database.json"));

        Assert.False(key.Matches("abc"));
        Assert.NotNull(key.File);
    }

    [Fact]
    public void Only_the_exact_key_matches()
    {
        var key = new SetupKey("correct-horse-battery", null);

        Assert.True(key.Matches("correct-horse-battery"));
        Assert.False(key.Matches("correct-horse-batter"));
        Assert.False(key.Matches("CORRECT-HORSE-BATTERY"));
        Assert.False(key.Matches(""));
        Assert.False(key.Matches(null));
    }

    [Fact]
    public void Ten_wrong_tries_block_for_ten_minutes_then_it_opens_again()
    {
        var clock = new MovableClock();
        var key = new SetupKey("correct-horse-battery", null, clock);

        for (var i = 0; i < SetupKey.MaxFailures - 1; i++) key.RecordFailure();
        Assert.Null(key.BlockedFor());

        key.RecordFailure();
        Assert.NotNull(key.BlockedFor());
        Assert.InRange(key.BlockedFor()!.Value, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(key.BlockedFor());
    }

    [Fact]
    public void A_right_key_clears_the_count_of_wrong_ones()
    {
        var key = new SetupKey("correct-horse-battery", null, new MovableClock());
        for (var i = 0; i < SetupKey.MaxFailures - 1; i++) key.RecordFailure();

        key.RecordSuccess();
        key.RecordFailure();

        Assert.Null(key.BlockedFor());
    }

    [Fact]
    public void Discarding_removes_the_file()
    {
        using var folder = new TempFolder();
        var key = SetupKey.Resolve(new ConfigurationBuilder().Build(), folder.File("database.json"));
        Assert.True(File.Exists(key.File));

        key.Discard();

        Assert.False(File.Exists(folder.File("setup-key.txt")));
    }
}

public class DatabaseConfigFileTests
{
    private static IConfiguration Read(string folder, Dictionary<string, string?>? before = null)
    {
        var configuration = new ConfigurationManager();
        if (before is not null) configuration.AddInMemoryCollection(before);
        DatabaseConfigFile.Add(configuration, configuration, folder);
        return configuration;
    }

    [Fact]
    public void A_SQL_login_is_written_with_the_password_encrypted_and_read_back_as_settings()
    {
        using var folder = new TempFolder();
        var secret = "Pa;ss=w0rd'\"!";
        var encrypted = DatabaseConfigFile.Protect(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "a-long-enough-signing-key-for-the-test" }).Build(), secret);

        DatabaseConfigFile.Write(folder.File("database.json"), new SetupConnection(" srv\\SQL ", " helpdesk ", "sql", " app ", secret, Trust: false), encrypted);

        var text = File.ReadAllText(folder.File("database.json"));
        Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain("Pa;ss", text);
        Assert.False(File.Exists(folder.File("database.json.tmp")));
        var config = Read(folder.Path);
        Assert.Equal("SqlServer", config["Database:Provider"]);
        Assert.Equal("srv\\SQL", config["Database:Server"]);
        Assert.Equal("helpdesk", config["Database:Name"]);
        Assert.Equal("sql", config["Database:Auth"]);
        Assert.Equal("app", config["Database:User"]);
        Assert.Equal("False", config["Database:TrustServerCertificate"]);
        Assert.Equal(encrypted, config["Database:PasswordProtected"]);
    }

    [Fact]
    public void A_Windows_login_has_no_user_and_no_password()
    {
        using var folder = new TempFolder();

        DatabaseConfigFile.Write(folder.File("database.json"), new SetupConnection("srv", "helpdesk", "windows", "ignored", "ignored", true), "ignored");

        var text = File.ReadAllText(folder.File("database.json"));
        Assert.DoesNotContain("ignored", text);
        Assert.DoesNotContain("null", text);   // nothing is written for what does not apply
        var config = Read(folder.Path);
        Assert.Equal("windows", config["Database:Auth"]);
        Assert.Null(config["Database:User"]);
        Assert.Null(config["Database:PasswordProtected"]);
    }

    [Fact]
    public void The_file_wins_over_every_other_source()
    {
        using var folder = new TempFolder();
        DatabaseConfigFile.Write(folder.File("database.json"), new SetupConnection("new-server", "new-db", "windows", null, null), null);

        var config = Read(folder.Path, new() { ["Database:Server"] = "old-server", ["Database:Name"] = "old-db", ["Jwt:Key"] = "kept" });

        Assert.Equal("new-server", config["Database:Server"]);
        Assert.Equal("new-db", config["Database:Name"]);
        Assert.Equal("kept", config["Jwt:Key"]);
    }

    [Fact]
    public void Writing_again_replaces_the_file()
    {
        using var folder = new TempFolder();
        DatabaseConfigFile.Write(folder.File("database.json"), new SetupConnection("one", "db", "windows", null, null), null);
        DatabaseConfigFile.Write(folder.File("database.json"), new SetupConnection("two", "db", "windows", null, null), null);

        Assert.Equal("two", Read(folder.Path)["Database:Server"]);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[1, 2,")]
    public void A_settings_file_that_is_not_valid_json_is_ignored_instead_of_stopping_the_host(string content)
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("database.json"), content);

        var config = Read(folder.Path, new() { ["Database:Server"] = "from-elsewhere", ["Database:Name"] = "db" });

        Assert.Equal("from-elsewhere", config["Database:Server"]);   // the other sources still apply
    }

    [Fact]
    public void A_missing_file_or_folder_is_not_an_error()
    {
        using var folder = new TempFolder();

        Assert.Null(Read(folder.Path)["Database:Server"]);
        var configuration = new ConfigurationManager();
        DatabaseConfigFile.Add(configuration, configuration, folder.File("not-there"));
        Assert.Null(configuration["Database:Server"]);
    }

    [Fact]
    public void The_location_can_be_chosen_in_the_configuration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Setup:ConfigFile"] = "C:\\ProgramData\\Helpdesk\\database.json" }).Build();

        Assert.Equal("C:\\ProgramData\\Helpdesk\\database.json", DatabaseConfigFile.PathFor(config, "C:\\app"));
        Assert.Equal(Path.Combine("C:\\app", "database.json"), DatabaseConfigFile.PathFor(new ConfigurationBuilder().Build(), "C:\\app"));
    }

    [Fact]
    public void Whether_the_folder_can_be_written_is_found_out_without_leaving_anything_behind()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("a-file"), "x");

        Assert.True(DatabaseConfigFile.CanWrite(folder.File("database.json")));
        Assert.True(DatabaseConfigFile.CanWrite(Path.Combine(folder.Path, "new", "sub", "database.json")));
        Assert.False(DatabaseConfigFile.CanWrite(Path.Combine(folder.File("a-file"), "database.json")));   // "a-file" is not a folder
        Assert.Equal(["a-file", "new"], Directory.GetFileSystemEntries(folder.Path).Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public void A_password_cannot_be_kept_without_the_server_key()
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseConfigFile.Protect(new ConfigurationBuilder().Build(), "x"));
    }
}

public class DatabaseConnectionPartsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private const string JwtKey = "a-long-enough-signing-key-for-the-test";

    [Fact]
    public void Nothing_configured_means_setup_is_needed()
    {
        Assert.False(DatabaseProvider.IsConfigured(Config([])));
        Assert.False(DatabaseProvider.IsConfigured(Config(new() { ["ConnectionStrings:DefaultConnection"] = "  " })));
    }

    [Fact]
    public void A_ready_made_connection_string_still_works()
    {
        var config = Config(new() { ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=x" });

        Assert.True(DatabaseProvider.IsConfigured(config));
        Assert.Equal("Host=db;Database=x", DatabaseProvider.ConnectionString(config));
    }

    [Fact]
    public void A_server_without_a_database_name_is_not_a_setting_it_would_open_the_login_default_database()
    {
        var parts = Config(new() { ["Database:Server"] = "srv", ["Database:Auth"] = "windows" });
        var withString = Config(new() { ["Database:Server"] = "srv", ["ConnectionStrings:DefaultConnection"] = "Server=other;Database=x" });

        Assert.False(DatabaseProvider.IsConfigured(parts));
        Assert.Equal("Server=other;Database=x", DatabaseProvider.ConnectionString(withString));
    }

    [Fact]
    public void The_parts_from_the_wizard_win_over_a_ready_made_string()
    {
        var config = Config(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=old;Database=old",
            ["Database:Server"] = "srv\\SQL", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "windows",
        });

        var built = new SqlConnectionStringBuilder(DatabaseProvider.ConnectionString(config));

        Assert.Equal("srv\\SQL", built.DataSource);
        Assert.Equal("helpdesk", built.InitialCatalog);
        Assert.True(built.IntegratedSecurity);
        Assert.True(built.TrustServerCertificate);   // the default: a server's own certificate is what most installations have
    }

    [Fact]
    public void A_SQL_login_password_is_decrypted_and_quoted_so_odd_characters_cannot_change_the_meaning()
    {
        var secret = "p;a=ss'w\"ord; Initial Catalog=other";
        var encrypted = new SecretProtector(JwtKey).Protect(secret);
        var config = Config(new()
        {
            ["Database:Server"] = "srv", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "sql", ["Database:User"] = "app",
            ["Database:PasswordProtected"] = encrypted, ["Database:TrustServerCertificate"] = "false", ["Jwt:Key"] = JwtKey,
        });

        var built = new SqlConnectionStringBuilder(DatabaseProvider.ConnectionString(config));

        Assert.Equal("app", built.UserID);
        Assert.Equal(secret, built.Password);
        Assert.Equal("helpdesk", built.InitialCatalog);
        Assert.False(built.TrustServerCertificate);
        Assert.False(built.IntegratedSecurity);
    }

    [Fact]
    public void A_password_that_cannot_be_decrypted_gives_an_empty_one_so_the_login_fails_and_setup_explains_it()
    {
        var encrypted = new SecretProtector(JwtKey).Protect("secret");
        var config = Config(new()
        {
            ["Database:Server"] = "srv", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "sql", ["Database:User"] = "app",
            ["Database:PasswordProtected"] = encrypted, ["Jwt:Key"] = "a-different-signing-key-entirely-here",
        });

        var built = new SqlConnectionStringBuilder(DatabaseProvider.ConnectionString(config));

        Assert.Equal(string.Empty, built.Password);
        Assert.True(DatabaseProvider.IsConfigured(config));
    }

    [Fact]
    public void The_parts_imply_SQL_Server_when_no_provider_is_named_and_an_explicit_provider_still_wins()
    {
        Assert.Equal(DatabaseEngine.SqlServer, DatabaseProvider.EngineOf(Config(new() { ["Database:Server"] = "srv" })));
        Assert.Equal(DatabaseEngine.PostgreSql, DatabaseProvider.EngineOf(Config([])));
        Assert.Equal(DatabaseEngine.PostgreSql, DatabaseProvider.EngineOf(Config(new() { ["Database:Provider"] = "PostgreSql", ["Database:Server"] = "srv" })));
        Assert.Equal(DatabaseEngine.SqlServer, DatabaseProvider.EngineOf(Config(new() { ["Database:Provider"] = " ", ["Database:Server"] = "srv" })));
    }

    [Fact]
    public void The_database_is_configured_through_the_chosen_engine()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(options, Config(new() { ["Database:Server"] = "srv", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "windows" }));

        using var db = new AppDbContext(options.Options);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.Contains("helpdesk", db.Database.GetConnectionString());
    }
}

public class SetupModeDecisionTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "KPI.API";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void The_tests_never_use_the_supervisor()
    {
        Assert.False(SetupMode.UsesSupervisor(Config([]), new Environment("Testing")));
    }

    [Fact]
    public void With_no_database_chosen_the_supervisor_waits_for_setup()
    {
        Assert.True(SetupMode.UsesSupervisor(Config([]), new Environment("Production")));
    }

    [Fact]
    public void SQL_Server_always_goes_through_the_supervisor_so_a_lost_database_shows_the_setup_page()
    {
        Assert.True(SetupMode.UsesSupervisor(Config(new() { ["Database:Provider"] = "SqlServer", ["ConnectionStrings:DefaultConnection"] = "Server=x;Database=y" }), new Environment("Production")));
    }

    [Fact]
    public void A_configured_PostgreSQL_deployment_runs_as_it_always_did()
    {
        Assert.False(SetupMode.UsesSupervisor(Config(new() { ["ConnectionStrings:DefaultConnection"] = "Host=x;Database=y" }), new Environment("Production")));
        Assert.False(SetupMode.UsesSupervisor(Config(new() { ["Database:Provider"] = "PostgreSql", ["ConnectionStrings:DefaultConnection"] = "Host=x;Database=y" }), new Environment("Production")));
    }

    [Fact]
    public void Setup_can_be_switched_off()
    {
        Assert.False(SetupMode.UsesSupervisor(Config(new() { ["Setup:Enabled"] = "false" }), new Environment("Production")));
        Assert.False(SetupMode.UsesSupervisor(Config(new() { ["Setup:Enabled"] = "False", ["Database:Provider"] = "SqlServer", ["ConnectionStrings:DefaultConnection"] = "Server=x;Database=y" }), new Environment("Production")));
    }

    [Fact]
    public async Task Nothing_configured_needs_setup_without_trying_to_connect()
    {
        var need = await SetupMode.CheckAsync(Config([]), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(SetupReason.NotConfigured, need!.Reason);
    }

    [Fact]
    public async Task A_server_that_is_not_there_needs_setup_once_the_patience_is_used_up()
    {
        var config = Config(new() { ["Database:Server"] = "127.0.0.1,1", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "windows" });

        var need = await SetupMode.CheckAsync(config, NullLogger.Instance, CancellationToken.None, patience: TimeSpan.Zero);

        Assert.Equal(SetupReason.Unreachable, need!.Reason);
        Assert.Equal("unreachable", need.Problem);
        Assert.False(string.IsNullOrEmpty(need.Message));
    }
}

internal sealed class SetupHarness : IAsyncDisposable
{
    public required WebApplication App { get; init; }
    public required HttpClient Client { get; init; }
    public required SetupState State { get; init; }
    public required TempFolder Folder { get; init; }

    public static async Task<SetupHarness> StartAsync(bool local = true, bool allowRemote = false, bool jwt = true, string? configFile = null, SetupNeed? need = null, Dictionary<string, string?>? more = null)
    {
        var folder = new TempFolder();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = folder.Path });
        builder.WebHost.UseTestServer();
        var values = new Dictionary<string, string?> { ["Setup:Key"] = Key };
        if (jwt) values["Jwt:Key"] = JwtKey;
        foreach (var pair in more ?? []) values[pair.Key] = pair.Value;
        builder.Configuration.AddInMemoryCollection(values);

        var file = configFile ?? folder.File("database.json");
        var state = new SetupState
        {
            Need = need ?? new SetupNeed(SetupReason.NotConfigured),
            Key = SetupKey.Resolve(builder.Configuration, file),
            Configuration = builder.Configuration,
            ConfigFile = file,
            AllowRemote = allowRemote,
            CanWriteConfig = DatabaseConfigFile.CanWrite(file),
        };
        var app = builder.Build();
        if (local) app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return next(); });
        SetupEndpoints.Map(app, state);
        await app.StartAsync();
        return new SetupHarness { App = app, Client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient(), State = state, Folder = folder };
    }

    public const string Key = "the-setup-key-for-tests";
public const string JwtKey = "a-long-enough-signing-key-for-the-test";

public async Task<(HttpStatusCode Status, JsonElement Body, string Text)> PostAsync(string url, object body, string? key = Key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("X-Setup-Key", key);
        using var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : default, text);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        Folder.Dispose();
    }
}

/// <summary>The setup API, served without a network by a test server.</summary>
public class SetupEndpointsTests
{
    private const string Key = SetupHarness.Key;
    private const string JwtKey = SetupHarness.JwtKey;

    private static object Windows(string server = "127.0.0.1,1", string database = "helpdesk") => new { server, database, auth = "windows", trust = true };

    [Fact]
    public async Task While_setting_up_the_health_checks_say_so_and_the_application_is_not_ready()
    {
        await using var h = await SetupHarness.StartAsync();

        var health = await h.Client.GetAsync("/health");
        var ready = await h.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"setup\"", await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task Any_other_api_call_says_setup_is_required()
    {
        await using var h = await SetupHarness.StartAsync();

        var response = await h.Client.GetAsync("/api/issues");
        var post = await h.Client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.c", password = "x" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("setupRequired", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, post.StatusCode);
    }

    [Fact]
    public async Task The_client_config_is_still_answered_so_the_desktop_app_can_check_its_version()
    {
        await using var h = await SetupHarness.StartAsync();

        var response = await h.Client.GetAsync("/api/client-config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("minDesktopVersion", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_status_says_what_is_needed_and_never_the_key_or_a_password()
    {
        await using var h = await SetupHarness.StartAsync(need: new SetupNeed(SetupReason.Unreachable, "login-failed", "bad login"), more: new()
        {
            ["Database:Server"] = "srv", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "sql", ["Database:User"] = "app",
            ["Database:PasswordProtected"] = "enc1:SECRETBYTES",
        });

        var text = await h.Client.GetStringAsync("/api/setup/status");

        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("setup", body.GetProperty("mode").GetString());
        Assert.Equal("Unreachable", body.GetProperty("reason").GetString());
        Assert.Equal("login-failed", body.GetProperty("problem").GetString());
        Assert.Equal("SqlServer", body.GetProperty("engine").GetString());
        Assert.True(body.GetProperty("canWriteConfig").GetBoolean());
        Assert.True(body.GetProperty("jwtConfigured").GetBoolean());
        Assert.Equal("srv", body.GetProperty("current").GetProperty("server").GetString());
        Assert.Equal("app", body.GetProperty("current").GetProperty("user").GetString());
        Assert.DoesNotContain(Key, text);
        Assert.DoesNotContain(JwtKey, text);
        Assert.DoesNotContain("SECRETBYTES", text);
        Assert.DoesNotContain("assword", text);
    }

    [Fact]
    public async Task Another_machine_is_told_that_setup_is_needed_but_not_where_the_database_or_the_key_is()
    {
        await using var h = await SetupHarness.StartAsync(local: false, need: new SetupNeed(SetupReason.Unreachable, "login-failed", "bad login"), more: new()
        {
            ["Database:Server"] = "srv", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "sql", ["Database:User"] = "app",
        });

        var text = await h.Client.GetStringAsync("/api/setup/status");

        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("setup", body.GetProperty("mode").GetString());
        Assert.Equal("Unreachable", body.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("current").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("keyFile").ValueKind);
        Assert.DoesNotContain("srv", text);
        Assert.DoesNotContain("app", text.Replace("canWriteConfig", ""));   // the user name
    }

    [Fact]
    public async Task The_read_only_answers_can_be_read_from_another_origin_and_the_actions_cannot()
    {
        await using var h = await SetupHarness.StartAsync();

        foreach (var url in new[] { "/api/setup/status", "/health", "/api/client-config" })
        {
            using var response = await h.Client.GetAsync(url);
            Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        }
        using var test = await h.Client.PostAsJsonAsync("/api/setup/test", Windows());
        Assert.False(test.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task The_status_can_be_read_from_another_machine_but_the_actions_cannot()
    {
        await using var h = await SetupHarness.StartAsync(local: false);

        var status = await h.Client.GetAsync("/api/setup/status");
        var test = await h.PostAsync("/api/setup/test", Windows());
        var apply = await h.PostAsync("/api/setup/apply", new { connection = Windows() });

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, test.Status);
        Assert.Equal("local-only", test.Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, apply.Status);
    }

    [Fact]
    public async Task From_another_machine_the_actions_work_when_the_administrator_allowed_it()
    {
        await using var h = await SetupHarness.StartAsync(local: false, allowRemote: true);

        var test = await h.PostAsync("/api/setup/test", Windows(server: "", database: "x"));

        Assert.Equal(HttpStatusCode.BadRequest, test.Status);   // got past the guard: it is the form that is wrong
    }

    [Fact]
    public async Task Without_the_right_key_nothing_is_done()
    {
        await using var h = await SetupHarness.StartAsync();

        var none = await h.PostAsync("/api/setup/test", Windows(), key: null);
        var wrong = await h.PostAsync("/api/setup/test", Windows(), key: "not-the-key");
        var apply = await h.PostAsync("/api/setup/apply", new { connection = Windows() }, key: "not-the-key");

        Assert.Equal(HttpStatusCode.Unauthorized, none.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal("bad-key", wrong.Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, apply.Status);
        Assert.False(File.Exists(h.State.ConfigFile));
    }

    [Fact]
    public async Task Guessing_the_key_is_stopped_even_for_the_right_key_afterwards()
    {
        await using var h = await SetupHarness.StartAsync();

        for (var i = 0; i < SetupKey.MaxFailures; i++) await h.PostAsync("/api/setup/test", Windows(), key: "guess-" + i);
        var afterwards = await h.PostAsync("/api/setup/test", Windows());

        Assert.Equal(HttpStatusCode.TooManyRequests, afterwards.Status);
        Assert.Equal("too-many-attempts", afterwards.Body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("", "helpdesk", "windows", null, null)]
    [InlineData("srv", "", "windows", null, null)]
    [InlineData("srv", "bad name;drop", "windows", null, null)]
    [InlineData("srv", "helpdesk", "kerberos", null, null)]
    [InlineData("srv", "helpdesk", "sql", "sa", null)]
    [InlineData("srv", "helpdesk", "sql", null, "pw")]
    public async Task A_form_that_is_incomplete_or_odd_is_refused_with_a_message(string server, string database, string auth, string? user, string? password)
    {
        await using var h = await SetupHarness.StartAsync();

        var response = await h.PostAsync("/api/setup/test", new { server, database, auth, user, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("invalid", response.Body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Testing_a_server_that_does_not_answer_says_so_in_the_report()
    {
        await using var h = await SetupHarness.StartAsync();

        var response = await h.PostAsync("/api/setup/test", Windows());

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var report = response.Body.GetProperty("report");
        Assert.False(report.GetProperty("connected").GetBoolean());
        Assert.Equal("unreachable", report.GetProperty("connectionProblem").GetString());
    }

    [Fact]
    public async Task Saving_is_refused_when_the_database_cannot_be_reached_and_nothing_is_written()
    {
        await using var h = await SetupHarness.StartAsync();

        var response = await h.PostAsync("/api/setup/apply", new { connection = Windows() });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("not-connected", response.Body.GetProperty("error").GetString());
        Assert.False(File.Exists(h.State.ConfigFile));
        Assert.False(h.State.Completed.IsCompleted);
    }

    [Theory]
    [InlineData("not-an-address", "Boss", "Wizard-Admin-Pass-1")]
    [InlineData("two words@company.th", "Boss", "Wizard-Admin-Pass-1")]
    [InlineData("boss@company.th", "  ", "Wizard-Admin-Pass-1")]
    [InlineData("boss@company.th", "Boss", "")]
    public async Task An_administrator_that_will_be_refused_is_refused_before_the_database_is_looked_at(string email, string firstName, string password)
    {
        await using var h = await SetupHarness.StartAsync();   // the server in the form does not exist: reaching it would give not-connected

        var response = await h.PostAsync("/api/setup/apply", new { connection = Windows(), admin = new { email, firstName, lastName = "", password } });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("admin", response.Body.GetProperty("error").GetString());
        Assert.False(File.Exists(h.State.ConfigFile));
    }

    [Fact]
    public async Task A_SQL_login_cannot_be_saved_without_the_server_key_that_encrypts_its_password()
    {
        await using var h = await SetupHarness.StartAsync(jwt: false);

        var response = await h.PostAsync("/api/setup/apply", new { connection = new { server = "srv", database = "helpdesk", auth = "sql", user = "app", password = "pw" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("jwt-missing", response.Body.GetProperty("error").GetString());
        Assert.False(File.Exists(h.State.ConfigFile));
    }

    [Fact]
    public async Task Saving_says_so_when_the_settings_file_cannot_be_written()
    {
        using var blocker = new TempFolder();
        File.WriteAllText(blocker.File("a-file"), "x");
        await using var h = await SetupHarness.StartAsync(configFile: Path.Combine(blocker.File("a-file"), "database.json"));

        var status = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;
        var response = await h.PostAsync("/api/setup/apply", new { connection = Windows() });

        Assert.False(status.GetProperty("canWriteConfig").GetBoolean());
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Equal("cannot-write", response.Body.GetProperty("error").GetString());
    }
}

/// <summary>The whole wizard against a real SQL Server (TEST_SQLSERVER, see SqlServerLiveTests); without it these do nothing.</summary>
public class SetupLiveTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("TEST_SQLSERVER");
    private const string JwtKey = SetupHarness.JwtKey;
    private const string AdminPassword = "Wizard-Admin-Pass-1";

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WithServerAsync(bool createDatabase, Func<string, SqlConnectionStringBuilder, Task> test)
    {
        var name = "kpi_setup_" + Guid.NewGuid().ToString("N")[..16];
        var master = new SqlConnectionStringBuilder(Server!) { InitialCatalog = "master" }.ConnectionString;
        if (createDatabase) await ExecuteAsync(master, $"CREATE DATABASE [{name}] COLLATE Thai_CI_AS");
        try { await test(name, new SqlConnectionStringBuilder(Server!)); }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync(master, $"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}] END");
        }
    }

    /// <summary>How the person would fill in the form for the server these tests run against.</summary>
    private static object Form(string database, SqlConnectionStringBuilder server) => new
    {
        server = server.DataSource,
        database,
        auth = server.IntegratedSecurity ? "windows" : "sql",
        user = server.IntegratedSecurity ? null : server.UserID,
        password = server.IntegratedSecurity ? null : server.Password,
        trust = true,
    };

    [Fact]
    public async Task A_database_that_has_not_been_created_yet_is_told_apart_from_a_server_that_is_down()
    {
        if (Server is null) return;
        await WithServerAsync(createDatabase: false, async (name, server) =>
        {
            await using var h = await SetupHarness.StartAsync();

            var result = await h.PostAsync("/api/setup/test", Form(name, server));

            Assert.Equal(HttpStatusCode.OK, result.Status);
            var report = result.Body.GetProperty("report");
            Assert.False(report.GetProperty("connected").GetBoolean());
            Assert.Equal("no-database", report.GetProperty("connectionProblem").GetString());
        });
    }

    [Fact]
    public async Task A_missing_database_is_answered_at_once_and_creating_it_is_noticed_by_the_very_next_test()
    {
        if (Server is null) return;
        await WithServerAsync(createDatabase: false, async (name, server) =>
        {
            await using var h = await SetupHarness.StartAsync();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var before = await h.PostAsync("/api/setup/test", Form(name, server));

            Assert.Equal("no-database", before.Body.GetProperty("report").GetProperty("connectionProblem").GetString());
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}: a missing database must not be retried for seconds");

            // the administrator creates it (with the script, in SSMS) and presses Test again straight away
            await ExecuteAsync(new SqlConnectionStringBuilder(Server!) { InitialCatalog = "master" }.ConnectionString, $"CREATE DATABASE [{name}] COLLATE Thai_CI_AS");
            var after = await h.PostAsync("/api/setup/test", Form(name, server));

            Assert.True(after.Body.GetProperty("report").GetProperty("connected").GetBoolean(), "the failed attempt must not be remembered by the connection pool");
        });
    }

    [Fact]
    public async Task An_empty_database_is_reported_as_needing_everything()
    {
        if (Server is null) return;
        await WithServerAsync(createDatabase: true, async (name, server) =>
        {
            await using var h = await SetupHarness.StartAsync();

            var result = await h.PostAsync("/api/setup/test", Form(name, server));

            var report = result.Body.GetProperty("report");
            Assert.True(report.GetProperty("connected").GetBoolean());
            Assert.True(report.GetProperty("pendingMigrations").GetArrayLength() > 0);
            Assert.False(File.Exists(h.State.ConfigFile));   // testing only looks
        });
    }

    [Fact]
    public async Task Saving_builds_the_tables_makes_the_first_administrator_writes_the_settings_and_lets_the_application_start()
    {
        if (Server is null) return;
        await WithServerAsync(createDatabase: true, async (name, server) =>
        {
            await using var h = await SetupHarness.StartAsync();

            var result = await h.PostAsync("/api/setup/apply", new
            {
                connection = Form(name, server),
                repair = new { applyMigrations = true, createMissingObjects = true, seed = true },
                admin = new { email = "Boss@Company.th", firstName = "Boss", lastName = "Wizard", password = AdminPassword },
            });

            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.True(result.Body.GetProperty("saved").GetBoolean());
            Assert.True(result.Body.GetProperty("admin").GetProperty("created").GetBoolean());
            var state = result.Body.GetProperty("report").GetProperty("state");
            Assert.True(state.ValueKind == JsonValueKind.Number ? state.GetInt32() == (int)DatabaseState.Ready : state.GetString() == nameof(DatabaseState.Ready));

            // the settings file names this database, and a SQL login's password is not in it as text
            var text = File.ReadAllText(h.State.ConfigFile);
            Assert.Contains(name, text);
            if (!server.IntegratedSecurity) Assert.DoesNotContain(server.Password, text);

            // read back the way the application reads it at start-up, it reaches the database and finds it complete
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey });
            DatabaseConfigFile.Add(configuration, configuration, Path.GetDirectoryName(h.State.ConfigFile)!);
            Assert.Null(await SetupMode.CheckAsync(configuration, NullLogger.Instance, CancellationToken.None, patience: TimeSpan.Zero));

            var options = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProvider.Configure(options, configuration);
            await using var db = new AppDbContext(options.Options);
            var admin = await db.Users.Include(u => u.Role).SingleAsync();
            Assert.Equal("boss@company.th", admin.Email);
            Assert.Equal(RoleNames.SuperAdmin, admin.Role.Name);
            Assert.True(BCrypt.Net.BCrypt.Verify(AdminPassword, admin.PasswordHash));

            // and the supervisor is told to carry on, a moment after the answer was sent
            Assert.Same(h.State.Completed, await Task.WhenAny(h.State.Completed, Task.Delay(TimeSpan.FromSeconds(10))));
        });
    }

    [Fact]
    public async Task A_refused_administrator_saves_nothing_so_the_person_can_correct_the_form()
    {
        if (Server is null) return;
        await WithServerAsync(createDatabase: true, async (name, server) =>
        {
            await using var h = await SetupHarness.StartAsync();

            var badEmail = await h.PostAsync("/api/setup/apply", new
            {
                connection = Form(name, server),
                repair = new { applyMigrations = true, createMissingObjects = true, seed = true },
                admin = new { email = "not-an-address", firstName = "Boss", lastName = "", password = AdminPassword },
            });
            var weakPassword = await h.PostAsync("/api/setup/apply", new
            {
                connection = Form(name, server),
                repair = new { applyMigrations = true, createMissingObjects = true, seed = true },
                admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = "short" },
            });

            Assert.Equal(HttpStatusCode.BadRequest, badEmail.Status);
            Assert.Equal("admin", badEmail.Body.GetProperty("error").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, weakPassword.Status);
            Assert.False(File.Exists(h.State.ConfigFile));
            Assert.False(h.State.Completed.IsCompleted);
        });
    }

    [Fact]
    public async Task When_an_administrator_already_exists_the_form_one_is_not_added()
    {
        if (Server is null) return;
        await WithServerAsync(createDatabase: true, async (name, server) =>
        {
            await using var h = await SetupHarness.StartAsync();
            var repair = new { applyMigrations = true, createMissingObjects = true, seed = true };
            await h.PostAsync("/api/setup/apply", new { connection = Form(name, server), repair, admin = new { email = "first@company.th", firstName = "First", lastName = "", password = AdminPassword } });

            var again = await h.PostAsync("/api/setup/apply", new { connection = Form(name, server), repair, admin = new { email = "second@company.th", firstName = "Second", lastName = "", password = AdminPassword } });

            Assert.Equal(HttpStatusCode.OK, again.Status);
            Assert.Equal(JsonValueKind.Null, again.Body.GetProperty("admin").ValueKind);
            var options = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProvider.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer",
                ["ConnectionStrings:DefaultConnection"] = new SqlConnectionStringBuilder(Server!) { InitialCatalog = name }.ConnectionString,
            }).Build());
            await using var db = new AppDbContext(options.Options);
            Assert.Equal(["first@company.th"], await db.Users.Select(u => u.Email).ToListAsync());
        });
    }
}

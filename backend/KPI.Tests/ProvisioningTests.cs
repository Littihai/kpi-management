using System.Net;
using System.Text.Json;
using KPI.API.Startup.Setup;
using KPI.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace KPI.Tests;

public class ProvisioningNamesTests
{
    [Theory]
    [InlineData("PC-01", "helpdesk_pc_01")]
    [InlineData("SRV01", "helpdesk_srv01")]
    [InlineData("  weird name!  ", "helpdesk_weird_name")]
    [InlineData("", "helpdesk_app")]
    public void Every_machine_gets_a_login_name_of_its_own_made_from_its_name(string machine, string expected) =>
        Assert.Equal(expected, SqlServerProvisioner.DefaultLoginName(machine));

    [Fact]
    public void A_very_long_machine_name_is_cut_to_fit()
    {
        var login = SqlServerProvisioner.DefaultLoginName(new string('a', 300));

        Assert.True(login.Length <= 100);
        Assert.StartsWith("helpdesk_aaaa", login);
    }

    [Theory]
    [InlineData("helpdesk", true)]
    [InlineData("help-desk_2.test", true)]
    [InlineData("", false)]
    [InlineData("bad name", false)]
    [InlineData("x];DROP DATABASE y;--", false)]
    [InlineData("ฐานข้อมูล", false)]
    public void Only_plain_database_names_are_accepted_because_the_name_is_written_into_CREATE_DATABASE(string name, bool valid) =>
        Assert.Equal(valid, SqlServerProvisioner.IsValidDatabaseName(name));

    [Theory]
    [InlineData("master")]
    [InlineData("MASTER")]
    [InlineData("model")]
    [InlineData("msdb")]
    [InlineData("TempDB")]
    [InlineData("distribution")]
    public void SQL_Servers_own_databases_are_never_accepted_as_the_applications_database(string name) =>
        Assert.False(SqlServerProvisioner.IsValidDatabaseName(name));

    [Theory]
    [InlineData("helpdesk_pc01", true)]
    [InlineData("HELPDESK_PC01", true)]
    [InlineData("helpdesk_", false)]       // nothing after the prefix
    [InlineData("sa", false)]
    [InlineData("kpi_app", false)]         // not ours: its password must never be reset by this program
    [InlineData("helpdesk-pc01", false)]
    [InlineData("helpdesk_pc 01", false)]
    [InlineData("helpdesk_x]; DROP LOGIN sa;--", false)]
    public void Only_logins_that_start_with_helpdesk_and_are_plain_names_are_made_or_changed(string login, bool valid) =>
        Assert.Equal(valid, SqlServerProvisioner.IsValidLoginName(login));

    [Fact]
    public void The_default_login_name_is_always_one_this_program_accepts()
    {
        foreach (var machine in new[] { "PC-01", "SRV01", "x", "ชื่อเครื่อง", new string('a', 300), "" })
            Assert.True(SqlServerProvisioner.IsValidLoginName(SqlServerProvisioner.DefaultLoginName(machine)), machine);
    }

    [Fact]
    public void Generated_passwords_are_long_random_and_meet_the_usual_complexity_rule()
    {
        var a = SqlServerProvisioner.NewPassword();
        var b = SqlServerProvisioner.NewPassword();

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 30);
        Assert.Matches("^[A-Za-z0-9]+$", a);   // safe to write into a statement
        Assert.Matches("[A-Z]", a);
        Assert.Matches("[a-z]", a);
        Assert.Matches("[0-9]", a);
    }

    [Fact]
    public async Task A_server_that_is_not_there_is_reported_not_thrown()
    {
        var probe = await SqlServerProvisioner.ProbeAsync(new ServerAccess("127.0.0.1,1", true, null, null), "helpdesk");

        Assert.False(probe.Connected);
        Assert.Equal("unreachable", probe.Problem);
        Assert.False(probe.CanCreateDatabase);
    }

    [Fact]
    public async Task A_name_that_is_not_a_plain_name_is_refused_before_anything_is_sent()
    {
        var access = new ServerAccess("127.0.0.1,1", true, null, null);

        var database = await Assert.ThrowsAsync<ProvisioningException>(() => SqlServerProvisioner.EnsureDatabaseAsync(access, "x];--"));
        var login = await Assert.ThrowsAsync<ProvisioningException>(() => SqlServerProvisioner.EnsureAppLoginAsync(access, "helpdesk", "x]; DROP LOGIN sa;--"));

        Assert.Equal("invalid", database.Code);
        Assert.Equal("invalid", login.Code);
    }
}

public class SetupKeyThrottleTests
{
    [Fact]
    public void A_burst_of_parallel_guesses_gets_no_more_tries_than_the_limit()
    {
        var key = new SetupKey("correct-horse-battery", null, new MovableClock());
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<SetupKey.Outcome>();

        Parallel.For(0, 400, i => outcomes.Add(key.Authorize("guess-" + i, out _)));

        Assert.Equal(SetupKey.MaxFailures, outcomes.Count(o => o == SetupKey.Outcome.Wrong));   // not 10 per burst: 10 in all
        Assert.Equal(400 - SetupKey.MaxFailures, outcomes.Count(o => o == SetupKey.Outcome.Blocked));
    }

    [Fact]
    public void While_blocked_even_the_right_key_is_refused_and_afterwards_it_is_accepted()
    {
        var clock = new MovableClock();
        var key = new SetupKey("correct-horse-battery", null, clock);
        for (var i = 0; i < SetupKey.MaxFailures; i++) key.Authorize("wrong", out _);

        Assert.Equal(SetupKey.Outcome.Blocked, key.Authorize("correct-horse-battery", out var wait));
        Assert.InRange(wait, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(SetupKey.Outcome.Accepted, key.Authorize("correct-horse-battery", out _));
    }

    [Fact]
    public void An_accepted_key_clears_the_count_of_wrong_ones()
    {
        var key = new SetupKey("correct-horse-battery", null, new MovableClock());
        for (var i = 0; i < SetupKey.MaxFailures - 1; i++) key.Authorize("wrong", out _);
        key.Authorize("correct-horse-battery", out _);

        for (var i = 0; i < SetupKey.MaxFailures - 1; i++) Assert.Equal(SetupKey.Outcome.Wrong, key.Authorize("wrong", out _));
        Assert.Equal(SetupKey.Outcome.Accepted, key.Authorize("correct-horse-battery", out _));
    }
}

public class SetupWatcherTests
{
    private static SetupState State() => new()
    {
        Need = new SetupNeed(SetupReason.Unreachable, "unreachable"),
        Configuration = new ConfigurationBuilder().Build(),
        ConfigFile = "database.json",
    };

    [Fact]
    public async Task A_refused_login_is_not_tried_again_because_every_try_counts_against_the_login()
    {
        var calls = 0;
        var state = State();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var watcher = SetupHost.WatchAsync(state.Configuration, state, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, stop.Token,
            (_, _) => { calls++; return Task.FromResult((false, (string?)"login-failed", (string?)"refused")); }, TimeSpan.FromMilliseconds(20));
        await Task.WhenAny(watcher, Task.Delay(1000));

        Assert.Equal(1, calls);                       // one attempt, then it waits for the person
        Assert.False(state.Completed.IsCompleted);
    }

    [Fact]
    public async Task A_server_that_is_only_down_is_tried_again_and_the_application_starts_when_it_answers()
    {
        var calls = 0;
        var state = State();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await SetupHost.WatchAsync(state.Configuration, state, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, stop.Token,
            (_, _) => { calls++; return Task.FromResult(calls < 3 ? (false, (string?)"unreachable", (string?)"down") : (true, (string?)null, (string?)null)); }, TimeSpan.FromMilliseconds(20));

        Assert.Equal(3, calls);
        Assert.True(state.Completed.IsCompleted);
    }
}

public class SetupKeyOriginTests
{
    [Fact]
    public void A_key_from_the_configuration_is_marked_so_it_is_never_written_to_a_log()
    {
        using var folder = new TempFolder();
        var configured = SetupKey.Resolve(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Setup:Key"] = "from-the-config-1" }).Build(), folder.File("database.json"));
        var generated = SetupKey.Resolve(new ConfigurationBuilder().Build(), folder.File("database.json"));

        Assert.True(configured.FromConfiguration);
        Assert.False(generated.FromConfiguration);
    }
}

public class SetupJwtKeyTests
{
    private static object Sql() => new { connection = new { server = "127.0.0.1,1", database = "helpdesk", auth = "sql", user = "sa", password = "p" } };

    [Theory]
    [InlineData("short")]
    [InlineData("your-super-secret-key-minimum-32-characters-long")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_server_key_the_application_would_refuse_is_not_used_to_encrypt_a_password(string key)
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: false, more: new() { ["Jwt:Key"] = key });

        var status = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;
        var apply = await h.PostAsync("/api/setup/apply", Sql(), key: null);

        Assert.False(status.GetProperty("jwtConfigured").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, apply.Status);
        Assert.Equal("jwt-missing", apply.Body.GetProperty("error").GetString());
        Assert.False(File.Exists(h.State.ConfigFile));
    }
}

public class SetupGuardTests
{
    private static object Bad() => new { connection = new { server = "", database = "x", auth = "windows" } };
    private static Dictionary<string, string> H(string name, string value) => new() { [name] = value };

    [Fact]
    public async Task A_request_addressed_to_another_name_is_refused_even_from_the_loopback_address_dns_rebinding()
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: false);

        var rebound = await h.PostAsync("/api/setup/apply", Bad(), key: null, headers: H("Host", "attacker.example:8080"));
        var byAddress = await h.PostAsync("/api/setup/test", Bad(), key: null, headers: H("Host", "10.0.0.5:8080"));
        var local = await h.PostAsync("/api/setup/test", Bad(), key: null, headers: H("Host", "localhost:8080"));
        var ip = await h.PostAsync("/api/setup/test", Bad(), key: null, headers: H("Host", "127.0.0.1:8080"));

        Assert.Equal(HttpStatusCode.Forbidden, rebound.Status);
        Assert.Equal("local-only", rebound.Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, byAddress.Status);
        Assert.Equal(HttpStatusCode.BadRequest, local.Status);   // past the guard
        Assert.Equal(HttpStatusCode.BadRequest, ip.Status);
    }

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("X-Forwarded-Host", "helpdesk.company.com")]
    [InlineData("X-Forwarded-Proto", "https")]
    [InlineData("Forwarded", "for=203.0.113.9")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public async Task A_request_passed_on_by_a_reverse_proxy_does_not_count_as_the_server_itself(string header, string value)
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: false);

        var response = await h.PostAsync("/api/setup/apply", Bad(), key: null, headers: H(header, value));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("local-only", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_proxied_request_is_accepted_when_remote_setup_was_allowed_and_the_key_is_right()
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: false, allowRemote: true);

        var without = await h.PostAsync("/api/setup/test", Bad(), key: null, headers: H("X-Forwarded-For", "203.0.113.9"));
        var with = await h.PostAsync("/api/setup/test", Bad(), key: h.State.Key!.Reveal(), headers: H("X-Forwarded-For", "203.0.113.9"));

        Assert.Equal(HttpStatusCode.Unauthorized, without.Status);
        Assert.Equal(HttpStatusCode.BadRequest, with.Status);
    }

    [Fact]
    public async Task A_request_from_another_web_site_is_refused_whatever_the_key()
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: true);

        var foreign = await h.PostAsync("/api/setup/test", Bad(), headers: H("Origin", "https://evil.example"));
        var nullOrigin = await h.PostAsync("/api/setup/test", Bad(), headers: H("Origin", "null"));
        var samePage = await h.PostAsync("/api/setup/test", Bad(), headers: H("Origin", "http://localhost"));

        Assert.Equal(HttpStatusCode.Forbidden, foreign.Status);
        Assert.Equal("cross-origin", foreign.Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, nullOrigin.Status);
        Assert.Equal(HttpStatusCode.BadRequest, samePage.Status);   // the page's own origin is fine
    }

    [Fact]
    public async Task The_status_tells_where_things_are_kept_only_to_the_servers_own_page_not_to_another_origin_that_may_read_it()
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: false, need: new SetupNeed(SetupReason.Unreachable, "login-failed", "bad"), more: new()
        {
            ["Database:Server"] = "srv", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "sql", ["Database:User"] = "app",
        });

        var own = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;
        using var crossRequest = new HttpRequestMessage(HttpMethod.Get, "/api/setup/status");
        crossRequest.Headers.Add("Origin", "https://evil.example");
        using var cross = await h.Client.SendAsync(crossRequest);
        var foreign = JsonDocument.Parse(await cross.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("srv", own.GetProperty("current").GetProperty("server").GetString());
        Assert.False(string.IsNullOrEmpty(own.GetProperty("machineLogin").GetString()));
        Assert.Equal("*", Assert.Single(cross.Headers.GetValues("Access-Control-Allow-Origin")));   // readable from anywhere ...
        Assert.Equal("Unreachable", foreign.GetProperty("reason").GetString());                      // ... so it says only that setup is waiting
        Assert.Equal(JsonValueKind.Null, foreign.GetProperty("current").ValueKind);
        Assert.Equal(JsonValueKind.Null, foreign.GetProperty("keyFile").ValueKind);
        Assert.Equal(JsonValueKind.Null, foreign.GetProperty("machineLogin").ValueKind);
    }

    [Fact]
    public async Task Remote_setup_being_allowed_does_not_hand_the_details_to_anyone_who_asks()
    {
        await using var h = await SetupHarness.StartAsync(local: false, allowRemote: true, keyConfigured: false, more: new()
        {
            ["Database:Server"] = "srv", ["Database:Name"] = "helpdesk", ["Database:Auth"] = "sql", ["Database:User"] = "app",
        });

        var status = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;

        Assert.Equal(JsonValueKind.Null, status.GetProperty("current").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("keyFile").ValueKind);
        Assert.True(status.GetProperty("keyRequired").GetBoolean());
    }
}

public class SetupKeyRulesTests
{
    private static object Bad() => new { connection = new { server = "", database = "x", auth = "windows" } };

    [Fact]
    public async Task On_the_server_itself_no_key_is_needed_unless_one_was_set_on_purpose()
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: false);

        var status = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;
        var response = await h.PostAsync("/api/setup/test", Bad(), key: null);

        Assert.False(status.GetProperty("keyRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);   // past the guard: it is the form that is wrong
        Assert.Empty(Directory.GetFiles(h.Folder.Path));            // no key file was made
    }

    [Fact]
    public async Task A_key_set_in_the_configuration_is_asked_for_on_the_server_too()
    {
        await using var h = await SetupHarness.StartAsync(keyConfigured: true);

        var status = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;
        var without = await h.PostAsync("/api/setup/test", Bad(), key: null);

        Assert.True(status.GetProperty("keyRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, without.Status);
    }

    [Fact]
    public async Task From_another_machine_a_key_is_always_needed_and_one_is_made_for_it()
    {
        await using var h = await SetupHarness.StartAsync(local: false, allowRemote: true, keyConfigured: false);

        var status = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;
        var without = await h.PostAsync("/api/setup/test", Bad(), key: null);
        var withKey = await h.PostAsync("/api/setup/test", Bad(), key: h.State.Key!.Reveal());

        Assert.True(status.GetProperty("keyRequired").GetBoolean());
        Assert.True(File.Exists(Path.Combine(h.Folder.Path, "setup-key.txt")));
        Assert.Equal(HttpStatusCode.Unauthorized, without.Status);
        Assert.Equal(HttpStatusCode.BadRequest, withKey.Status);
    }

    [Fact]
    public async Task Allowing_other_machines_does_not_make_the_server_itself_ask_for_the_key()
    {
        await using var h = await SetupHarness.StartAsync(local: true, allowRemote: true, keyConfigured: false);

        var status = JsonDocument.Parse(await h.Client.GetStringAsync("/api/setup/status")).RootElement;
        var response = await h.PostAsync("/api/setup/test", Bad(), key: null);

        Assert.False(status.GetProperty("keyRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }
}

/// <summary>Against a real SQL Server (TEST_SQLSERVER, with an account that may create databases and logins): the whole "connect as sa, create once, other machines reuse" cycle.</summary>
public class ProvisioningLiveTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("TEST_SQLSERVER");
    private const string JwtKey = SetupHarness.JwtKey;
    private const string AdminPassword = "Wizard-Admin-Pass-1";

    private static string Master() => new SqlConnectionStringBuilder(Server!) { InitialCatalog = "master" }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private sealed record Sa(string User, string Password);

    /// <summary>What the person types: a SQL login that may do everything (like sa), made here so the test does not depend on the real one.</summary>
    private static async Task<Sa> MakeSaAsync(bool sysadmin = true, bool dbCreator = false)
    {
        var sa = new Sa("kpi_t_" + Guid.NewGuid().ToString("N")[..10], SqlServerProvisioner.NewPassword());
        await ExecuteAsync(Master(), $"CREATE LOGIN [{sa.User}] WITH PASSWORD = N'{sa.Password}', CHECK_POLICY = OFF");
        if (sysadmin) await ExecuteAsync(Master(), $"ALTER SERVER ROLE sysadmin ADD MEMBER [{sa.User}]");
        if (dbCreator) await ExecuteAsync(Master(), $"ALTER SERVER ROLE dbcreator ADD MEMBER [{sa.User}]");
        return sa;
    }

    private static ServerAccess Access(Sa sa) => new(new SqlConnectionStringBuilder(Server!).DataSource, false, sa.User, sa.Password, true);

    private static object Form(string database, Sa sa) => new
    {
        server = new SqlConnectionStringBuilder(Server!).DataSource,
        database,
        auth = "sql",
        user = sa.User,
        password = sa.Password,
        trust = true,
    };

    private static async Task CleanAsync(string? database, params string[] logins)
    {
        SqlConnection.ClearAllPools();
        if (database is not null)
            await ExecuteAsync(Master(), $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}] END");
        foreach (var login in logins)
            await ExecuteAsync(Master(), $"IF SUSER_ID('{login}') IS NOT NULL DROP LOGIN [{login}]");
    }

    private static string NewName() => "kpi_prov_" + Guid.NewGuid().ToString("N")[..12];

    private static string ConnectAs(string user, string password, string database) =>
        new SqlConnectionStringBuilder(Server!) { InitialCatalog = database, IntegratedSecurity = false, UserID = user, Password = password }.ConnectionString;

    // ───────────── the provisioner on its own ─────────────

    [Fact]
    public async Task The_probe_says_who_can_do_what_and_whether_the_database_is_there()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        try
        {
            var before = await SqlServerProvisioner.ProbeAsync(Access(sa), name);
            await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);
            var after = await SqlServerProvisioner.ProbeAsync(Access(sa), name);
            var wrong = await SqlServerProvisioner.ProbeAsync(Access(sa) with { Password = "not-the-password" }, name);

            Assert.True(before.Connected);
            Assert.False(string.IsNullOrEmpty(before.Version));
            Assert.True(before.CanCreateDatabase);
            Assert.True(before.CanCreateLogin);
            Assert.False(before.DatabaseExists);
            Assert.True(after.DatabaseExists);
            Assert.True(after.CanOpenDatabase);
            Assert.False(wrong.Connected);
            Assert.Equal("login-failed", wrong.Problem);
        }
        finally { await CleanAsync(name, sa.User); }
    }

    [Fact]
    public async Task The_database_is_created_once_with_the_case_insensitive_collation_and_never_again()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        try
        {
            var first = await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);
            var second = await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);

            Assert.True(first);
            Assert.False(second);
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(Master(), $"SELECT CONVERT(int, DATABASEPROPERTYEX('{name}', 'IsAutoClose'))")));
            Assert.Equal(SqlServerProvisioner.Collation, (string?)await ScalarAsync(Master(), $"SELECT CONVERT(varchar(128), DATABASEPROPERTYEX('{name}', 'Collation'))"));
        }
        finally { await CleanAsync(name, sa.User); }
    }

    [Fact]
    public async Task Machines_that_start_at_the_same_moment_create_the_database_only_once()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name)));

            Assert.Equal(1, results.Count(created => created));
        }
        finally { await CleanAsync(name, sa.User); }
    }

    [Fact]
    public async Task An_account_that_may_not_create_databases_is_told_so()
    {
        if (Server is null) return;
        var weak = await MakeSaAsync(sysadmin: false);
        var name = NewName();
        try
        {
            var probe = await SqlServerProvisioner.ProbeAsync(Access(weak), name);
            var refused = await Assert.ThrowsAsync<ProvisioningException>(() => SqlServerProvisioner.EnsureDatabaseAsync(Access(weak), name));

            Assert.True(probe.Connected);
            Assert.False(probe.CanCreateDatabase);
            Assert.False(probe.CanCreateLogin);
            Assert.Equal("no-permission", refused.Code);
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(Master(), $"SELECT COUNT(*) FROM sys.databases WHERE name = '{name}'")));
        }
        finally { await CleanAsync(name, weak.User); }
    }

    [Fact]
    public async Task The_application_login_owns_this_database_only_and_a_second_request_gives_it_a_new_password()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        var loginName = "helpdesk_t_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);

            var first = await SqlServerProvisioner.EnsureAppLoginAsync(Access(sa), name, loginName);
            Assert.True(first.Created);
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync(ConnectAs(first.Login, first.Password, name), "SELECT IS_ROLEMEMBER('db_owner')")));
            await ExecuteAsync(ConnectAs(first.Login, first.Password, name), "CREATE TABLE T1 (Id int)");   // may create tables here
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(ConnectAs(first.Login, first.Password, name), "SELECT IS_SRVROLEMEMBER('sysadmin')")));

            var second = await SqlServerProvisioner.EnsureAppLoginAsync(Access(sa), name, loginName);
            SqlConnection.ClearAllPools();
            Assert.False(second.Created);
            Assert.NotEqual(first.Password, second.Password);
            await ExecuteAsync(ConnectAs(second.Login, second.Password, name), "SELECT 1");
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(ConnectAs(first.Login, first.Password, name), "SELECT 1"));
        }
        finally { await CleanAsync(name, sa.User, loginName); }
    }

    [Fact]
    public async Task An_account_that_is_a_sysadmin_is_never_given_a_new_password()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var other = new Sa("helpdesk_admin_" + Guid.NewGuid().ToString("N")[..8], SqlServerProvisioner.NewPassword());   // another administrator that happens to be named like a machine's login
        await ExecuteAsync(Master(), $"CREATE LOGIN [{other.User}] WITH PASSWORD = N'{other.Password}', CHECK_POLICY = OFF; ALTER SERVER ROLE sysadmin ADD MEMBER [{other.User}]");
        var name = NewName();
        try
        {
            await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);

            var refused = await Assert.ThrowsAsync<ProvisioningException>(() => SqlServerProvisioner.EnsureAppLoginAsync(Access(sa), name, other.User));

            Assert.Equal("login-in-use", refused.Code);
            await ExecuteAsync(ConnectAs(other.User, other.Password, "master"), "SELECT 1");   // still works with its own password
        }
        finally { await CleanAsync(name, sa.User, other.User); }
    }

    [Fact]
    public async Task A_login_that_does_not_start_with_helpdesk_is_refused_and_left_alone()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var someoneElses = new Sa("kpi_other_" + Guid.NewGuid().ToString("N")[..8], SqlServerProvisioner.NewPassword());   // another application's login
        await ExecuteAsync(Master(), $"CREATE LOGIN [{someoneElses.User}] WITH PASSWORD = N'{someoneElses.Password}', CHECK_POLICY = OFF");
        var name = NewName();
        try
        {
            await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);

            var refused = await Assert.ThrowsAsync<ProvisioningException>(() => SqlServerProvisioner.EnsureAppLoginAsync(Access(sa), name, someoneElses.User));

            Assert.Equal("invalid", refused.Code);
            await ExecuteAsync(ConnectAs(someoneElses.User, someoneElses.Password, "master"), "SELECT 1");   // its password was not reset
        }
        finally { await CleanAsync(name, sa.User, someoneElses.User); }
    }

    [Fact]
    public async Task A_user_left_in_the_database_by_an_earlier_login_is_attached_to_the_new_one_instead_of_failing()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        var loginName = "helpdesk_t_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);
            await SqlServerProvisioner.EnsureAppLoginAsync(Access(sa), name, loginName);
            // the machine is reinstalled: the login is dropped, the database (and the user in it) stays
            await ExecuteAsync(Master(), $"DROP LOGIN [{loginName}]");

            var again = await SqlServerProvisioner.EnsureAppLoginAsync(Access(sa), name, loginName);

            Assert.True(again.Created);
            SqlConnection.ClearAllPools();
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync(ConnectAs(again.Login, again.Password, name), "SELECT IS_ROLEMEMBER('db_owner')")));
        }
        finally { await CleanAsync(name, sa.User, loginName); }
    }

    [Fact]
    public async Task A_system_database_is_never_touched_even_by_a_sysadmin_who_types_its_name()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);

            var test = await h.PostAsync("/api/setup/test", new { connection = Form("master", sa) }, key: null);
            var apply = await h.PostAsync("/api/setup/apply", new { connection = Form("master", sa), repair = Repair() }, key: null);

            Assert.Equal(HttpStatusCode.BadRequest, test.Status);
            Assert.Equal("invalid", test.Body.GetProperty("error").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, apply.Status);
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(Master(), "SELECT COUNT(*) FROM master.sys.tables WHERE name = 'Users'")));
        }
        finally { await CleanAsync(null, sa.User); }
    }

    [Fact]
    public async Task A_login_that_already_exists_is_only_reset_when_the_person_agrees()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        var login = "helpdesk_t_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await SqlServerProvisioner.EnsureDatabaseAsync(Access(sa), name);
            var existing = await SqlServerProvisioner.EnsureAppLoginAsync(Access(sa), name, login);   // another machine's (or an earlier) login
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);

            var test = await h.PostAsync("/api/setup/test", new { connection = Form(name, sa), appAccess = new { mode = "auto", login } }, key: null);
            var refused = await h.PostAsync("/api/setup/apply", new { connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "auto", login }, admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword } }, key: null);

            Assert.True(test.Body.GetProperty("plan").GetProperty("loginExists").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, refused.Status);
            Assert.Equal("login-exists", refused.Body.GetProperty("error").GetString());
            Assert.False(File.Exists(h.State.ConfigFile));
            await ExecuteAsync(ConnectAs(existing.Login, existing.Password, name), "SELECT 1");   // its password was not touched

            var agreed = await h.PostAsync("/api/setup/apply", new { connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "auto", login, confirmReset = true }, admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword } }, key: null);

            Assert.Equal(HttpStatusCode.OK, agreed.Status);
            Assert.Contains("reset-login", agreed.Body.GetProperty("done").EnumerateArray().Select(x => x.GetString()));
            SqlConnection.ClearAllPools();
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(ConnectAs(existing.Login, existing.Password, name), "SELECT 1"));   // the old password no longer works
        }
        finally { await CleanAsync(name, sa.User, login); }
    }

    [Fact]
    public async Task Two_setups_at_the_same_moment_run_one_after_the_other_not_together()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        var login = "helpdesk_t_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);
            var body = new { connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "auto", login, confirmReset = true }, admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword } };

            var both = await Task.WhenAll(h.PostAsync("/api/setup/apply", body, key: null), h.PostAsync("/api/setup/apply", body, key: null));

            Assert.Contains(HttpStatusCode.OK, both.Select(r => r.Status));
            Assert.Contains(both, r => r.Status == HttpStatusCode.Conflict && r.Body.GetProperty("error").GetString() == "busy");
            // the saved password is the one the login has: the application can use it
            var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey });
            DatabaseConfigFile.Add(config, config, Path.GetDirectoryName(h.State.ConfigFile)!);
            await ExecuteAsync(DatabaseProvider.ConnectionString(config)!, "SELECT COUNT(*) FROM Users");
        }
        finally { await CleanAsync(name, sa.User, login); }
    }

    [Fact]
    public async Task An_administrator_e_mail_that_belongs_to_someone_else_stops_the_save_instead_of_leaving_an_install_nobody_can_sign_in_to()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        try
        {
            await using var first = await SetupHarness.StartAsync(keyConfigured: false);
            Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("/api/setup/apply", new { connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "same" }, admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword } }, key: null)).Status);
            // the only administrator is switched off: the database now has a user with that e-mail and nobody who is an administrator
            await ExecuteAsync(new SqlConnectionStringBuilder(Server!) { InitialCatalog = name }.ConnectionString, "UPDATE Users SET IsActive = 0");

            await using var second = await SetupHarness.StartAsync(keyConfigured: false);
            var apply = await second.PostAsync("/api/setup/apply", new { connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "same" }, admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword } }, key: null);

            Assert.Equal(HttpStatusCode.BadRequest, apply.Status);
            Assert.Equal("admin", apply.Body.GetProperty("error").GetString());
            Assert.False(File.Exists(second.State.ConfigFile));          // nothing was saved
            Assert.False(second.State.Completed.IsCompleted);
        }
        finally { await CleanAsync(name, sa.User); }
    }

    [Fact]
    public async Task A_bad_login_name_stops_the_plan_before_the_database_is_created()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);

            var test = await h.PostAsync("/api/setup/test", new { connection = Form(name, sa), appAccess = new { mode = "auto", login = "evil-login" } }, key: null);
            var apply = await h.PostAsync("/api/setup/apply", new { connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "auto", login = "evil-login" } }, key: null);

            Assert.Equal("invalid-login", test.Body.GetProperty("plan").GetProperty("blocker").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, apply.Status);
            Assert.Equal("invalid-login", apply.Body.GetProperty("error").GetString());
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(Master(), $"SELECT COUNT(*) FROM sys.databases WHERE name = '{name}'")));   // nothing was created
        }
        finally { await CleanAsync(name, sa.User); }
    }

    // ───────────── the setup endpoints, as sa ─────────────

    private static object Repair() => new { applyMigrations = true, createMissingObjects = true, seed = true };

    [Fact]
    public async Task The_first_machine_creates_everything_and_keeps_only_its_own_account_not_sa()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        var machine = "helpdesk_first_" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);

            var test = await h.PostAsync("/api/setup/test", new { connection = Form(name, sa), appAccess = new { mode = "auto", login = machine } }, key: null);
            Assert.Equal(HttpStatusCode.OK, test.Status);
            Assert.False(test.Body.GetProperty("server").GetProperty("databaseExists").GetBoolean());
            Assert.True(test.Body.GetProperty("plan").GetProperty("createDatabase").GetBoolean());
            Assert.Equal("dedicated", test.Body.GetProperty("plan").GetProperty("access").GetString());
            Assert.Equal(JsonValueKind.Null, test.Body.GetProperty("plan").GetProperty("blocker").ValueKind);

            var apply = await h.PostAsync("/api/setup/apply", new
            {
                connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "auto", login = machine },
                admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword },
            }, key: null);

            Assert.Equal(HttpStatusCode.OK, apply.Status);
            Assert.True(apply.Body.GetProperty("createdDatabase").GetBoolean());
            Assert.Equal("dedicated", apply.Body.GetProperty("access").GetString());
            Assert.Equal(machine, apply.Body.GetProperty("login").GetString());
            Assert.True(apply.Body.GetProperty("admin").GetProperty("created").GetBoolean());
            var written = File.ReadAllText(h.State.ConfigFile);
            Assert.Contains(machine, written);
            Assert.DoesNotContain(sa.User, written);       // sa is used once and not kept
            Assert.DoesNotContain(sa.Password, written);
            var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey });
            DatabaseConfigFile.Add(config, config, Path.GetDirectoryName(h.State.ConfigFile)!);
            Assert.Equal(machine, new SqlConnectionStringBuilder(DatabaseProvider.ConnectionString(config)).UserID);
            await ExecuteAsync(DatabaseProvider.ConnectionString(config)!, "SELECT COUNT(*) FROM Users");   // the saved account works
            Assert.Same(h.State.Completed, await Task.WhenAny(h.State.Completed, Task.Delay(TimeSpan.FromSeconds(10))));
        }
        finally { await CleanAsync(name, sa.User, machine); }
    }

    [Fact]
    public async Task A_second_machine_finds_the_database_and_does_not_create_or_rebuild_it()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        var first = "helpdesk_pc01_" + Guid.NewGuid().ToString("N")[..6];
        var second = "helpdesk_pc02_" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            await using (var one = await SetupHarness.StartAsync(keyConfigured: false))
            {
                var applied = await one.PostAsync("/api/setup/apply", new
                {
                    connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "auto", login = first },
                    admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword },
                }, key: null);
                Assert.Equal(HttpStatusCode.OK, applied.Status);
            }

            await using var two = await SetupHarness.StartAsync(keyConfigured: false);
            var test = await two.PostAsync("/api/setup/test", new { connection = Form(name, sa), appAccess = new { mode = "auto", login = second } }, key: null);
            Assert.True(test.Body.GetProperty("server").GetProperty("databaseExists").GetBoolean());
            Assert.False(test.Body.GetProperty("plan").GetProperty("createDatabase").GetBoolean());
            var state = test.Body.GetProperty("report").GetProperty("state");
            Assert.True(state.ValueKind == JsonValueKind.Number ? state.GetInt32() == 0 : state.GetString() == "Ready", "the database is complete");

            var apply = await two.PostAsync("/api/setup/apply", new
            {
                connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "auto", login = second },
                admin = new { email = "another@company.th", firstName = "Another", lastName = "", password = AdminPassword },
            }, key: null);

            Assert.Equal(HttpStatusCode.OK, apply.Status);
            Assert.False(apply.Body.GetProperty("createdDatabase").GetBoolean());
            var steps = apply.Body.GetProperty("done").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Equal(["create-login"], steps);                                          // its own account, and nothing else
            Assert.Equal(JsonValueKind.Null, apply.Body.GetProperty("admin").ValueKind);   // an administrator exists: not added again
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync(Master(), $"SELECT COUNT(*) FROM sys.databases WHERE name = '{name}'")));
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync(new SqlConnectionStringBuilder(Server!) { InitialCatalog = name }.ConnectionString, "SELECT COUNT(*) FROM Users")));
            // both machines have their own working account on the same database
            await ExecuteAsync(ConnectAs(second, ReadPassword(two.State.ConfigFile), name), "SELECT COUNT(*) FROM Users");
        }
        finally { await CleanAsync(name, sa.User, first, second); }
    }

    /// <summary>The application account's password as the second machine saved it (encrypted with its key; decrypted here the way the application does).</summary>
    private static string ReadPassword(string configFile)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey });
        DatabaseConfigFile.Add(config, config, Path.GetDirectoryName(configFile)!);
        return new SqlConnectionStringBuilder(DatabaseProvider.ConnectionString(config)).Password;
    }

    [Fact]
    public async Task Using_the_typed_in_account_for_the_application_too_keeps_it_encrypted_and_makes_no_login()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);

            var apply = await h.PostAsync("/api/setup/apply", new
            {
                connection = Form(name, sa), repair = Repair(), appAccess = new { mode = "same" },
                admin = new { email = "boss@company.th", firstName = "Boss", lastName = "", password = AdminPassword },
            }, key: null);

            Assert.Equal(HttpStatusCode.OK, apply.Status);
            Assert.Equal("same", apply.Body.GetProperty("access").GetString());
            var steps = apply.Body.GetProperty("done").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Contains("create-database", steps);
            Assert.DoesNotContain("create-login", steps);
            var written = File.ReadAllText(h.State.ConfigFile);
            Assert.Contains(sa.User, written);
            Assert.DoesNotContain(sa.Password, written);   // encrypted, not plain
            Assert.Equal(sa.Password, ReadPassword(h.State.ConfigFile));
        }
        finally { await CleanAsync(name, sa.User); }
    }

    [Fact]
    public async Task An_account_without_the_rights_is_stopped_before_anything_is_created()
    {
        if (Server is null) return;
        var weak = await MakeSaAsync(sysadmin: false);
        var name = NewName();
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);

            var test = await h.PostAsync("/api/setup/test", new { connection = Form(name, weak) }, key: null);
            var apply = await h.PostAsync("/api/setup/apply", new { connection = Form(name, weak), repair = Repair() }, key: null);

            Assert.True(test.Body.GetProperty("server").GetProperty("connected").GetBoolean());
            Assert.Equal("no-permission-database", test.Body.GetProperty("plan").GetProperty("blocker").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, apply.Status);
            Assert.Equal("no-permission-database", apply.Body.GetProperty("error").GetString());
            Assert.False(string.IsNullOrWhiteSpace(apply.Body.GetProperty("message").GetString()));
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(Master(), $"SELECT COUNT(*) FROM sys.databases WHERE name = '{name}'")));
            Assert.False(File.Exists(h.State.ConfigFile));
        }
        finally { await CleanAsync(name, weak.User); }
    }

    [Fact]
    public async Task An_account_that_may_create_databases_but_not_logins_is_offered_the_same_account_instead()
    {
        if (Server is null) return;
        var creator = await MakeSaAsync(sysadmin: false, dbCreator: true);
        var name = NewName();
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);

            var auto = await h.PostAsync("/api/setup/test", new { connection = Form(name, creator) }, key: null);
            var forced = await h.PostAsync("/api/setup/test", new { connection = Form(name, creator), appAccess = new { mode = "dedicated" } }, key: null);

            Assert.Equal("same", auto.Body.GetProperty("plan").GetProperty("access").GetString());   // "auto" falls back
            Assert.Equal(JsonValueKind.Null, auto.Body.GetProperty("plan").GetProperty("blocker").ValueKind);
            Assert.Equal("no-permission-login", forced.Body.GetProperty("plan").GetProperty("blocker").GetString());
        }
        finally { await CleanAsync(name, creator.User); }
    }

    [Fact]
    public async Task A_wrong_password_says_the_login_failed_and_nothing_is_created()
    {
        if (Server is null) return;
        var sa = await MakeSaAsync();
        var name = NewName();
        try
        {
            await using var h = await SetupHarness.StartAsync(keyConfigured: false);
            var wrong = sa with { Password = "not-the-password" };

            var test = await h.PostAsync("/api/setup/test", new { connection = Form(name, wrong) }, key: null);
            var apply = await h.PostAsync("/api/setup/apply", new { connection = Form(name, wrong), repair = Repair() }, key: null);

            Assert.False(test.Body.GetProperty("server").GetProperty("connected").GetBoolean());
            Assert.Equal("login-failed", test.Body.GetProperty("server").GetProperty("problem").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, apply.Status);
            Assert.Equal("not-connected", apply.Body.GetProperty("error").GetString());
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync(Master(), $"SELECT COUNT(*) FROM sys.databases WHERE name = '{name}'")));
        }
        finally { await CleanAsync(name, sa.User); }
    }
}

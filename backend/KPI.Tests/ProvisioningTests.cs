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
        var loginName = "kpi_app_" + Guid.NewGuid().ToString("N")[..8];
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
        var other = await MakeSaAsync();   // another administrator that happens to be named like a machine's login
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

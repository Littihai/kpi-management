using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Database;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace KPI.Tests;

public class DatabaseDoctorBasicTests
{
    [Fact]
    public async Task An_in_memory_database_is_reported_as_not_checkable_instead_of_failing()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var report = await DatabaseDoctor.InspectAsync(db);

        Assert.Equal("InMemory", report.Engine);
        Assert.False(report.Supported);
        Assert.Equal(DatabaseState.Blocked, report.State);
        var result = await DatabaseDoctor.RepairAsync(db, new RepairRequest());
        Assert.Empty(result.Done);
    }

    [Fact]
    public void Connection_failures_are_explained_in_terms_an_administrator_can_act_on()
    {
        Assert.Equal("login-failed", DatabaseDoctor.ClassifyConnectionFailure(new InvalidOperationException("password authentication failed for user x")).Problem);
        Assert.Equal("login-failed", DatabaseDoctor.ClassifyConnectionFailure(new Exception("outer", new Exception("Login failed for user 'sa'."))).Problem);
        Assert.Equal("unreachable", DatabaseDoctor.ClassifyConnectionFailure(new Exception("A network-related or instance-specific error occurred")).Problem);
        Assert.Equal("unreachable", DatabaseDoctor.ClassifyConnectionFailure(new Exception("Connection refused")).Problem);
        Assert.Equal("no-database", DatabaseDoctor.ClassifyConnectionFailure(new Exception("Cannot open database \"helpdesk\" requested by the login.")).Problem);
        var other = DatabaseDoctor.ClassifyConnectionFailure(new Exception("something unusual"));
        Assert.Equal(("other", "something unusual"), other);
    }
}

/// <summary>Against a real SQL Server (TEST_SQLSERVER, see SqlServerLiveTests); without it these do nothing.</summary>
public class DatabaseDoctorLiveTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("TEST_SQLSERVER");

    private static AppDbContext Context(string connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:DefaultConnection"] = connection,
        }).Build());
        return new AppDbContext(options.Options);
    }

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

    private static async Task WithDatabaseAsync(Func<string, Task> test, string collation = "Thai_CI_AS")
    {
        var name = "kpi_doctor_" + Guid.NewGuid().ToString("N")[..16];
        var master = new SqlConnectionStringBuilder(Server!) { InitialCatalog = "master" }.ConnectionString;
        var database = new SqlConnectionStringBuilder(Server!) { InitialCatalog = name }.ConnectionString;
        await ExecuteAsync(master, $"CREATE DATABASE [{name}] COLLATE {collation}");
        try { await test(database); }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
    }

    private static async Task<DatabaseReport> InspectAsync(string connection)
    {
        await using var db = Context(connection);
        return await DatabaseDoctor.InspectAsync(db);
    }

    private static async Task MigrateAsync(string connection)
    {
        await using var db = Context(connection);
        await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task An_empty_database_is_connected_but_has_everything_to_do()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            var report = await InspectAsync(connection);

            Assert.True(report.Connected);
            Assert.True(report.Supported);
            Assert.Equal("SqlServer", report.Engine);
            Assert.Equal(DatabaseState.NeedsRepair, report.State);
            Assert.Empty(report.AppliedMigrations);
            Assert.NotEmpty(report.PendingMigrations);
            Assert.Contains("Users", report.MissingTables);
            Assert.Contains("Issues", report.MissingTables);
            Assert.Null(report.Seed);                                   // there is nowhere to look yet
            Assert.True(report.CaseInsensitive);
            Assert.Contains("Thai_CI_AS", report.Collation);
            Assert.False(string.IsNullOrEmpty(report.ServerVersion));
            Assert.StartsWith("kpi_doctor_", report.DatabaseName);
        });
    }

    [Fact]
    public async Task Tables_without_the_starting_data_are_reported_as_missing_roles_and_permissions()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await MigrateAsync(connection);

            var report = await InspectAsync(connection);

            Assert.Empty(report.PendingMigrations);
            Assert.Empty(report.MissingTables);
            Assert.Empty(report.MissingColumns);
            Assert.NotNull(report.Seed);
            Assert.Equal(RoleNames.All.Length, report.Seed!.MissingRoles.Count);
            Assert.Equal(0, report.Seed.PermissionsFound);
            Assert.Equal(DatabaseState.NeedsRepair, report.State);
        });
    }

    [Fact]
    public async Task A_complete_database_is_ready_and_says_when_nobody_can_sign_in_yet()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await MigrateAsync(connection);
            await using (var db = Context(connection)) await DbSeeder.SeedAsync(db);

            var report = await InspectAsync(connection);

            Assert.Equal(DatabaseState.Ready, report.State);
            Assert.Equal(RoleNames.All.Length, report.Seed!.RolesFound);
            Assert.Equal(Permissions.All.Length, report.Seed.PermissionsFound);
            Assert.False(report.Seed.HasActiveAdmin);
            Assert.Contains("no-administrator", report.Warnings);
            Assert.DoesNotContain("case-sensitive-collation", report.Warnings);
        });
    }

    [Fact]
    public async Task A_case_sensitive_collation_is_a_warning_because_e_mail_addresses_would_stop_being_unique()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await MigrateAsync(connection);
            await using (var db = Context(connection)) await DbSeeder.SeedAsync(db);

            var report = await InspectAsync(connection);

            Assert.False(report.CaseInsensitive);
            Assert.Contains("case-sensitive-collation", report.Warnings);
        }, collation: "Latin1_General_CS_AS");
    }

    [Fact]
    public async Task A_table_that_was_dropped_is_reported_and_created_again_without_touching_the_rest()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await MigrateAsync(connection);
            await using (var db = Context(connection)) await DbSeeder.SeedAsync(db);
            await ExecuteAsync(connection, "INSERT INTO [AppSettings] ([Key], [Value], [UpdatedAt]) VALUES (N'issues.autoCloseDays', N'9', SYSUTCDATETIME())");
            await ExecuteAsync(connection, "DROP TABLE [AppSettingHistory]");

            var report = await InspectAsync(connection);
            Assert.Equal(["AppSettingHistory"], report.MissingTables);
            Assert.Empty(report.PendingMigrations);                       // the migration history still says it is there
            Assert.Equal(DatabaseState.NeedsRepair, report.State);

            await using var db2 = Context(connection);
            var preview = await DatabaseDoctor.PreviewAsync(db2);
            Assert.Equal(["create-tables"], preview.Steps);
            Assert.Contains("CREATE TABLE [AppSettingHistory]", preview.Script);
            Assert.Contains("GO", preview.Script);

            var result = await DatabaseDoctor.RepairAsync(db2, new RepairRequest());
            Assert.Equal(["create-tables"], result.Done);
            Assert.Equal(DatabaseState.Ready, result.Report.State);
            Assert.Equal("9", await ScalarAsync(connection, "SELECT [Value] FROM [AppSettings] WHERE [Key] = N'issues.autoCloseDays'"));   // other data untouched
            await ExecuteAsync(connection, "INSERT INTO [AppSettingHistory] ([Id], [Key], [ChangedAt]) VALUES (NEWID(), N'x', SYSUTCDATETIME())");   // and it works
        });
    }

    [Fact]
    public async Task A_dropped_table_comes_back_with_its_keys_and_indexes()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await MigrateAsync(connection);
            await ExecuteAsync(connection, "DROP TABLE [IssueComments]");

            await using var db = Context(connection);
            var result = await DatabaseDoctor.RepairAsync(db, new RepairRequest(Seed: false));

            Assert.Equal(["create-tables"], result.Done);
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N'FK_IssueComments_Issues_IssueId'"));
            Assert.True((int)(await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[IssueComments]') AND name LIKE N'IX_IssueComments%'"))! >= 1);
            Assert.Equal(DatabaseState.NeedsRepair, result.Report.State);       // only because the starting data was not asked for
            Assert.Empty(result.Report.MissingTables);
        });
    }

    [Fact]
    public async Task A_dropped_column_is_added_again_and_rows_that_were_there_keep_working()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await MigrateAsync(connection);
            await using (var db = Context(connection))
            {
                await DbSeeder.SeedAsync(db);
                var roleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id;
                db.Users.Add(new User { FirstName = "Existing", LastName = "Person", Email = "existing@example.com", PasswordHash = "x", RoleId = roleId });
                await db.SaveChangesAsync();
            }
            await ExecuteAsync(connection, "ALTER TABLE [Users] DROP COLUMN [FailedLoginCount], [PasswordChangedAt]");

            var report = await InspectAsync(connection);
            Assert.Equal(["FailedLoginCount", "PasswordChangedAt"], report.MissingColumns.Where(c => c.Table == "Users").Select(c => c.Column).Order());
            Assert.Contains(report.MissingColumns, c => c.Column == "FailedLoginCount" && c.StoreType == "int");

            await using var db2 = Context(connection);
            var preview = await DatabaseDoctor.PreviewAsync(db2);
            Assert.Equal(["add-columns"], preview.Steps);
            Assert.Contains("ADD [FailedLoginCount]", preview.Script);

            var result = await DatabaseDoctor.RepairAsync(db2, new RepairRequest());
            Assert.Equal(["add-columns"], result.Done);
            Assert.Equal(DatabaseState.Ready, result.Report.State);
            await using var db3 = Context(connection);
            var user = await db3.Users.SingleAsync(u => u.Email == "existing@example.com");
            Assert.Equal(0, user.FailedLoginCount);                          // a neutral value for the row that was already there
            Assert.Null(user.PasswordChangedAt);
        });
    }

    [Fact]
    public async Task Repairing_an_empty_database_applies_the_migrations_then_adds_the_starting_data()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await using var db = Context(connection);

            var preview = await DatabaseDoctor.PreviewAsync(db);
            Assert.Equal(["apply-migrations"], preview.Steps);          // seeding has to wait for the tables
            Assert.Null(preview.Script);

            var result = await DatabaseDoctor.RepairAsync(db, new RepairRequest());

            Assert.Equal(["apply-migrations", "seed"], result.Done);
            Assert.Equal(DatabaseState.Ready, result.Report.State);
            Assert.Equal(RoleNames.All.Length, result.Report.Seed!.RolesFound);

            var again = await DatabaseDoctor.RepairAsync(db, new RepairRequest());
            Assert.Empty(again.Done);                                    // nothing left to do, nothing done
        });
    }

    [Fact]
    public async Task A_repair_only_does_the_steps_that_were_asked_for()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await using var db = Context(connection);

            var nothing = await DatabaseDoctor.RepairAsync(db, new RepairRequest(false, false, false));
            Assert.Empty(nothing.Done);
            Assert.NotEmpty(nothing.Report.PendingMigrations);

            var migrationsOnly = await DatabaseDoctor.RepairAsync(db, new RepairRequest(true, false, false));
            Assert.Equal(["apply-migrations"], migrationsOnly.Done);
            Assert.Empty(migrationsOnly.Report.PendingMigrations);
            Assert.Equal(DatabaseState.NeedsRepair, migrationsOnly.Report.State);       // the starting data is still missing
        });
    }

    [Fact]
    public async Task Things_the_application_does_not_know_about_are_left_alone()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async connection =>
        {
            await using (var db = Context(connection)) { await db.Database.MigrateAsync(); await DbSeeder.SeedAsync(db); }
            await ExecuteAsync(connection, "CREATE TABLE [SomeoneElsesTable] ([Id] int NOT NULL); ALTER TABLE [Roles] ADD [ExtraColumn] int NULL");

            var report = await InspectAsync(connection);

            Assert.Equal(DatabaseState.Ready, report.State);
            Assert.Empty(report.MissingTables);
            Assert.Empty(report.MissingColumns);
        });
    }

    [Fact]
    public async Task A_database_that_does_not_exist_is_told_apart_from_a_server_that_cannot_be_reached()
    {
        if (Server is null) return;

        var missing = new SqlConnectionStringBuilder(Server) { InitialCatalog = "kpi_no_such_" + Guid.NewGuid().ToString("N")[..10], ConnectTimeout = 8 }.ConnectionString;
        var noDatabase = await InspectAsync(missing);
        Assert.False(noDatabase.Connected);
        Assert.Equal(DatabaseState.Blocked, noDatabase.State);
        Assert.Equal("no-database", noDatabase.ConnectionProblem);

        var nobodyHome = new SqlConnectionStringBuilder { DataSource = "127.0.0.1,1", InitialCatalog = "x", ConnectTimeout = 3, TrustServerCertificate = true, IntegratedSecurity = true }.ConnectionString;
        var unreachable = await InspectAsync(nobodyHome);
        Assert.False(unreachable.Connected);
        Assert.Equal("unreachable", unreachable.ConnectionProblem);
    }

    [Fact]
    public async Task A_wrong_password_is_reported_as_a_refused_login()
    {
        if (Server is null || !Server.Contains("User Id", StringComparison.OrdinalIgnoreCase)) return;     // needs a server that accepts SQL logins (CI)
        var wrong = new SqlConnectionStringBuilder(Server) { Password = "definitely-not-the-password", InitialCatalog = "master", ConnectTimeout = 8 }.ConnectionString;

        var report = await InspectAsync(wrong);

        Assert.False(report.Connected);
        Assert.Equal("login-failed", report.ConnectionProblem);
    }
}

public class DatabaseApiTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public DatabaseApiTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_administrators_may_look_and_only_those_who_manage_settings_may_repair()
    {
        var staff = await _factory.ClientAsync("staff-a");
        var admin = await _factory.ClientAsync("admin");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/database/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/database/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/database/repair/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/database/repair", new { })).StatusCode);

        var status = await admin.GetFromJsonAsync<JsonElement>("/api/database/status");
        Assert.Equal("InMemory", status.GetProperty("engine").GetString());      // the test server has no real database
        Assert.False(status.GetProperty("supported").GetBoolean());
        var repair = await admin.PostAsJsonAsync("/api/database/repair", new { });
        Assert.Equal(HttpStatusCode.OK, repair.StatusCode);
        Assert.Equal(0, (await repair.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("done").GetArrayLength());
    }
}

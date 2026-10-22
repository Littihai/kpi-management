using System.Text;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace KPI.Tests;

/// <summary>
/// The SQL files an administrator runs (deploy/sqlserver) are generated from the code, so they cannot drift from it.
/// When one of these tests fails, regenerate the files:  UPDATE_GOLDEN=1 dotnet test --filter DeploymentScriptTests
/// </summary>
public class DeploymentScriptTests
{
    private static string DeployDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "deploy", "sqlserver");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("deploy/sqlserver was not found above " + AppContext.BaseDirectory);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimStart('﻿');

    private static void MatchesFile(string fileName, string actual)
    {
        var path = Path.Combine(DeployDirectory(), fileName);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return;
        }
        Assert.True(File.Exists(path), $"{fileName} is missing. Generate it with UPDATE_GOLDEN=1.");
        Assert.True(Normalize(File.ReadAllText(path)) == Normalize(actual),
            $"deploy/sqlserver/{fileName} is out of date. Regenerate it with: UPDATE_GOLDEN=1 dotnet test --filter DeploymentScriptTests");
    }

    private static AppDbContext SqlServerContext(string connection = "Server=unused;Database=unused;TrustServerCertificate=True")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:DefaultConnection"] = connection,
        }).Build());
        return new AppDbContext(options.Options);
    }

    public static string SchemaScript()
    {
        using var db = SqlServerContext();
        var body = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        return "-- Helpdesk on SQL Server, step 2 of 3: the tables.\n" +
               "-- GENERATED from the SQL Server migrations (see DeploymentScriptTests). Do not edit by hand.\n" +
               "-- Run it against the database from step 1:  sqlcmd -S <server> -d <database> -b -f 65001 -i 02-schema.sql\n" +
               "-- Safe to run again: it only applies the migrations the database does not have yet. The application does the\n" +
               "-- same thing itself when it starts (unless Database__AutoMigrate=false), so this file is for people who want\n" +
               "-- to review and run the change by hand.\n\n" + body.Replace("\r\n", "\n");
    }

    [Fact]
    public void The_schema_script_matches_the_sql_server_migrations() => MatchesFile("02-schema.sql", SchemaScript());

    [Fact]
    public void The_seed_script_matches_what_the_application_seeds() => MatchesFile("03-seed.sql", SeedScript.BuildSqlServer());

    [Fact]
    public void The_seed_script_covers_every_role_and_permission_and_never_touches_custom_roles()
    {
        var sql = SeedScript.BuildSqlServer();

        foreach (var role in RoleNames.All) Assert.Contains($"N'{role}'", sql);
        foreach (var permission in Permissions.All) Assert.Contains($"N'{permission}'", sql);
        Assert.DoesNotContain("DELETE FROM [Roles]", sql);
        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RoleNames.All.Length, DbSeeder.BuiltInRoles.Count);
        Assert.Equal(DbSeeder.BuiltInRoles.Count, DbSeeder.BuiltInRoles.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void A_permission_gets_the_same_id_every_time_and_two_never_share_one()
    {
        Assert.Equal(DbSeeder.PermissionIdFor("issue.view"), DbSeeder.PermissionIdFor("issue.view"));
        Assert.Equal(Permissions.All.Length, Permissions.All.Select(DbSeeder.PermissionIdFor).Distinct().Count());
    }

    // ───────────── against a real SQL Server (only when TEST_SQLSERVER is set) ─────────────

    private static readonly string? Server = Environment.GetEnvironmentVariable("TEST_SQLSERVER");

    private static async Task RunScriptAsync(string connectionString, string sql, Dictionary<string, string>? variables = null)
    {
        foreach (var (name, value) in variables ?? [])
            sql = sql.Replace($"$({name})", value);
        // sqlcmd splits a script into batches at a line that holds only GO
        var batches = System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var batch in batches.Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task The_three_scripts_build_a_database_the_application_accepts_as_it_is()
    {
        if (Server is null) return;
        var name = "kpi_scripts_" + Guid.NewGuid().ToString("N")[..12];
        var login = "kpi_app_" + Guid.NewGuid().ToString("N")[..8];
        var master = new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString;
        var database = new SqlConnectionStringBuilder(Server) { InitialCatalog = name }.ConnectionString;
        var directory = DeployDirectory();
        var sysadminDatabase = name + "_w";
        try
        {
            // 01 needs rights to create logins; a Windows-authenticated LocalDB user has them, and so does 'sa' in CI
            await RunScriptAsync(master, File.ReadAllText(Path.Combine(directory, "01-create-database.sql")), new()
            {
                ["DbName"] = name, ["AuthMode"] = "SQL", ["AppLogin"] = login, ["AppPassword"] = "Kpi-Test-Pass-" + Guid.NewGuid().ToString("N")[..10] + "Aa1",
            });
            await RunScriptAsync(master, File.ReadAllText(Path.Combine(directory, "01-create-database.sql")), new()      // and again
            {
                ["DbName"] = name, ["AuthMode"] = "SQL", ["AppLogin"] = login, ["AppPassword"] = "Another-Pass-1Aa",
            });
            // the account of a person who can already do everything (a sysadmin) is the database owner: nothing to add for it
            if (OperatingSystem.IsWindows() && new SqlConnectionStringBuilder(Server).IntegratedSecurity)
                await RunScriptAsync(master, File.ReadAllText(Path.Combine(directory, "01-create-database.sql")), new()
                {
                    ["DbName"] = sysadminDatabase, ["AuthMode"] = "WINDOWS", ["AppLogin"] = System.Security.Principal.WindowsIdentity.GetCurrent().Name, ["AppPassword"] = "unused",
                });
            await RunScriptAsync(database, File.ReadAllText(Path.Combine(directory, "02-schema.sql")));
            await RunScriptAsync(database, File.ReadAllText(Path.Combine(directory, "03-seed.sql")));
            await RunScriptAsync(database, File.ReadAllText(Path.Combine(directory, "03-seed.sql")));                        // and again

            await using var db = SqlServerContext(database);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());                    // the script recorded the migrations
            Assert.Equal(RoleNames.All.OrderBy(n => n), (await db.Roles.Select(r => r.Name).ToListAsync()).OrderBy(n => n));
            Assert.Equal(Permissions.All.Length, await db.Permissions.CountAsync());
            var grantsFromScript = await db.RolePermissions.CountAsync();
            Assert.Equal(RolePermissionMap.Defaults.Sum(r => r.Value.Distinct().Count()), grantsFromScript);

            // what the application does at start-up finds nothing left to do
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db);
            Assert.Equal(grantsFromScript, await db.RolePermissions.CountAsync());
            Assert.Equal(RoleNames.All.Length, await db.Roles.CountAsync());
            Assert.Equal(Permissions.All.Length, await db.Permissions.CountAsync());
            var manager = await db.Roles.SingleAsync(r => r.Name == RoleNames.Manager);
            Assert.Equal(IssueVisibility.Department, manager.IssueVisibility);
            Assert.Equal(DbSeeder.BuiltInRoles.Single(r => r.Name == RoleNames.Manager).Description, manager.Description);

        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{sysadminDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{sysadminDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{sysadminDatabase}]; END; IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END; IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}];";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Running_the_create_script_again_gives_the_login_the_new_password_and_never_touches_a_sysadmin()
    {
        if (Server is null) return;
        var name = "kpi_scripts_" + Guid.NewGuid().ToString("N")[..12];
        var login = "helpdesk_s_" + Guid.NewGuid().ToString("N")[..8];
        var admin = "helpdesk_admin_" + Guid.NewGuid().ToString("N")[..8];
        var master = new SqlConnectionStringBuilder(Server) { InitialCatalog = "master" }.ConnectionString;
        var directory = DeployDirectory();
        string As(string user, string password) => new SqlConnectionStringBuilder(Server) { InitialCatalog = "master", IntegratedSecurity = false, UserID = user, Password = password }.ConnectionString;
        async Task Run(string user, string password) => await RunScriptAsync(master, File.ReadAllText(Path.Combine(directory, "01-create-database.sql")), new()
        {
            ["DbName"] = name, ["AuthMode"] = "SQL", ["AppLogin"] = user, ["AppPassword"] = password,
        });
        try
        {
            await Run(login, "First-Pass-1Aa-" + Guid.NewGuid().ToString("N")[..6]);
            var second = "Second-Pass-1Aa-" + Guid.NewGuid().ToString("N")[..6];
            await Run(login, second);                           // a second machine, or a reinstall: the password in the new settings file must be the login's
            SqlConnection.ClearAllPools();
            await using (var connection = new SqlConnection(As(login, second))) { await connection.OpenAsync(); }

            // an administrator's login is refused, not reset
            var adminPassword = "Admin-Pass-1Aa-" + Guid.NewGuid().ToString("N")[..6];
            await using (var connection = new SqlConnection(master))
            {
                await connection.OpenAsync();
                await using var create = connection.CreateCommand();
                create.CommandText = $"CREATE LOGIN [{admin}] WITH PASSWORD = N'{adminPassword}', CHECK_POLICY = OFF; ALTER SERVER ROLE sysadmin ADD MEMBER [{admin}]";
                await create.ExecuteNonQueryAsync();
            }
            var refused = await Assert.ThrowsAsync<SqlException>(() => Run(admin, "Some-Other-Pass-1Aa"));
            Assert.Contains("sysadmin", refused.Message);
            SqlConnection.ClearAllPools();
            await using (var connection = new SqlConnection(As(admin, adminPassword))) { await connection.OpenAsync(); }   // still its own password
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END; IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}]; IF SUSER_ID(N'{admin}') IS NOT NULL DROP LOGIN [{admin}];";
            await drop.ExecuteNonQueryAsync();
        }
    }
}

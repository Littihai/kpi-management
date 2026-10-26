using KPI.Application.Issues;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Issues;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace KPI.Tests;

/// <summary>Choosing the database engine, and the SQL Server migrations that go with it.</summary>
public class DatabaseProviderTests
{
    private static IConfiguration Config(string? provider, string connection = "Server=unused;Database=unused;TrustServerCertificate=True") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["ConnectionStrings:DefaultConnection"] = connection,
        }).Build();

    private static AppDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(options, Config("SqlServer"));
        return new AppDbContext(options.Options);
    }

    [Theory]
    [InlineData(null, DatabaseEngine.PostgreSql)]
    [InlineData("PostgreSql", DatabaseEngine.PostgreSql)]
    [InlineData("postgres", DatabaseEngine.PostgreSql)]
    [InlineData("SqlServer", DatabaseEngine.SqlServer)]
    [InlineData("sqlserver", DatabaseEngine.SqlServer)]
    [InlineData(" MsSql ", DatabaseEngine.SqlServer)]
    public void The_engine_is_read_from_the_configuration_and_defaults_to_postgresql(string? value, DatabaseEngine expected) =>
        Assert.Equal(expected, DatabaseProvider.EngineOf(Config(value)));

    [Fact]
    public void An_unknown_engine_stops_the_start_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseProvider.EngineOf(Config("Oracle")));
        Assert.Contains("PostgreSql or SqlServer", ex.Message);
    }

    [Fact]
    public void SQL_Server_has_its_own_migrations_in_their_own_assembly()
    {
        using var db = SqlServerContext();

        var migrations = db.Database.GetMigrations().ToList();

        Assert.NotEmpty(migrations);
        Assert.Equal(migrations.OrderBy(m => m, StringComparer.Ordinal), migrations);
        Assert.Equal(DatabaseProvider.SqlServerMigrationsAssembly,
            db.GetService<IMigrationsAssembly>().Assembly.GetName().Name);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
    }

    [Fact]
    public void The_entity_model_matches_the_latest_sql_server_snapshot()
    {
        using var db = SqlServerContext();

        var differ = db.GetService<IMigrationsModelDiffer>();
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var current = db.GetService<IDesignTimeModel>().Model;
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshot);
        if (snapshot is IMutableModel mutable) snapshot = mutable.FinalizeModel();
        snapshot = initializer.Initialize(snapshot);

        var differences = differ.GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel());

        Assert.True(differences.Count == 0,
            "The model changed without a SQL Server migration (see KPI.Migrations.SqlServer/README.md). " +
            $"Pending: {string.Join(", ", differences.Select(d => d.GetType().Name))}");
    }
}

/// <summary>
/// Runs against a real SQL Server when TEST_SQLSERVER holds a connection string for the server (no database name),
/// for example Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True. Without it the
/// tests do nothing, so the normal run needs no SQL Server. CI sets it.
/// </summary>
public class SqlServerLiveTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("TEST_SQLSERVER");

    private static async Task WithDatabaseAsync(Func<AppDbContext, Task> test)
    {
        var name = "kpi_test_" + Guid.NewGuid().ToString("N");
        var admin = new SqlConnectionStringBuilder(Server!) { InitialCatalog = "master" }.ConnectionString;
        var database = new SqlConnectionStringBuilder(Server!) { InitialCatalog = name }.ConnectionString;

        await using (var connection = new SqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{name}] COLLATE Thai_CI_AS";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProvider.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer",
                ["ConnectionStrings:DefaultConnection"] = database,
            }).Build());
            await using var db = new AppDbContext(options.Options);
            await test(db);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task The_migrations_apply_to_an_empty_database_and_leave_nothing_pending()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async db =>
        {
            await db.Database.MigrateAsync();

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        });
    }

    [Fact]
    public async Task The_seed_fills_roles_and_permissions_and_can_be_run_again()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async db =>
        {
            await db.Database.MigrateAsync();

            await DbSeeder.SeedAsync(db);
            await DbSeeder.SeedAsync(db);

            Assert.Equal(RoleNames.All.OrderBy(n => n), (await db.Roles.Select(r => r.Name).ToListAsync()).OrderBy(n => n));
            Assert.Equal(Permissions.All.Length, await db.Permissions.CountAsync());
            Assert.Equal(RolePermissionMap.Defaults.Sum(r => r.Value.Length), await db.RolePermissions.CountAsync());
        });
    }

    [Fact]
    public async Task An_e_mail_address_is_unique_whatever_its_case_and_search_ignores_case()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async db =>
        {
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db);
            var roleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id;
            db.Users.Add(new User { FirstName = "A", LastName = "B", Email = "mixed.case@example.com", PasswordHash = "x", RoleId = roleId });
            await db.SaveChangesAsync();

            db.Users.Add(new User { FirstName = "C", LastName = "D", Email = "MIXED.CASE@EXAMPLE.COM", PasswordHash = "x", RoleId = roleId });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            Assert.Equal(1, await db.Users.CountAsync(u => u.Email.ToLower().Contains("mixed")));
        });
    }

    [Fact]
    public async Task Binary_attachments_and_thai_text_round_trip_and_the_issue_number_is_unique()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async db =>
        {
            await db.Database.MigrateAsync();
            var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
            var issue = new Issue
            {
                Title = "เครื่องพิมพ์ชั้น 2 ไม่ออก", Description = "สั่งพิมพ์แล้วไม่มีอะไรออกมา", ReporterName = "สมชาย", NumberYear = 2569, Number = 1,
                ResponseDueAt = DateTime.UtcNow, ResolveDueAt = DateTime.UtcNow,
            };
            issue.Attachments.Add(new IssueAttachment { FileName = "ภาพ.png", ContentType = "image/png", SizeBytes = bytes.Length, Data = bytes });
            db.Issues.Add(issue);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var loaded = await db.Issues.Include(i => i.Attachments).SingleAsync();

            Assert.Equal("เครื่องพิมพ์ชั้น 2 ไม่ออก", loaded.Title);
            Assert.Equal("ภาพ.png", loaded.Attachments.Single().FileName);
            Assert.Equal(bytes, loaded.Attachments.Single().Data);

            db.Issues.Add(new Issue { Title = "ซ้ำเลข", Description = "x", ReporterName = "y", NumberYear = 2569, Number = 1, ResponseDueAt = DateTime.UtcNow, ResolveDueAt = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());   // the service retries on this
        });
    }

    [Fact]
    public async Task The_attention_numbers_are_counted_by_one_query_that_SQL_Server_accepts()
    {
        if (Server is null) return;
        await WithDatabaseAsync(async db =>
        {
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db);
            var roleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id;
            var me = new User { FirstName = "A", LastName = "B", Email = "me@example.com", PasswordHash = "x", RoleId = roleId };
            var other = new User { FirstName = "C", LastName = "D", Email = "other@example.com", PasswordHash = "x", RoleId = roleId };
            db.Users.AddRange(me, other);
            await db.SaveChangesAsync();

            var now = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
            var number = 0;
            void Add(IssueStatus status, Guid? assignee = null, Guid? reporter = null, bool late = false) => db.Issues.Add(new Issue
            {
                Title = "เรื่องที่ " + ++number, Description = "x", ReporterName = "y", NumberYear = 2569, Number = number,
                Status = status, AssigneeId = assignee, ReporterUserId = reporter,
                ResponseDueAt = now.AddHours(late ? -1 : 1), ResolveDueAt = now.AddHours(late ? -1 : 1),
            });
            Add(IssueStatus.New);                                    // unclaimed
            Add(IssueStatus.New, late: true);                        // unclaimed and overdue
            Add(IssueStatus.InProgress, assignee: me.Id);            // mine to work on
            Add(IssueStatus.Assigned, assignee: me.Id, late: true);  // mine, and overdue
            Add(IssueStatus.WaitingForReporter, reporter: me.Id);
            Add(IssueStatus.Resolved, reporter: me.Id);
            Add(IssueStatus.Closed, reporter: me.Id, late: true);    // finished: waits for nobody
            Add(IssueStatus.OnHold, assignee: me.Id);                // parked: nobody's move
            Add(IssueStatus.InProgress, assignee: other.Id);         // somebody else's
            await db.SaveChangesAsync();

            var worker = await IssueService.AttentionCounts(db.Issues, me.Id, queueWorker: true, now).SingleAsync();
            Assert.Equal(new IssueAttentionDto(6, 2, 2, 2, 1, 1), worker);

            var requester = await IssueService.AttentionCounts(db.Issues, me.Id, queueWorker: false, now).SingleAsync();
            Assert.Equal(new IssueAttentionDto(4, 0, 2, 0, 1, 1), requester);

            Assert.Empty(await IssueService.AttentionCounts(db.Issues, Guid.NewGuid(), queueWorker: false, now).ToListAsync());
        });
    }
}

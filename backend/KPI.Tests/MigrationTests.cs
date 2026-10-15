using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace KPI.Tests;

/// <summary>
/// The API applies migrations when it starts. EF Core refuses to do that (and the app would not start) when the
/// entity model has changed without a matching migration, so catch that here, in CI, without needing PostgreSQL.
/// </summary>
public class MigrationTests
{
    static MigrationTests()
    {
        // Program.cs sets the same switch before anything touches the database. Without it Npgsql maps DateTime to
        // "timestamp with time zone" and every date column would look changed against the snapshot.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private static AppDbContext NpgsqlContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused;Database=unused").Options);

    [Fact]
    public void Migrations_exist_and_are_found_by_the_context()
    {
        using var db = NpgsqlContext();

        var migrations = db.Database.GetMigrations().ToList();

        Assert.NotEmpty(migrations);
        Assert.Equal(migrations.OrderBy(m => m, StringComparer.Ordinal), migrations); // ids sort by creation time
    }

    [Fact]
    public void The_entity_model_matches_the_latest_migration_snapshot()
    {
        using var db = NpgsqlContext();

        var differ = db.GetService<IMigrationsModelDiffer>();
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var current = db.GetService<IDesignTimeModel>().Model;

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshot);
        if (snapshot is IMutableModel mutable) snapshot = mutable.FinalizeModel();
        snapshot = initializer.Initialize(snapshot);

        var differences = differ.GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel());

        var summary = string.Join(", ", differences.Select(Describe));
        Assert.True(differences.Count == 0,
            "The model changed without a migration. Run: dotnet ef migrations add <Name> " +
            $"--project KPI.Infrastructure --startup-project KPI.API. Pending: {summary}");
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        ColumnOperation column => $"{operation.GetType().Name}({column.Table}.{column.Name})",
        TableOperation table => $"{operation.GetType().Name}({table.Name})",
        _ => operation.GetType().Name,
    };
}

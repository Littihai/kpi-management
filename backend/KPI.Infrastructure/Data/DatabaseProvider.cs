using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KPI.Infrastructure.Data;

public enum DatabaseEngine { PostgreSql, SqlServer }

/// <summary>
/// Which database the system runs on, chosen by <c>Database:Provider</c> (PostgreSql, the default, or SqlServer).
/// Each engine has its own set of migrations, because a migration is written in one engine's SQL.
/// </summary>
public static class DatabaseProvider
{
    /// <summary>The assembly that holds the SQL Server migrations (PostgreSQL's are in this assembly).</summary>
    public const string SqlServerMigrationsAssembly = "KPI.Migrations.SqlServer";

    public static DatabaseEngine EngineOf(IConfiguration config)
    {
        var name = (config["Database:Provider"] ?? "PostgreSql").Trim();
        if (name.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) || name.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            return DatabaseEngine.PostgreSql;
        if (name.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || name.Equals("MsSql", StringComparison.OrdinalIgnoreCase))
            return DatabaseEngine.SqlServer;
        throw new InvalidOperationException($"Database:Provider '{name}' is not supported. Use PostgreSql or SqlServer.");
    }

    public static void Configure(DbContextOptionsBuilder options, IConfiguration config)
    {
        var connection = config.GetConnectionString("DefaultConnection");
        switch (EngineOf(config))
        {
            case DatabaseEngine.SqlServer:
                options.UseSqlServer(connection, sql => sql
                    .MigrationsAssembly(SqlServerMigrationsAssembly)
                    .EnableRetryOnFailure(maxRetryCount: 3));
                break;
            default:
                options.UseNpgsql(connection);
                break;
        }
    }
}

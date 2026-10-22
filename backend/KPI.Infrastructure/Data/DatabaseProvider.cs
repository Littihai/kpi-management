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
        // The parts the setup page writes (Database:Server ...) exist only for SQL Server, so they imply it when no provider is named.
        var named = config["Database:Provider"];
        var name = (string.IsNullOrWhiteSpace(named) ? (string.IsNullOrWhiteSpace(config["Database:Server"]) ? "PostgreSql" : "SqlServer") : named).Trim();
        if (name.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) || name.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            return DatabaseEngine.PostgreSql;
        if (name.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) || name.Equals("MsSql", StringComparison.OrdinalIgnoreCase))
            return DatabaseEngine.SqlServer;
        throw new InvalidOperationException($"Database:Provider '{name}' is not supported. Use PostgreSql or SqlServer.");
    }

    /// <summary>
    /// The connection string in use. Parts written by the setup wizard (Database:Server, Name, Auth, User, PasswordProtected,
    /// TrustServerCertificate; the password is encrypted with the Jwt key) win over a ready-made ConnectionStrings:DefaultConnection.
    /// </summary>
    public static string? ConnectionString(IConfiguration config)
    {
        var server = config["Database:Server"];
        if (string.IsNullOrWhiteSpace(server)) return config.GetConnectionString("DefaultConnection");

        var windows = string.Equals(config["Database:Auth"], "windows", StringComparison.OrdinalIgnoreCase);
        string? password = null;
        if (!windows)
        {
            var key = config["Jwt:Key"];
            password = string.IsNullOrEmpty(key) || string.IsNullOrEmpty(config["Database:PasswordProtected"])
                ? null
                : new Services.SecretProtector(key).Unprotect(config["Database:PasswordProtected"]!);
        }
        return BuildSqlServer(server, config["Database:Name"] ?? string.Empty, windows, config["Database:User"], password,
            !string.Equals(config["Database:TrustServerCertificate"], "false", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsConfigured(IConfiguration config) => !string.IsNullOrWhiteSpace(ConnectionString(config));

    private static string? WithoutBlocking(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) return connection;
        try { return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection) { PoolBlockingPeriod = Microsoft.Data.SqlClient.PoolBlockingPeriod.NeverBlock }.ConnectionString; }
        catch (ArgumentException) { return connection; }   // not a string SqlClient understands: let it say so when it is used
    }

    /// <summary>A SQL Server connection string from its parts; the builder quotes anything that would otherwise change the meaning.</summary>
    public static string BuildSqlServer(string server, string database, bool windowsAuth, string? user, string? password, bool trustServerCertificate, int connectTimeoutSeconds = 15)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = server.Trim(),
            InitialCatalog = database.Trim(),
            TrustServerCertificate = trustServerCertificate,
            Encrypt = true,
            ConnectTimeout = connectTimeoutSeconds,
            ConnectRetryCount = 0,   // fail at once when the server is not there: the wizard and the supervisor retry on their own terms
            ApplicationName = "Helpdesk",
        };
        if (windowsAuth) builder.IntegratedSecurity = true;
        else { builder.UserID = user ?? string.Empty; builder.Password = password ?? string.Empty; }
        return builder.ConnectionString;
    }

    /// <param name="quick">
    /// SQL Server only. For a check that must answer at once and must not remember a failure: no retry of transient errors
    /// (error 4060, a database that does not exist, counts as transient and would be retried for several seconds) and no pool
    /// blocking period (after a failed connect SqlClient keeps answering with the same error for 5 to 60 seconds, so
    /// "create the database, then press Test again" would still say the database is missing).
    /// </param>
    public static void Configure(DbContextOptionsBuilder options, IConfiguration config, bool quick = false)
    {
        var connection = ConnectionString(config);
        switch (EngineOf(config))
        {
            case DatabaseEngine.SqlServer:
                options.UseSqlServer(quick ? WithoutBlocking(connection) : connection, sql =>
                {
                    sql.MigrationsAssembly(SqlServerMigrationsAssembly);
                    if (!quick) sql.EnableRetryOnFailure(maxRetryCount: 3);
                });
                break;
            default:
                options.UseNpgsql(connection);
                break;
        }
    }
}

using System.Data.Common;
using KPI.Application.Database;
using KPI.Domain.Constants;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace KPI.Infrastructure.Data;

/// <summary>
/// Looks at a database and says what is missing compared with what the application needs, and creates what is missing
/// without touching anything that is there. It never drops or alters an existing table or column: a difference it cannot
/// fix safely is reported, not "repaired".
/// </summary>
public static class DatabaseDoctor
{
    // ───────────── looking ─────────────

    public static async Task<DatabaseReport> InspectAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var engine = EngineOf(db);
        if (!db.Database.IsRelational())
            return Empty(engine, supported: false, connected: true, DatabaseState.Blocked);

        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var (problem, message) = ClassifyConnectionFailure(ex);
            return Empty(engine, supported: true, connected: false, DatabaseState.Blocked) with { ConnectionProblem = problem, ConnectionMessage = message };
        }

        try
        {
            var connection = db.Database.GetDbConnection();
            var version = await ScalarAsync(connection, VersionSql(engine), cancellationToken);
            var name = await ScalarAsync(connection, engine == "SqlServer" ? "SELECT DB_NAME()" : "SELECT current_database()", cancellationToken);
            var collation = await ScalarAsync(connection, CollationSql(engine), cancellationToken);
            bool? caseInsensitive = engine == "SqlServer" && collation is not null ? collation.Contains("_CI", StringComparison.OrdinalIgnoreCase) : null;

            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            var existing = await ExistingColumnsAsync(db, connection, engine, cancellationToken);
            var (missingTables, missingColumns) = Compare(db, existing, engine);

            var seed = missingTables.Contains("Roles") || missingTables.Contains("Permissions") || missingTables.Contains("Users") || missingTables.Contains("RolePermissions")
                       || missingColumns.Any(c => c.Table is "Roles" or "Permissions" or "Users" or "RolePermissions")
                ? null
                : await SeedAsync(db, cancellationToken);

            var warnings = new List<string>();
            if (caseInsensitive == false) warnings.Add("case-sensitive-collation");
            if (seed is { HasActiveAdmin: false }) warnings.Add("no-administrator");

            var needsRepair = pending.Count > 0 || missingTables.Count > 0 || missingColumns.Count > 0
                              || seed is { MissingRoles.Count: > 0 } || (seed is not null && seed.PermissionsFound < seed.PermissionsExpected);

            return new DatabaseReport(engine, true, true, null, null, version, name, collation, caseInsensitive,
                applied, pending, missingTables, missingColumns, seed, warnings,
                needsRepair ? DatabaseState.NeedsRepair : DatabaseState.Ready);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static DatabaseReport Empty(string engine, bool supported, bool connected, DatabaseState state) =>
        new(engine, supported, connected, null, null, null, null, null, null, [], [], [], [], null, [], state);

    public static string EngineOf(AppDbContext db) => db.Database.ProviderName switch
    {
        { } p when p.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) => "SqlServer",
        { } p when p.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) => "PostgreSql",
        { } p when p.Contains("InMemory", StringComparison.OrdinalIgnoreCase) => "InMemory",
        var other => other ?? "Unknown",
    };

    private static string VersionSql(string engine) => engine == "SqlServer"
        ? "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)) + N' (' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) + N')'"
        : "SHOW server_version";

    private static string CollationSql(string engine) => engine == "SqlServer"
        ? "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))"
        : "SELECT datcollate FROM pg_database WHERE datname = current_database()";

    /// <summary>Turns a failure to connect into something an administrator can act on.</summary>
    public static (string Problem, string Message) ClassifyConnectionFailure(Exception exception)
    {
        // The innermost exception is the real cause, so it is asked first. It does not always say anything: on Linux SqlClient raises
        // a SqlException ("A network-related or instance-specific error occurred ...") around a SocketException whose own message is
        // just "Success", so when the innermost one is not recognised the ones around it are asked in turn.
        var chain = new List<Exception>();
        for (var e = exception; e is not null; e = e.InnerException) chain.Add(e);
        for (var i = chain.Count - 1; i >= 0; i--)
            if (Recognise(chain[i]) is { } known) return known;
        return ("other", chain[^1].Message);
    }

    private static (string Problem, string Message)? Recognise(Exception exception)
    {
        var message = exception.Message;
        var number = (exception as SqlException)?.Number;
        var text = message.ToLowerInvariant();

        if (number is 4060 || text.Contains("cannot open database") || text.Contains("does not exist") && text.Contains("database"))
            return ("no-database", "The server answered, but the database does not exist or this account may not open it.");
        if (number is 18456 || number is 18452 || text.Contains("login failed") || text.Contains("password authentication failed"))
            return ("login-failed", "The server refused the sign-in: check the user name and password (or the Windows account).");
        if (number is 53 or 2 or -1 or 258 or 10060 or 10061 || text.Contains("network-related") || text.Contains("could not open a connection") || text.Contains("no such host") || text.Contains("name or service not known") || text.Contains("timeout") || text.Contains("timed out")
                || text.Contains("connection refused") || text.Contains("actively refused") || text.Contains("no connection could be made")
                || text.Contains("connection attempt failed") || text.Contains("unreachable") || text.Contains("unable to connect"))
            return ("unreachable", "Cannot reach the server: check its name or address, the port, that it is running, and the firewall.");
        return null;
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
        }
        catch
        {
            return null;   // a detail is nice to have; it must never stop the check
        }
    }

    private static string DefaultSchema(AppDbContext db, string engine) =>
        db.Model.GetDefaultSchema() ?? (engine == "SqlServer" ? "dbo" : "public");

    private static async Task<Dictionary<string, HashSet<string>>> ExistingColumnsAsync(
        AppDbContext db, DbConnection connection, string engine, CancellationToken cancellationToken)
    {
        var comparer = engine == "SqlServer" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var tables = new Dictionary<string, HashSet<string>>(comparer);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@schema";
        parameter.Value = DefaultSchema(db, engine);
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.GetString(0);
            if (!tables.TryGetValue(table, out var columns)) tables[table] = columns = new HashSet<string>(comparer);
            columns.Add(reader.GetString(1));
        }
        return tables;
    }

    /// <summary>What the model needs: every table with its columns and their store types.</summary>
    private static IEnumerable<(string Table, string? Schema, List<(string Column, string StoreType)> Columns)> Expected(AppDbContext db)
    {
        foreach (var entity in db.Model.GetEntityTypes().Where(e => e.GetTableName() is not null).GroupBy(e => (e.GetTableName()!, e.GetSchema())))
        {
            var (table, schema) = entity.Key;
            var id = StoreObjectIdentifier.Table(table, schema);
            var columns = new List<(string, string)>();
            foreach (var property in entity.SelectMany(e => e.GetProperties()))
                if (property.GetColumnName(id) is { } column && columns.All(c => c.Item1 != column))
                    columns.Add((column, property.GetColumnType(id)));
            yield return (table, schema, columns);
        }
    }

    private static (List<string> MissingTables, List<MissingColumnDto> MissingColumns) Compare(
        AppDbContext db, Dictionary<string, HashSet<string>> existing, string engine)
    {
        var missingTables = new List<string>();
        var missingColumns = new List<MissingColumnDto>();
        foreach (var (table, _, columns) in Expected(db).OrderBy(t => t.Table, StringComparer.Ordinal))
        {
            if (!existing.TryGetValue(table, out var have)) { missingTables.Add(table); continue; }
            foreach (var (column, storeType) in columns)
                if (!have.Contains(column)) missingColumns.Add(new MissingColumnDto(table, column, storeType));
        }
        return (missingTables, missingColumns);
    }

    private static async Task<SeedReport?> SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var roles = await db.Roles.AsNoTracking().Select(r => r.Name).ToListAsync(cancellationToken);
            var permissions = await db.Permissions.AsNoTracking().Select(p => p.Name).ToListAsync(cancellationToken);
            var admin = await db.Users.AsNoTracking().AnyAsync(u => u.IsActive && u.Role.Name == RoleNames.SuperAdmin, cancellationToken);
            return new SeedReport(
                RoleNames.All.Count(roles.Contains), RoleNames.All.Length,
                RoleNames.All.Where(r => !roles.Contains(r)).ToList(),
                Permissions.All.Count(permissions.Contains), Permissions.All.Length, admin);
        }
        catch
        {
            return null;
        }
    }

    // ───────────── repairing ─────────────

    /// <summary>What a repair would do, and the SQL for the part that creates tables and columns, so it can be read first.</summary>
    public static async Task<RepairPreview> PreviewAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var report = await InspectAsync(db, cancellationToken);
        var steps = new List<string>();
        string? script = null;
        if (report.Connected && report.Supported)
        {
            if (report.PendingMigrations.Count > 0) steps.Add("apply-migrations");
            else if (report.MissingTables.Count > 0 || report.MissingColumns.Count > 0)
            {
                if (report.MissingTables.Count > 0) steps.Add("create-tables");
                if (report.MissingColumns.Count > 0) steps.Add("add-columns");
                script = ScriptFor(db, report);
            }
            if (NeedsSeed(report)) steps.Add("seed");
        }
        return new RepairPreview(steps, script);
    }

    private static bool NeedsSeed(DatabaseReport report) =>
        report.Seed is { } s && (s.MissingRoles.Count > 0 || s.PermissionsFound < s.PermissionsExpected);

    /// <summary>Does what is asked, in the safe order (migrations, then missing tables and columns, then the starting data).</summary>
    public static async Task<RepairResult> RepairAsync(AppDbContext db, RepairRequest request, CancellationToken cancellationToken = default)
    {
        var done = new List<string>();
        var report = await InspectAsync(db, cancellationToken);
        if (!report.Connected || !report.Supported) return new RepairResult(done, report);

        if (request.ApplyMigrations && report.PendingMigrations.Count > 0)
        {
            await db.Database.MigrateAsync(cancellationToken);
            done.Add("apply-migrations");
            report = await InspectAsync(db, cancellationToken);
        }

        // Only when nothing is waiting to be migrated: a migration would create the same objects.
        if (request.CreateMissingObjects && report.PendingMigrations.Count == 0 && (report.MissingTables.Count > 0 || report.MissingColumns.Count > 0))
        {
            var commands = CommandsFor(db, report);
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var connection = db.Database.GetDbConnection();
                foreach (var command in commands)
                {
                    await using var dbCommand = connection.CreateCommand();
                    dbCommand.Transaction = transaction.GetDbTransaction();
                    dbCommand.CommandText = command;
                    await dbCommand.ExecuteNonQueryAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
            if (report.MissingTables.Count > 0) done.Add("create-tables");
            if (report.MissingColumns.Count > 0) done.Add("add-columns");
            report = await InspectAsync(db, cancellationToken);
        }

        if (request.Seed && report.Connected && report.MissingTables.Count == 0 && report.MissingColumns.Count == 0 && report.PendingMigrations.Count == 0)
        {
            var before = report.Seed;
            await DbSeeder.SeedAsync(db);
            if (before is null || NeedsSeed(report)) done.Add("seed");
            report = await InspectAsync(db, cancellationToken);
        }

        return new RepairResult(done, report);
    }

    private static string ScriptFor(AppDbContext db, DatabaseReport report)
    {
        var go = EngineOf(db) == "SqlServer";
        return string.Join("\n\n", CommandsFor(db, report).Select(c => go ? c.TrimEnd() + "\nGO" : c.TrimEnd()));
    }

    /// <summary>
    /// The SQL for exactly what is missing: the model is compared with an empty database, and only the parts about missing
    /// tables (with their indexes and keys) and missing columns are kept. New required columns get a neutral default so
    /// the statement works on a table that already has rows.
    /// </summary>
    private static List<string> CommandsFor(AppDbContext db, DatabaseReport report)
    {
        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var everything = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model);
        var missingTables = new HashSet<string>(report.MissingTables, StringComparer.OrdinalIgnoreCase);
        var missingColumns = report.MissingColumns.GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Column).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var operations = new List<MigrationOperation>();
        foreach (var operation in everything)
        {
            switch (operation)
            {
                case CreateTableOperation create when missingTables.Contains(create.Name):
                    operations.Add(create);
                    break;
                case CreateTableOperation create when missingColumns.TryGetValue(create.Name, out var columns):
                    foreach (var column in create.Columns.Where(c => columns.Contains(c.Name)))
                        operations.Add(ToAddColumn(create, column));
                    break;
                case CreateIndexOperation index when missingTables.Contains(index.Table):
                    operations.Add(index);
                    break;
                case AddForeignKeyOperation key when missingTables.Contains(key.Table):
                    operations.Add(key);
                    break;
            }
        }

        var generator = db.GetService<IMigrationsSqlGenerator>();
        return generator.Generate(operations, db.GetService<IDesignTimeModel>().Model).Select(c => c.CommandText).ToList();
    }

    private static AddColumnOperation ToAddColumn(CreateTableOperation table, AddColumnOperation column) => new()
    {
        Table = table.Name,
        Schema = table.Schema,
        Name = column.Name,
        ClrType = column.ClrType,
        ColumnType = column.ColumnType,
        IsNullable = column.IsNullable,
        IsUnicode = column.IsUnicode,
        MaxLength = column.MaxLength,
        Precision = column.Precision,
        Scale = column.Scale,
        IsFixedLength = column.IsFixedLength,
        IsRowVersion = column.IsRowVersion,
        DefaultValue = column.DefaultValue ?? (column.DefaultValueSql is null && !column.IsNullable ? NeutralDefault(column.ClrType) : null),
        DefaultValueSql = column.DefaultValueSql,
        ComputedColumnSql = column.ComputedColumnSql,
    };

    private static object? NeutralDefault(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(byte[])) return Array.Empty<byte>();
        if (type == typeof(DateTime)) return new DateTime(1970, 1, 1);
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

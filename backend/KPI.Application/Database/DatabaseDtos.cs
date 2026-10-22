namespace KPI.Application.Database;

public enum DatabaseState
{
    /// <summary>Everything the application needs is there.</summary>
    Ready,
    /// <summary>Connected, but something is missing that the repair can create.</summary>
    NeedsRepair,
    /// <summary>Cannot connect, or the engine is not one this check understands.</summary>
    Blocked,
}

public record MissingColumnDto(string Table, string Column, string StoreType);

public record SeedReport(
    int RolesFound, int RolesExpected, IReadOnlyList<string> MissingRoles,
    int PermissionsFound, int PermissionsExpected, bool HasActiveAdmin);

/// <summary>What the database looks like compared with what the application needs. Structured data only; the screen words it.</summary>
public record DatabaseReport(
    string Engine,
    bool Supported,
    bool Connected,
    /// <summary>When not connected: unreachable, login-failed, no-database or other.</summary>
    string? ConnectionProblem,
    string? ConnectionMessage,
    string? ServerVersion,
    string? DatabaseName,
    string? Collation,
    /// <summary>SQL Server only: whether text comparison ignores capitals (the application needs it to, for e-mail addresses).</summary>
    bool? CaseInsensitive,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<MissingColumnDto> MissingColumns,
    SeedReport? Seed,
    IReadOnlyList<string> Warnings,
    DatabaseState State);

public record RepairRequest(bool ApplyMigrations = true, bool CreateMissingObjects = true, bool Seed = true);

public record RepairPreview(IReadOnlyList<string> Steps, string? Script);

public record RepairResult(IReadOnlyList<string> Done, DatabaseReport Report);

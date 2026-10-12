namespace KPI.Domain.Constants;

public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Director = "Director";
    public const string Manager = "Manager";
    public const string TeamLeader = "TeamLeader";
    public const string Staff = "Staff";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [SuperAdmin, Director, Manager, TeamLeader, Staff, Viewer];

    /// <summary>SuperAdmin and Director see every department; all other roles are limited to their own.</summary>
    public static bool SeesAllDepartments(string? roleName) =>
        roleName is SuperAdmin or Director;
}

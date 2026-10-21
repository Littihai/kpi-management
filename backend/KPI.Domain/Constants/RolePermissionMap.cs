namespace KPI.Domain.Constants;

/// <summary>
/// The permissions each built-in role holds. Seeded (and re-synced) at startup.
/// Scope (all departments / own department / own issues) is enforced separately in the services.
/// </summary>
public static class RolePermissionMap
{
    private static readonly string[] ReportAndFollow = [Permissions.Issue.Create, Permissions.Issue.View];

    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
    {
        [RoleNames.SuperAdmin] = Permissions.All,

        [RoleNames.Director] =
        [
            Permissions.User.View,
            Permissions.Department.View,
            .. ReportAndFollow,
        ],

        [RoleNames.Manager] =
        [
            Permissions.User.View,
            Permissions.Department.View,
            .. ReportAndFollow,
        ],

        [RoleNames.TeamLeader] = [Permissions.Department.View, .. ReportAndFollow],

        [RoleNames.Staff] = [Permissions.Department.View, .. ReportAndFollow],

        [RoleNames.Viewer] = [Permissions.Department.View, .. ReportAndFollow],

        // Works the issue queue.
        [RoleNames.ITSupport] =
        [
            Permissions.Department.View,
            Permissions.Issue.Create, Permissions.Issue.View, Permissions.Issue.Manage,
        ],
    };
}

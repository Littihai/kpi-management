namespace KPI.Domain.Constants;

/// <summary>
/// The permissions each built-in role holds. Seeded (and re-synced) at startup.
/// Scope (all departments / own department / own issues) is enforced separately in the services.
/// </summary>
public static class RolePermissionMap
{
    private static readonly string[] ReportAndFollow = [Permissions.Issue.Create, Permissions.Issue.View];

    /// <summary>Which issues and which departments each built-in role sees. Re-synced at startup like the permissions.</summary>
    public static readonly IReadOnlyDictionary<string, (IssueVisibility Issues, bool AllDepartments)> Scopes =
        new Dictionary<string, (IssueVisibility, bool)>
        {
            [RoleNames.SuperAdmin] = (IssueVisibility.All, true),
            [RoleNames.Director] = (IssueVisibility.All, true),
            [RoleNames.ITSupport] = (IssueVisibility.All, false),
            [RoleNames.Manager] = (IssueVisibility.Department, false),
            [RoleNames.TeamLeader] = (IssueVisibility.Own, false),
            [RoleNames.Staff] = (IssueVisibility.Own, false),
            [RoleNames.Viewer] = (IssueVisibility.Own, false),
        };

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

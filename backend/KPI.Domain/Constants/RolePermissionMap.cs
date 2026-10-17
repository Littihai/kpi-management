namespace KPI.Domain.Constants;

/// <summary>
/// The permissions each built-in role holds. Seeded (and re-synced) at startup.
/// Scope (all departments / own department / own KPIs / own issues) is enforced separately in the services.
/// </summary>
public static class RolePermissionMap
{
    private static readonly string[] ReportAndFollow = [Permissions.Issue.Create, Permissions.Issue.View];

    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
    {
        [RoleNames.SuperAdmin] = Permissions.All,

        [RoleNames.Director] =
        [
            Permissions.Kpi.View, Permissions.Kpi.Approve, Permissions.Kpi.Reject, Permissions.Kpi.Close,
            Permissions.Project.View,
            Permissions.User.View,
            Permissions.Department.View,
            Permissions.Export.View,
            .. ReportAndFollow,
        ],

        [RoleNames.Manager] =
        [
            Permissions.Kpi.View, Permissions.Kpi.Create, Permissions.Kpi.Edit,
            Permissions.Kpi.Submit, Permissions.Kpi.Progress,
            Permissions.Project.View, Permissions.Project.Create, Permissions.Project.Edit, Permissions.Project.Delete,
            Permissions.User.View,
            Permissions.Department.View,
            Permissions.Export.View,
            .. ReportAndFollow,
        ],

        [RoleNames.TeamLeader] =
        [
            Permissions.Kpi.View, Permissions.Kpi.Progress,
            Permissions.Project.View, Permissions.Project.Create, Permissions.Project.Edit,
            Permissions.Department.View,
            .. ReportAndFollow,
        ],

        [RoleNames.Staff] =
        [
            Permissions.Kpi.View, Permissions.Kpi.Progress,
            Permissions.Project.View,
            Permissions.Department.View,
            .. ReportAndFollow,
        ],

        [RoleNames.Viewer] =
        [
            Permissions.Kpi.View,
            Permissions.Project.View,
            Permissions.Department.View,
            .. ReportAndFollow,
        ],

        // Works the issue queue. Reads KPIs in their own department like a viewer, nothing more.
        [RoleNames.ITSupport] =
        [
            Permissions.Kpi.View,
            Permissions.Project.View,
            Permissions.Department.View,
            Permissions.Issue.Create, Permissions.Issue.View, Permissions.Issue.Manage,
        ],
    };
}

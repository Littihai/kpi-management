namespace KPI.Domain.Constants;

public static class Permissions
{
    public static class User
    {
        public const string View   = "user.view";
        public const string Create = "user.create";
        public const string Edit   = "user.edit";
        public const string Delete = "user.delete";
    }

    public static class Role
    {
        public const string View = "role.view";
        /// <summary>Create, change and delete custom roles. A role can only be given rights its author holds.</summary>
        public const string Manage = "role.manage";
    }

    public static class Settings
    {
        public const string View = "settings.view";
        /// <summary>Change system settings (SLA targets, limits, e-mail on/off, retention).</summary>
        public const string Manage = "settings.manage";
    }

    public static class Department
    {
        public const string View   = "department.view";
        public const string Manage = "department.manage";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    public static class Job
    {
        public const string Trigger = "job.trigger";
    }

    public static class Issue
    {
        /// <summary>Report a problem. Every signed-in role has it.</summary>
        public const string Create = "issue.create";
        /// <summary>See issues in your scope: your own, your department's (managers), or all (IT, directors, admins).</summary>
        public const string View = "issue.view";
        /// <summary>Work on issues: assign, start, ask for information, resolve, reject, set priority, internal notes.</summary>
        public const string Manage = "issue.manage";
    }

    public static class Overview
    {
        /// <summary>The system-wide activity overview and request log (SuperAdmin only).</summary>
        public const string View = "overview.view";
    }

    public static readonly string[] All =
    [
        User.View, User.Create, User.Edit, User.Delete,
        Role.View, Role.Manage,
        Settings.View, Settings.Manage,
        Department.View, Department.Manage,
        Audit.View,
        Job.Trigger,
        Issue.Create, Issue.View, Issue.Manage,
        Overview.View,
    ];
}

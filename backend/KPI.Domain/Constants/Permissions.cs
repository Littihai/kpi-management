namespace KPI.Domain.Constants;

public static class Permissions
{
    public static class Kpi
    {
        public const string View     = "kpi.view";
        public const string Create   = "kpi.create";
        public const string Edit     = "kpi.edit";
        public const string Delete   = "kpi.delete";
        public const string Submit   = "kpi.submit";
        public const string Approve  = "kpi.approve";
        public const string Reject   = "kpi.reject";
        public const string Progress = "kpi.progress";
        public const string Close    = "kpi.close";
    }

    public static class Project
    {
        public const string View   = "project.view";
        public const string Create = "project.create";
        public const string Edit   = "project.edit";
        public const string Delete = "project.delete";
    }

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
    }

    public static class Department
    {
        public const string View   = "department.view";
        public const string Manage = "department.manage";
    }

    public static class Export
    {
        public const string View = "export.view";
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
        Kpi.View, Kpi.Create, Kpi.Edit, Kpi.Delete, Kpi.Submit, Kpi.Approve, Kpi.Reject, Kpi.Progress, Kpi.Close,
        Project.View, Project.Create, Project.Edit, Project.Delete,
        User.View, User.Create, User.Edit, User.Delete,
        Role.View,
        Department.View, Department.Manage,
        Export.View,
        Audit.View,
        Job.Trigger,
        Issue.Create, Issue.View, Issue.Manage,
        Overview.View,
    ];
}

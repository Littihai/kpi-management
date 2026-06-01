namespace KPI.Domain.Constants;

public static class Permissions
{
    public static class Kpi
    {
        public const string View   = "kpi.view";
        public const string Create = "kpi.create";
        public const string Edit   = "kpi.edit";
        public const string Delete = "kpi.delete";
        public const string Approve = "kpi.approve";
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

    public static class Department
    {
        public const string View   = "department.view";
        public const string Manage = "department.manage";
    }
}
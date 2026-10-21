using System.Reflection;
using KPI.API.Controllers;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace KPI.Tests;

/// <summary>
/// Guards against the original problem of endpoints that any logged-in user could call:
/// every action must be explicitly protected, with a policy that really exists.
/// </summary>
public class ControllerAuthorizationTests
{
    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (t, m)));

    private static string? PolicyOf(Type controller, MethodInfo action)
    {
        var onMethod = action.GetCustomAttributes<AuthorizeAttribute>().FirstOrDefault(a => a.Policy != null);
        var onClass = controller.GetCustomAttributes<AuthorizeAttribute>().FirstOrDefault(a => a.Policy != null);
        return (onMethod ?? onClass)?.Policy;
    }

    private static bool IsAnonymous(Type controller, MethodInfo action) =>
        action.GetCustomAttributes<AllowAnonymousAttribute>().Any()
        || controller.GetCustomAttributes<AllowAnonymousAttribute>().Any();

    private static bool IsAuthorized(Type controller, MethodInfo action) =>
        action.GetCustomAttributes<AuthorizeAttribute>().Any()
        || controller.GetCustomAttributes<AuthorizeAttribute>().Any();

    [Fact]
    public void Controllers_were_found()
    {
        Assert.True(Actions().Count() > 20);
    }

    [Fact]
    public void Every_endpoint_requires_authorization_except_login()
    {
        var unprotected = Actions()
            .Where(a => IsAnonymous(a.Controller, a.Action) || !IsAuthorized(a.Controller, a.Action))
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
            .ToList();

        // Login, and the two public endpoints that let someone who cannot sign in ask for help.
        Assert.Equal(
            new[]
            {
                $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
                $"{nameof(PublicController)}.{nameof(PublicController.Departments)}",
                $"{nameof(PublicController)}.{nameof(PublicController.ReportIssue)}",
            }.OrderBy(n => n),
            unprotected.OrderBy(n => n));
    }

    [Fact]
    public void Every_policy_used_is_a_real_permission()
    {
        foreach (var (controller, action) in Actions())
        {
            var policy = PolicyOf(controller, action);
            if (policy != null)
                Assert.True(Permissions.All.Contains(policy), $"{controller.Name}.{action.Name} uses unknown policy '{policy}'");
        }
    }

    [Fact]
    public void Only_a_few_read_endpoints_rely_on_authentication_alone()
    {
        var authOnly = Actions()
            .Where(a => IsAuthorized(a.Controller, a.Action) && !IsAnonymous(a.Controller, a.Action)
                        && PolicyOf(a.Controller, a.Action) == null)
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
            .ToList();

        // The caller's own profile and own password only; both re-check that the account is active.
        Assert.Equal(
            [$"{nameof(AuthController)}.{nameof(AuthController.ChangePassword)}", $"{nameof(AuthController)}.{nameof(AuthController.Me)}"],
            authOnly.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(typeof(UsersController), nameof(UsersController.Create), Permissions.User.Create)]
    [InlineData(typeof(UsersController), nameof(UsersController.Search), Permissions.User.View)]
    [InlineData(typeof(UsersController), nameof(UsersController.Update), Permissions.User.Edit)]
    [InlineData(typeof(UsersController), nameof(UsersController.Reactivate), Permissions.User.Edit)]
    [InlineData(typeof(UsersController), nameof(UsersController.ResetPassword), Permissions.User.Edit)]
    [InlineData(typeof(DepartmentsController), nameof(DepartmentsController.ListAll), Permissions.Department.Manage)]
    [InlineData(typeof(DepartmentsController), nameof(DepartmentsController.Create), Permissions.Department.Manage)]
    [InlineData(typeof(DepartmentsController), nameof(DepartmentsController.Update), Permissions.Department.Manage)]
    [InlineData(typeof(SettingsController), nameof(SettingsController.List), Permissions.Settings.View)]
    [InlineData(typeof(SettingsController), nameof(SettingsController.History), Permissions.Settings.View)]
    [InlineData(typeof(SettingsController), nameof(SettingsController.Update), Permissions.Settings.Manage)]
    [InlineData(typeof(SettingsController), nameof(SettingsController.Reset), Permissions.Settings.Manage)]
    [InlineData(typeof(RolesController), nameof(RolesController.Details), Permissions.Role.View)]
    [InlineData(typeof(RolesController), nameof(RolesController.Catalog), Permissions.Role.View)]
    [InlineData(typeof(RolesController), nameof(RolesController.Create), Permissions.Role.Manage)]
    [InlineData(typeof(RolesController), nameof(RolesController.Update), Permissions.Role.Manage)]
    [InlineData(typeof(RolesController), nameof(RolesController.Delete), Permissions.Role.Manage)]
    [InlineData(typeof(UsersController), nameof(UsersController.ChangeRole), Permissions.User.Edit)]
    [InlineData(typeof(UsersController), nameof(UsersController.Deactivate), Permissions.User.Edit)]
    [InlineData(typeof(AuthController), nameof(AuthController.Register), Permissions.User.Create)]
    [InlineData(typeof(JobsController), nameof(JobsController.TriggerMaintenance), Permissions.Job.Trigger)]
    [InlineData(typeof(AuditController), nameof(AuditController.Search), Permissions.Audit.View)]
    [InlineData(typeof(RolesController), nameof(RolesController.GetAll), Permissions.Role.View)]
    [InlineData(typeof(AdminController), nameof(AdminController.Overview), Permissions.Overview.View)]
    [InlineData(typeof(AdminController), nameof(AdminController.RequestLogs), Permissions.Overview.View)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Create), Permissions.Issue.Create)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Assign), Permissions.Issue.Manage)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Start), Permissions.Issue.Manage)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.RequestInfo), Permissions.Issue.Manage)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Resolve), Permissions.Issue.Manage)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Reject), Permissions.Issue.Manage)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.SetPriority), Permissions.Issue.Manage)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Assignees), Permissions.Issue.Manage)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.List), Permissions.Issue.View)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Get), Permissions.Issue.View)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Comment), Permissions.Issue.View)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Confirm), Permissions.Issue.View)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Reopen), Permissions.Issue.View)]
    [InlineData(typeof(IssuesController), nameof(IssuesController.Download), Permissions.Issue.View)]
    public void Sensitive_endpoints_require_their_specific_permission(Type controller, string actionName, string permission)
    {
        var action = controller.GetMethod(actionName)!;
        Assert.Equal(permission, PolicyOf(controller, action));
    }
}

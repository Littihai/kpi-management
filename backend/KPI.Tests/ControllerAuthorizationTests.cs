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
        typeof(KpiController).Assembly.GetTypes()
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

        Assert.Equal([$"{nameof(AuthController)}.{nameof(AuthController.Login)}"], unprotected);
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

        // /auth/me only: it returns the caller's own profile and re-checks that the account is active.
        Assert.Equal([$"{nameof(AuthController)}.{nameof(AuthController.Me)}"], authOnly);
    }

    [Theory]
    [InlineData(typeof(KpiController), nameof(KpiController.Create), Permissions.Kpi.Create)]
    [InlineData(typeof(KpiController), nameof(KpiController.Update), Permissions.Kpi.Edit)]
    [InlineData(typeof(KpiController), nameof(KpiController.Delete), Permissions.Kpi.Delete)]
    [InlineData(typeof(KpiController), nameof(KpiController.Submit), Permissions.Kpi.Submit)]
    [InlineData(typeof(KpiController), nameof(KpiController.Approve), Permissions.Kpi.Approve)]
    [InlineData(typeof(KpiController), nameof(KpiController.Reject), Permissions.Kpi.Reject)]
    [InlineData(typeof(KpiController), nameof(KpiController.Close), Permissions.Kpi.Close)]
    [InlineData(typeof(KpiController), nameof(KpiController.UpdateProgress), Permissions.Kpi.Progress)]
    [InlineData(typeof(UsersController), nameof(UsersController.Create), Permissions.User.Create)]
    [InlineData(typeof(UsersController), nameof(UsersController.ChangeRole), Permissions.User.Edit)]
    [InlineData(typeof(UsersController), nameof(UsersController.Deactivate), Permissions.User.Edit)]
    [InlineData(typeof(AuthController), nameof(AuthController.Register), Permissions.User.Create)]
    [InlineData(typeof(JobsController), nameof(JobsController.TriggerDelayDetection), Permissions.Job.Trigger)]
    [InlineData(typeof(AuditController), nameof(AuditController.GetLogs), Permissions.Audit.View)]
    [InlineData(typeof(RolesController), nameof(RolesController.GetAll), Permissions.Role.View)]
    [InlineData(typeof(ExportController), nameof(ExportController.ExportPdf), Permissions.Export.View)]
    [InlineData(typeof(ExportController), nameof(ExportController.ExportExcel), Permissions.Export.View)]
    public void Sensitive_endpoints_require_their_specific_permission(Type controller, string actionName, string permission)
    {
        var action = controller.GetMethod(actionName)!;
        Assert.Equal(permission, PolicyOf(controller, action));
    }
}

using KPI.Application.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace KPI.API.Realtime;

public class SignalRNotifier : IRealtimeNotifier
{
    public const string KpiChangedMethod = "KpiChanged";
    public const string IssueChangedMethod = "IssueChanged";
    public const string NotificationMethod = "Notification";

    private readonly IHubContext<KpiHub> _hub;

    public SignalRNotifier(IHubContext<KpiHub> hub)
    {
        _hub = hub;
    }

    public Task PublishKpiChangedAsync(KpiChangedEvent evt, CancellationToken ct = default)
    {
        // Everyone who may see this KPI: its department, the cross-department roles, and its owner.
        var groups = new[]
        {
            KpiHub.DepartmentGroup(evt.DepartmentId),
            KpiHub.AllDepartmentsGroup,
            KpiHub.UserGroup(evt.OwnerId),
        };
        return _hub.Clients.Groups(groups).SendAsync(KpiChangedMethod, evt, ct);
    }

    public Task PublishIssueChangedAsync(IssueChangedEvent evt, CancellationToken ct = default)
    {
        // The reporter, the assignee, the issue queue, directors/admins, and the department's managers.
        var groups = new List<string> { KpiHub.ItSupportGroup, KpiHub.AllDepartmentsGroup };
        if (evt.ReporterUserId is { } reporter) groups.Add(KpiHub.UserGroup(reporter));
        if (evt.AssigneeId is { } assignee) groups.Add(KpiHub.UserGroup(assignee));
        if (evt.DepartmentId is { } department) groups.Add(KpiHub.DepartmentManagersGroup(department));
        return _hub.Clients.Groups(groups.Distinct().ToList()).SendAsync(IssueChangedMethod, evt, ct);
    }

    public Task NotifyAsync(RealtimeNotification notification, RealtimeAudience audience, CancellationToken ct = default)
    {
        var groups = audience.UserIds.Select(KpiHub.UserGroup).ToList();
        if (audience.Approvers) groups.Add(KpiHub.AllDepartmentsGroup);
        if (audience.ItSupport) groups.Add(KpiHub.ItSupportGroup);
        if (audience.ManagersOfDepartment is { } departmentId)
            groups.Add(KpiHub.DepartmentManagersGroup(departmentId));

        if (groups.Count == 0) return Task.CompletedTask;
        return _hub.Clients.Groups(groups.Distinct().ToList()).SendAsync(NotificationMethod, notification, ct);
    }
}

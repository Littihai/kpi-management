using KPI.Application.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace KPI.API.Realtime;

public class SignalRNotifier : IRealtimeNotifier
{
    public const string IssueChangedMethod = "IssueChanged";
    public const string NotificationMethod = "Notification";

    private readonly IHubContext<KpiHub> _hub;

    public SignalRNotifier(IHubContext<KpiHub> hub)
    {
        _hub = hub;
    }

    public Task PublishIssueChangedAsync(IssueChangedEvent evt, CancellationToken ct = default)
    {
        if (evt.StaffOnly)
        {
            var staff = new List<string> { KpiHub.ItSupportGroup };
            if (evt.AssigneeId is { } owner) staff.Add(KpiHub.UserGroup(owner));
            return _hub.Clients.Groups(staff.Distinct().ToList()).SendAsync(IssueChangedMethod, evt, ct);
        }

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
        if (audience.ItSupport) groups.Add(KpiHub.ItSupportGroup);

        if (groups.Count == 0) return Task.CompletedTask;
        return _hub.Clients.Groups(groups.Distinct().ToList()).SendAsync(NotificationMethod, notification, ct);
    }
}

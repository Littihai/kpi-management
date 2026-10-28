namespace KPI.Application.Notifications;

/// <summary>
/// Told after a save that changed what some people may see in real time: a person was deactivated, given another role or
/// department, or the rights of a role changed. The real-time hub uses it to take live connections out of groups they no
/// longer belong in, so a change takes effect at once instead of when the connection happens to drop.
/// </summary>
public interface IAccessChangeListener
{
    /// <param name="userIds">People whose own access changed.</param>
    /// <param name="roleIds">Roles whose rights changed; everyone holding one is affected.</param>
    Task AccessChangedAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);
}

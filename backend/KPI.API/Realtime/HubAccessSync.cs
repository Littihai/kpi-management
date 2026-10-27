using KPI.Application.Notifications;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Realtime;

/// <summary>
/// Keeps live connections in step with what people may see. A connection is put into groups when it opens; without this
/// a person who is deactivated, demoted or moved would keep hearing about issues until their connection happened to
/// drop, although every normal request already refuses them. Each affected connection gets the groups the person has
/// now, and the connections of someone who can no longer sign in are closed.
///
/// Everything that decides groups (a connection opening, an access change) runs one at a time and reads the groups from
/// the database inside that turn. So whichever runs last has read the newest data, and an older computation can never
/// be applied after a newer one.
/// </summary>
public sealed class HubAccessSync : IAccessChangeListener
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly HubConnectionRegistry _registry;
    private readonly IHubContext<KpiHub> _hub;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<HubAccessSync> _logger;

    public HubAccessSync(
        HubConnectionRegistry registry, IHubContext<KpiHub> hub, IServiceScopeFactory scopes, ILogger<HubAccessSync> logger)
    {
        _registry = registry;
        _hub = hub;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>
    /// Puts a newly opened (and already registered) connection into the groups its person belongs in. Returns the person,
    /// or null when they do not exist or are deactivated; such a connection stays open, in no group, and is picked up
    /// if the person is reactivated later.
    /// </summary>
    public async Task<User?> AttachAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        await _turn.WaitAsync(cancellationToken);
        try
        {
            if (!_registry.TryGet(connectionId, out var connection)) return null; // it dropped already

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (user, groups) = await HubGroups.ForUserAsync(db, connection.UserId, cancellationToken);
            await ApplyAsync(connectionId, connection, groups, close: false, cancellationToken);
            return user;
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task AccessChangedAsync(
        IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        if (_registry.IsEmpty) return; // nobody is connected to this process: nothing to move

        await _turn.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var affected = new HashSet<Guid>(userIds);
            if (roleIds.Count > 0)
            {
                var connected = _registry.ConnectedUserIds();
                affected.UnionWith(await db.Users.AsNoTracking()
                    .Where(u => connected.Contains(u.Id) && roleIds.Contains(u.RoleId))
                    .Select(u => u.Id)
                    .ToListAsync(cancellationToken));
            }

            foreach (var userId in affected)
            {
                try
                {
                    var connections = _registry.ForUser(userId);
                    if (connections.Count == 0) continue;

                    var (user, groups) = await HubGroups.ForUserAsync(db, userId, cancellationToken);
                    foreach (var (connectionId, connection) in connections)
                        await ApplyAsync(connectionId, connection, groups, close: user is null, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One person's failure (a database hiccup) must not leave everyone after them with their old access.
                    _logger.LogWarning(ex, "Could not update the live connections of user {UserId}", userId);
                }
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task ApplyAsync(
        string connectionId, HubConnectionRegistry.Connection connection, List<string> groups, bool close, CancellationToken cancellationToken)
    {
        try
        {
            var (leave, join) = connection.SetGroups(groups);

            // Leaving first, so access is taken away before anything new is given.
            foreach (var group in leave)
                await _hub.Groups.RemoveFromGroupAsync(connectionId, group, cancellationToken);
            foreach (var group in join)
                await _hub.Groups.AddToGroupAsync(connectionId, group, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A connection that just dropped is fine; anything else must not stop the others being updated.
            _logger.LogWarning(ex, "Could not update the groups of connection {ConnectionId}", connectionId);
        }
        finally
        {
            if (close) connection.Context.Abort(); // even when a group call failed: this person must not stay connected
        }
    }
}

using System.Collections.Concurrent;
using KPI.Application.Admin;

namespace KPI.API.Realtime;

/// <summary>Who has a real-time connection right now (in this process). Feeds the "online now" list for SuperAdmin.</summary>
public sealed class PresenceTracker : IPresenceTracker
{
    private sealed record Entry(Guid UserId, string Name, string Role, string? Ip, DateTime Since);

    private readonly ConcurrentDictionary<string, Entry> _connections = new();

    public void Connected(string connectionId, Guid userId, string name, string role, string? ip) =>
        _connections[connectionId] = new Entry(userId, name, role, ip, DateTime.UtcNow);

    public void Disconnected(string connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlyList<OnlineUserDto> Snapshot() =>
        _connections.Values
            .GroupBy(e => e.UserId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.Since).First();
                return new OnlineUserDto(g.Key, latest.Name, latest.Role, latest.Ip, g.Count(), g.Min(e => e.Since));
            })
            .OrderBy(o => o.Name)
            .ToList();
}

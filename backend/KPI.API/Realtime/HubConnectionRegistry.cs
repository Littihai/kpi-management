using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace KPI.API.Realtime;

/// <summary>
/// The live hub connections of this process and the groups each one was put in, so that when someone's access changes
/// their connections can be moved out of groups they no longer belong in (or closed).
/// </summary>
public sealed class HubConnectionRegistry
{
    public sealed class Connection
    {
        private readonly object _gate = new();
        private HashSet<string> _groups;

        internal Connection(Guid userId, HubCallerContext context)
        {
            UserId = userId;
            Context = context;
            _groups = [];
        }

        public Guid UserId { get; }
        public HubCallerContext Context { get; }

        /// <summary>Makes the recorded groups equal <paramref name="wanted"/> and says what has to be left and what joined.</summary>
        public (List<string> Leave, List<string> Join) SetGroups(IEnumerable<string> wanted)
        {
            var next = new HashSet<string>(wanted);
            lock (_gate)
            {
                var leave = _groups.Where(g => !next.Contains(g)).ToList();
                var join = next.Where(g => !_groups.Contains(g)).ToList();
                _groups = next;
                return (leave, join);
            }
        }
    }

    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public bool IsEmpty => _connections.IsEmpty;

    /// <summary>Records a new connection, in no group yet; <see cref="HubAccessSync.AttachAsync"/> puts it into its groups.</summary>
    public void Add(string connectionId, Guid userId, HubCallerContext context) =>
        _connections[connectionId] = new Connection(userId, context);

    public bool TryGet(string connectionId, out Connection connection) => _connections.TryGetValue(connectionId, out connection!);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlyList<Guid> ConnectedUserIds() => _connections.Values.Select(c => c.UserId).Distinct().ToList();

    public IReadOnlyList<KeyValuePair<string, Connection>> ForUser(Guid userId) =>
        _connections.Where(c => c.Value.UserId == userId).ToList();
}

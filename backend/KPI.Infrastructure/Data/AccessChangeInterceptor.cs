using System.Runtime.CompilerServices;
using KPI.Application.Notifications;
using KPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Data;

/// <summary>
/// Notices saves that change who may see what in real time and tells <see cref="IAccessChangeListener"/> once the save
/// has succeeded. Watching the save, rather than each place that edits a person or a role, means no path can be forgotten.
/// It never fails the save: if telling the listener goes wrong, the change is already stored and a warning is logged.
/// </summary>
public sealed class AccessChangeInterceptor : SaveChangesInterceptor
{
    private static readonly TimeSpan ListenerTimeout = TimeSpan.FromSeconds(30);

    private sealed class Pending
    {
        public HashSet<Guid> Users { get; } = [];
        public HashSet<Guid> Roles { get; } = [];
        public bool Any => Users.Count + Roles.Count > 0;
    }

    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();
    private readonly IAccessChangeListener _listener;
    private readonly ILogger<AccessChangeInterceptor> _logger;

    public AccessChangeInterceptor(IAccessChangeListener listener, ILogger<AccessChangeInterceptor> logger)
    {
        _listener = listener;
        _logger = logger;
    }

    // ───────────── before the save: what is about to change ─────────────

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        _pending.Remove(context);

        var pending = new Pending();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case User user when entry.State == EntityState.Modified:
                    if (entry.Property(nameof(User.IsActive)).IsModified
                        || entry.Property(nameof(User.RoleId)).IsModified
                        || entry.Property(nameof(User.DepartmentId)).IsModified)
                        pending.Users.Add(user.Id);
                    break;

                case User user when entry.State == EntityState.Deleted:
                    pending.Users.Add(user.Id);
                    break;

                case Role role when entry.State == EntityState.Modified:
                    if (entry.Property(nameof(Role.SeesAllDepartments)).IsModified
                        || entry.Property(nameof(Role.IssueVisibility)).IsModified)
                        pending.Roles.Add(role.Id);
                    break;

                case RolePermission when entry.State is EntityState.Added or EntityState.Deleted:
                    if (entry.Property(nameof(RolePermission.RoleId)).CurrentValue is Guid roleId && roleId != Guid.Empty)
                        pending.Roles.Add(roleId);
                    break;
            }
        }

        if (pending.Any) _pending.Add(context, pending);
    }

    // ───────────── after the save: tell the listener ─────────────

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (Take(eventData.Context) is { } pending)
            NotifyAsync(pending).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (Take(eventData.Context) is { } pending)
            await NotifyAsync(pending);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Take(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Take(eventData.Context);
        return Task.CompletedTask;
    }

    private Pending? Take(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return null;
        _pending.Remove(context);
        return pending;
    }

    private async Task NotifyAsync(Pending pending)
    {
        // Not tied to the request: a client that gives up must not leave a deactivated person connected.
        using var timeout = new CancellationTokenSource(ListenerTimeout);
        try
        {
            await _listener.AccessChangedAsync(pending.Users, pending.Roles, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update live connections after an access change ({Users} people, {Roles} roles)",
                pending.Users.Count, pending.Roles.Count);
        }
    }
}

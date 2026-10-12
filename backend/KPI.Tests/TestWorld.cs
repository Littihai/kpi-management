using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Enums;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Repositories;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class RecordingNotifier : IRealtimeNotifier
{
    public List<KpiChangedEvent> Events { get; } = [];
    public List<(RealtimeNotification Notification, RealtimeAudience Audience)> Notifications { get; } = [];
    public bool Throw { get; set; }

    public Task PublishKpiChangedAsync(KpiChangedEvent evt, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("hub is down");
        Events.Add(evt);
        return Task.CompletedTask;
    }

    public Task NotifyAsync(RealtimeNotification notification, RealtimeAudience audience, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("hub is down");
        Notifications.Add((notification, audience));
        return Task.CompletedTask;
    }
}

public class RecordingEmail : IEmailService
{
    public List<string> DelayedTo { get; } = [];
    public bool Throw { get; set; }

    public Task SendKpiDelayedAsync(string toEmail, string fullName, string kpiName, int daysDelayed)
    {
        if (Throw) throw new InvalidOperationException("smtp down");
        DelayedTo.Add(toEmail);
        return Task.CompletedTask;
    }

    public Task SendKpiApprovedAsync(string toEmail, string fullName, string kpiName) => Task.CompletedTask;
    public Task SendTaskAssignedAsync(string toEmail, string fullName, string taskName) => Task.CompletedTask;
    public Task SendKpiCreatedAsync(string toEmail, string managerName, string kpiName, string ownerName) => Task.CompletedTask;
    public Task SendKpiSubmittedAsync(string toEmail, string directorName, string kpiName, string ownerName) => Task.CompletedTask;
}

/// <summary>An in-memory database with two departments and one user per role.</summary>
public class TestWorld
{
    public AppDbContext Db { get; }
    public DbContextOptions<AppDbContext> Options { get; }
    public RecordingNotifier Notifier { get; } = new();
    public KpiEventPublisher Events { get; }
    public KpiService Service { get; }

    public Department DeptA { get; } = new() { Name = "Dept A", Code = "A" };
    public Department DeptB { get; } = new() { Name = "Dept B", Code = "B" };

    public User SuperAdmin { get; private set; } = null!;
    public User Director { get; private set; } = null!;
    public User ManagerA { get; private set; } = null!;
    public User ManagerB { get; private set; } = null!;
    public User TeamLeaderA { get; private set; } = null!;
    public User StaffA { get; private set; } = null!;
    public User ViewerA { get; private set; } = null!;
    public User NoDepartment { get; private set; } = null!;

    private TestWorld()
    {
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new AppDbContext(Options);
        Events = new KpiEventPublisher(Notifier, NullLogger<KpiEventPublisher>.Instance);
        Service = new KpiService(new KpiRepository(Db), new UserRepository(Db), Db, Events);
    }

    public static async Task<TestWorld> CreateAsync()
    {
        var world = new TestWorld();
        await world.SeedAsync();
        return world;
    }

    private async Task SeedAsync()
    {
        await DbSeeder.SeedAsync(Db);
        Db.Departments.AddRange(DeptA, DeptB);
        await Db.SaveChangesAsync();

        SuperAdmin = await AddUserAsync("admin", RoleNames.SuperAdmin, null);
        Director = await AddUserAsync("director", RoleNames.Director, null);
        ManagerA = await AddUserAsync("manager-a", RoleNames.Manager, DeptA.Id);
        ManagerB = await AddUserAsync("manager-b", RoleNames.Manager, DeptB.Id);
        TeamLeaderA = await AddUserAsync("lead-a", RoleNames.TeamLeader, DeptA.Id);
        StaffA = await AddUserAsync("staff-a", RoleNames.Staff, DeptA.Id);
        ViewerA = await AddUserAsync("viewer-a", RoleNames.Viewer, DeptA.Id);
        NoDepartment = await AddUserAsync("nodept", RoleNames.Manager, null);
    }

    public async Task<User> AddUserAsync(string name, string roleName, Guid? departmentId, bool active = true)
    {
        var role = await Db.Roles.FirstAsync(r => r.Name == roleName);
        var user = new User
        {
            FirstName = name,
            LastName = "Tester",
            Email = $"{name}@example.com",
            PasswordHash = "x",
            RoleId = role.Id,
            DepartmentId = departmentId,
            IsActive = active,
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    /// <summary>Inserts a KPI directly (bypassing the service) so tests can start from any status.</summary>
    public async Task<Kpi> AddKpiAsync(
        KpiStatus status,
        User owner,
        Department? department = null,
        decimal progress = 0,
        decimal target = 100,
        DateTime? start = null,
        DateTime? due = null)
    {
        var kpi = new Kpi
        {
            Name = $"KPI {Guid.NewGuid():N}"[..12],
            Description = "",
            Year = 2026,
            Target = target,
            Progress = progress,
            Status = status,
            StartDate = start ?? DateTime.UtcNow.AddDays(-10),
            DueDate = due ?? DateTime.UtcNow.AddDays(90),
            DepartmentId = (department ?? DeptA).Id,
            OwnerId = owner.Id,
        };
        Db.Kpis.Add(kpi);
        await Db.SaveChangesAsync();
        return kpi;
    }

    public void Clear() => Db.ChangeTracker.Clear();
}

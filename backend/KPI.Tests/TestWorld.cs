using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Issues;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Repositories;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class RecordingNotifier : IRealtimeNotifier
{
    public List<IssueChangedEvent> IssueEvents { get; } = [];
    public List<(RealtimeNotification Notification, RealtimeAudience Audience)> Notifications { get; } = [];
    public bool Throw { get; set; }

    public Task PublishIssueChangedAsync(IssueChangedEvent evt, CancellationToken ct = default)
    {
        if (Throw) throw new InvalidOperationException("hub is down");
        IssueEvents.Add(evt);
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
    public List<IssueEmailMessage> IssueSent { get; } = [];
    public bool Throw { get; set; }

    public IEnumerable<string> To(IssueEmailKind kind) => IssueSent.Where(m => m.Kind == kind).Select(m => m.ToEmail);

    public List<string> TestSent { get; } = [];

    public Task SendIssueAsync(IssueEmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Throw) throw new InvalidOperationException("mail provider down");
        IssueSent.Add(message);
        return Task.CompletedTask;
    }

    public Task SendTestAsync(string toEmail, CancellationToken cancellationToken = default)
    {
        if (Throw) throw new InvalidOperationException("mail provider down");
        TestSent.Add(toEmail);
        return Task.CompletedTask;
    }

}

/// <summary>A clock tests can move forward, so deadlines and auto-close can be checked without waiting.</summary>
public class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 17, 7, 30, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
    public DateTime UtcNow => _now.UtcDateTime;
}

/// <summary>An in-memory database with two departments and one user per role.</summary>
public class TestWorld
{
    public AppDbContext Db { get; }
    public DbContextOptions<AppDbContext> Options { get; }
    public RecordingNotifier Notifier { get; } = new();
    public RecordingEmail Email { get; } = new();
    public IssueService Issues { get; }
    public TestClock Clock { get; } = new();
    public DefaultSettings Settings { get; }

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
    public User ItA { get; private set; } = null!;
    public User ItB { get; private set; } = null!;
    public User StaffB { get; private set; } = null!;

    private TestWorld(bool emailEnabled)
    {
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new AppDbContext(Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:PacingMs"] = "0",
            ["Email:Enabled"] = emailEnabled ? "true" : "false",
        }).Build();
        Settings = new DefaultSettings();
        var issueEvents = new IssueEventPublisher(Notifier, Db, Email, config, Settings, NullLogger<IssueEventPublisher>.Instance);
        Issues = new IssueService(Db, new UserRepository(Db), issueEvents, Settings, Clock);
    }

    public static async Task<TestWorld> CreateAsync(bool emailEnabled = true)
    {
        var world = new TestWorld(emailEnabled);
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
        ItA = await AddUserAsync("it-a", RoleNames.ITSupport, DeptB.Id);
        ItB = await AddUserAsync("it-b", RoleNames.ITSupport, DeptB.Id);
        StaffB = await AddUserAsync("staff-b", RoleNames.Staff, DeptB.Id);
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

    public void Clear() => Db.ChangeTracker.Clear();
}

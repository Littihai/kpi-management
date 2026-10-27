using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class UserRepositoryTests
{
    private sealed class CountingListener : IAccessChangeListener
    {
        public int Calls { get; private set; }
        public void Reset() => Calls = 0;

        public Task AccessChangedAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private static async Task<(DbContextOptions<AppDbContext> Options, CountingListener Listener, Guid UserId)> CreateAsync()
    {
        var listener = new CountingListener();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AccessChangeInterceptor(listener, NullLogger<AccessChangeInterceptor>.Instance))
            .Options;

        await using var db = new AppDbContext(options);
        await DbSeeder.SeedAsync(db);
        var user = new User
        {
            FirstName = "Somchai", LastName = "Tester", Email = "somchai@example.com", PasswordHash = "x", IsActive = true,
            RoleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.Staff)).Id,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        listener.Reset(); // setting the data up is not the thing under test
        return (options, listener, user.Id);
    }

    [Fact]
    public async Task Saving_a_failed_sign_in_does_not_undo_a_deactivation_made_in_the_meantime()
    {
        var (options, _, userId) = await CreateAsync();

        // The sign-in loads the person, then spends a moment checking the password ...
        await using var signIn = new AppDbContext(options);
        var repository = new UserRepository(signIn);
        var loaded = await repository.GetByIdAsync(userId);
        Assert.True(loaded!.IsActive);

        // ... during which an administrator deactivates them.
        await using (var admin = new AppDbContext(options))
        {
            (await admin.Users.FirstAsync(u => u.Id == userId)).IsActive = false;
            await admin.SaveChangesAsync();
        }

        loaded.FailedLoginCount++;
        await repository.UpdateAsync(loaded);

        await using var check = new AppDbContext(options);
        var stored = await check.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.False(stored.IsActive);             // still deactivated
        Assert.Equal(1, stored.FailedLoginCount);  // and the count was saved
    }

    [Fact]
    public async Task Saving_a_failed_sign_in_does_not_report_an_access_change()
    {
        var (options, listener, userId) = await CreateAsync();
        await using var db = new AppDbContext(options);
        var repository = new UserRepository(db);
        var loaded = (await repository.GetByIdAsync(userId))!;

        loaded.FailedLoginCount = 4;
        loaded.LockedUntil = DateTime.UtcNow.AddMinutes(15);
        await repository.UpdateAsync(loaded);

        Assert.Equal(0, listener.Calls);
    }

    [Fact]
    public async Task A_person_who_was_not_loaded_through_the_context_is_still_saved_in_full()
    {
        var (options, _, userId) = await CreateAsync();
        User detached;
        await using (var read = new AppDbContext(options))
            detached = await read.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        detached.FirstName = "Renamed";
        detached.IsActive = false;
        await using (var db = new AppDbContext(options))
            await new UserRepository(db).UpdateAsync(detached);

        await using var check = new AppDbContext(options);
        var stored = await check.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.Equal("Renamed", stored.FirstName);
        Assert.False(stored.IsActive);
    }
}

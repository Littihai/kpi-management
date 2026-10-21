using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

public class InitialAdminTests
{
    private const string GoodPassword = "Bootstrap1!";

    private static async Task<AppDbContext> EmptyDatabaseAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DbSeeder.SeedAsync(db);
        return db;
    }

    private static async Task AddUserAsync(AppDbContext db, string email, string role, bool active = true)
    {
        var roleId = (await db.Roles.FirstAsync(r => r.Name == role)).Id;
        db.Users.Add(new User { FirstName = "x", LastName = "y", Email = email, PasswordHash = "x", RoleId = roleId, IsActive = active });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Creates_an_active_SuperAdmin_with_a_hashed_password_and_no_department()
    {
        await using var db = await EmptyDatabaseAsync();

        var created = await DbSeeder.SeedInitialAdminAsync(db, "  Boss@Example.com ", GoodPassword, "Ada", "Lovelace");

        Assert.True(created);
        var admin = await db.Users.Include(u => u.Role).SingleAsync();
        Assert.Equal("boss@example.com", admin.Email);
        Assert.Equal(RoleNames.SuperAdmin, admin.Role.Name);
        Assert.Equal("Ada", admin.FirstName);
        Assert.Equal("Lovelace", admin.LastName);
        Assert.True(admin.IsActive);
        Assert.Null(admin.DepartmentId);
        Assert.NotEqual(GoodPassword, admin.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(GoodPassword, admin.PasswordHash));
    }

    [Fact]
    public async Task Uses_default_names_when_none_are_given()
    {
        await using var db = await EmptyDatabaseAsync();
        await DbSeeder.SeedInitialAdminAsync(db, "boss@example.com", GoodPassword);

        var admin = await db.Users.SingleAsync();
        Assert.Equal("System", admin.FirstName);
        Assert.Equal("Admin", admin.LastName);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", null)]
    public async Task Does_nothing_when_the_settings_are_empty(string? email, string? password)
    {
        await using var db = await EmptyDatabaseAsync();

        Assert.False(await DbSeeder.SeedInitialAdminAsync(db, email, password));
        Assert.Empty(db.Users);
    }

    [Fact]
    public async Task Running_twice_creates_only_one_account()
    {
        await using var db = await EmptyDatabaseAsync();

        Assert.True(await DbSeeder.SeedInitialAdminAsync(db, "boss@example.com", GoodPassword));
        Assert.False(await DbSeeder.SeedInitialAdminAsync(db, "boss@example.com", GoodPassword));
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task Does_nothing_when_an_active_SuperAdmin_exists_even_if_the_settings_are_wrong()
    {
        // A healthy deployment must not crash because stale Admin__* values were left behind.
        await using var db = await EmptyDatabaseAsync();
        await AddUserAsync(db, "existing@example.com", RoleNames.SuperAdmin);

        Assert.False(await DbSeeder.SeedInitialAdminAsync(db, "not-an-email", "short"));
        Assert.False(await DbSeeder.SeedInitialAdminAsync(db, "other@example.com", GoodPassword));
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task Recreates_an_admin_when_every_SuperAdmin_is_deactivated()
    {
        await using var db = await EmptyDatabaseAsync();
        await AddUserAsync(db, "old@example.com", RoleNames.SuperAdmin, active: false);

        Assert.True(await DbSeeder.SeedInitialAdminAsync(db, "new@example.com", GoodPassword));
        Assert.Equal(2, db.Users.Count());
    }

    [Fact]
    public async Task A_non_admin_user_with_the_same_email_is_not_silently_promoted()
    {
        await using var db = await EmptyDatabaseAsync();
        await AddUserAsync(db, "boss@example.com", RoleNames.Staff);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DbSeeder.SeedInitialAdminAsync(db, "BOSS@example.com", GoodPassword));

        Assert.Contains("Admin:Email", ex.Message);
        Assert.Equal(RoleNames.Staff, (await db.Users.Include(u => u.Role).SingleAsync()).Role.Name);
    }

    [Theory]
    [InlineData("", "email")]
    [InlineData("no-at-sign", "email")]
    public async Task Rejects_an_unusable_email(string email, string expected)
    {
        await using var db = await EmptyDatabaseAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DbSeeder.SeedInitialAdminAsync(db, email == "" ? " " : email, GoodPassword));

        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Users);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public async Task Rejects_a_password_that_breaks_the_policy(string? password)
    {
        await using var db = await EmptyDatabaseAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DbSeeder.SeedInitialAdminAsync(db, "boss@example.com", password));

        Assert.Contains("Admin:Password", ex.Message);
        Assert.Empty(db.Users);
    }
}

/// <summary>An app started on an empty database with the Admin__* settings, like a first deploy.</summary>
public class FirstDeployApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Admin:Email", "Boss@Example.com");
        builder.UseSetting("Admin:Password", "Bootstrap1!");
        builder.UseSetting("Admin:FirstName", "Ada");
        builder.UseSetting("Admin:LastName", "Lovelace");
    }
}

public class FirstDeployTests : IClassFixture<FirstDeployApiFactory>
{
    private readonly FirstDeployApiFactory _factory;

    public FirstDeployTests(FirstDeployApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_configured_admin_can_sign_in_on_a_new_database_and_holds_every_permission()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "boss@example.com", password = "Bootstrap1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(RoleNames.SuperAdmin, body.GetProperty("role").GetString());
        Assert.Equal("Ada Lovelace", body.GetProperty("fullName").GetString());

        var permissions = body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToHashSet();
        Assert.Contains(Permissions.User.Create, permissions);
        Assert.Contains(Permissions.Issue.Manage, permissions);
    }
}

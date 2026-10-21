using KPI.Application.Auth;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Startup;

/// <summary>
/// <c>KPI.API create-admin</c>: creates the first administrator on a new installation and exits, without starting the
/// web server. The details come from environment variables (not arguments) so the password never shows in a process list:
/// HELPDESK_ADMIN_EMAIL, HELPDESK_ADMIN_PASSWORD, HELPDESK_ADMIN_FIRST_NAME, HELPDESK_ADMIN_LAST_NAME.
/// Exit codes: 0 done (or an administrator already exists), 2 something missing, 3 refused (e-mail or password), 4 e-mail already taken.
/// </summary>
public static class AdminCommand
{
    public const string Name = "create-admin";

    public static async Task<int> RunAsync(IServiceProvider services, Func<string, string?> env, TextWriter output)
    {
        var email = AuthService.NormalizeEmail(env("HELPDESK_ADMIN_EMAIL"));
        var password = env("HELPDESK_ADMIN_PASSWORD");
        var first = (env("HELPDESK_ADMIN_FIRST_NAME") ?? string.Empty).Trim();
        var last = (env("HELPDESK_ADMIN_LAST_NAME") ?? string.Empty).Trim();

        if (email.Length == 0 || string.IsNullOrEmpty(password) || first.Length == 0)
        {
            await output.WriteLineAsync("Missing HELPDESK_ADMIN_EMAIL, HELPDESK_ADMIN_PASSWORD or HELPDESK_ADMIN_FIRST_NAME.");
            return 2;
        }
        if (!email.Contains('@') || email.Contains(' '))
        {
            await output.WriteLineAsync("The e-mail address is not valid.");
            return 3;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational()) await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);

        if (await db.Users.AnyAsync(u => u.IsActive && u.Role.Name == RoleNames.SuperAdmin))
        {
            await output.WriteLineAsync("An administrator already exists, so none was created. Sign in with that account.");
            return 0;
        }
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            await output.WriteLineAsync("That e-mail address already belongs to a user who is not an administrator. Use another address.");
            return 4;
        }

        var user = new User
        {
            FirstName = first,
            LastName = last,
            Email = email,
            RoleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.SuperAdmin)).Id,
            IsActive = true,
        };
        try
        {
            // The same rules as any other password: the policy in the settings (at least 8 characters unless changed).
            await scope.ServiceProvider.GetRequiredService<IPasswordService>().PrepareNewAsync(user, password);
        }
        catch (RequestValidationException ex)
        {
            await output.WriteLineAsync("The password was refused: " + ex.Message);
            return 3;
        }
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await output.WriteLineAsync($"Created the administrator {email}.");
        return 0;
    }
}

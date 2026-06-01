using KPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        if (await context.Roles.AnyAsync()) return;

        var roles = new List<Role>
        {
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "SuperAdmin",  Description = "Full system access" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Director",    Description = "View all departments" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "Manager",     Description = "Manage department KPI" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000004"), Name = "TeamLeader",  Description = "Manage projects" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000005"), Name = "Staff",       Description = "Update assigned tasks" },
            new() { Id = Guid.Parse("00000000-0000-0000-0000-000000000006"), Name = "Viewer",      Description = "Read only" },
        };

        context.Roles.AddRange(roles);
        await context.SaveChangesAsync();
    }
}
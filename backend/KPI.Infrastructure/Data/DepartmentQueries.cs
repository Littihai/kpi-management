using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Data;

public record DepartmentOption(Guid Id, string Name);

public static class DepartmentQueries
{
    /// <summary>Active departments by name, as id and name for a dropdown.</summary>
    public static Task<List<DepartmentOption>> ListActiveAsync(this AppDbContext db) =>
        db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentOption(d.Id, d.Name))
            .ToListAsync();
}

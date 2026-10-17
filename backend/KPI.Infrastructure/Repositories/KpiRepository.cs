using KPI.Domain.Entities;
using KPI.Domain.Interfaces;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Repositories;

public class KpiRepository : IKpiRepository
{
    private readonly AppDbContext _context;

    public KpiRepository(AppDbContext context)
    {
        _context = context;
    }

    // Lists are read-only, so skip change tracking.
    public async Task<List<Kpi>> GetAllAsync()
        => await _context.Kpis
            .AsNoTracking()
            .Include(k => k.Department)
            .Include(k => k.Owner)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

    public async Task<List<Kpi>> GetByDepartmentAsync(Guid departmentId)
        => await _context.Kpis
            .AsNoTracking()
            .Include(k => k.Department)
            .Include(k => k.Owner)
            .Where(k => k.DepartmentId == departmentId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

    public async Task<Kpi?> GetByIdAsync(Guid id)
        => await _context.Kpis
            .Include(k => k.Department)
            .Include(k => k.Owner)
            .FirstOrDefaultAsync(k => k.Id == id);

    public async Task<Kpi> CreateAsync(Kpi kpi)
    {
        _context.Kpis.Add(kpi);
        await _context.SaveChangesAsync();
        return kpi;
    }

    public async Task<Kpi> UpdateAsync(Kpi kpi)
    {
        // The entity is tracked (loaded through this context), so EF only writes the columns that
        // changed. Version is the concurrency token: a second writer holding the old value fails.
        kpi.UpdatedAt = DateTime.UtcNow;
        kpi.Version++;
        await _context.SaveChangesAsync();
        return kpi;
    }

    public async Task DeleteAsync(Kpi kpi)
    {
        _context.Kpis.Remove(kpi);
        await _context.SaveChangesAsync();
    }

    public async Task AddProgressLogAsync(KpiProgressLog log)
    {
        _context.KpiProgressLogs.Add(log);
        await _context.SaveChangesAsync();
    }
}

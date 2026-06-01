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

    public async Task<List<Kpi>> GetAllAsync()
        => await _context.Kpis
            .Include(k => k.Department)
            .Include(k => k.Owner)
            .Include(k => k.Projects)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();

    public async Task<List<Kpi>> GetByDepartmentAsync(Guid departmentId)
        => await _context.Kpis
            .Include(k => k.Department)
            .Include(k => k.Owner)
            .Where(k => k.DepartmentId == departmentId)
            .ToListAsync();

    public async Task<Kpi?> GetByIdAsync(Guid id)
        => await _context.Kpis
            .Include(k => k.Department)
            .Include(k => k.Owner)
            .Include(k => k.Projects)
            .Include(k => k.ProgressLogs)
            .FirstOrDefaultAsync(k => k.Id == id);

    public async Task<Kpi> CreateAsync(Kpi kpi)
    {
        _context.Kpis.Add(kpi);
        await _context.SaveChangesAsync();
        return kpi;
    }

    public async Task<Kpi> UpdateAsync(Kpi kpi)
    {
        kpi.UpdatedAt = DateTime.UtcNow;
        _context.Kpis.Update(kpi);
        await _context.SaveChangesAsync();
        return kpi;
    }
	public async Task AddProgressLogAsync(KpiProgressLog log)
	{
		_context.KpiProgressLogs.Add(log);
		await _context.SaveChangesAsync();
	}
}
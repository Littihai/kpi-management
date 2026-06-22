using KPI.Application.Dashboard;
using KPI.Domain.Enums;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid userId)
{
    var user = await _context.Users
        .Include(u => u.Role)
        .FirstOrDefaultAsync(u => u.Id == userId);

    var fullAccessRoles = new[] { "SuperAdmin", "Director" };

    var kpisQuery = _context.Kpis
        .Include(k => k.Department)
        .Include(k => k.ProgressLogs)
        .AsQueryable();

    // Filter ตาม Department
    if (user?.Role?.Name == null || !fullAccessRoles.Contains(user.Role.Name))
    {
        kpisQuery = kpisQuery.Where(k => k.DepartmentId == user!.DepartmentId);
    }

    var kpis = await kpisQuery.ToListAsync();

    // คำนวณ metrics เหมือนเดิม
    var totalKpis = kpis.Count;
    var activeKpis = kpis.Count(k => k.Status == KpiStatus.Active);
    var delayedKpis = kpis.Count(k => k.Status == KpiStatus.Delayed);
    var onTrackKpis = kpis.Count(k => k.Status == KpiStatus.Active || k.Status == KpiStatus.Completed);
    var overallProgress = kpis.Any()
        ? Math.Round(kpis.Average(k => (double)k.Progress), 1)
        : 0;

    var deptRankings = kpis
        .GroupBy(k => k.Department?.Name ?? "Unknown")
        .Select(g => new DepartmentRankingDto(
            DepartmentName: g.Key,
            AverageProgress: Math.Round(g.Average(k => (double)k.Progress), 1),
            TotalKpis: g.Count(),
            TrafficLight: GetTrafficLight(g.Average(k => (double)k.Progress))
        ))
        .OrderByDescending(d => d.AverageProgress)
        .ToList();

    var recentKpis = kpis
        .OrderByDescending(k => k.UpdatedAt)
        .Take(5)
        .Select(k => new KpiSummaryDto(
            Id: k.Id,
            Name: k.Name,
            Progress: (double)k.Progress,
            StatusLabel: k.Status.ToString(),
            TrafficLight: GetTrafficLight((double)k.Progress),
            DepartmentName: k.Department?.Name ?? ""
        ))
        .ToList();

    var allLogs = kpis.SelectMany(k => k.ProgressLogs).ToList();
    var monthlyTrend = allLogs
        .GroupBy(l => new { l.Year, l.Month })
        .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
        .Take(6)
        .Select(g => new MonthlyTrendDto(
            Month: $"{g.Key.Year}-{g.Key.Month:D2}",
            AverageProgress: Math.Round(g.Average(l => (double)l.Progress), 1)
        ))
        .ToList();

    return new DashboardDto(
        TotalKpis: totalKpis,
        ActiveKpis: activeKpis,
        DelayedKpis: delayedKpis,
        OnTrackKpis: onTrackKpis,
        OverallProgress: overallProgress,
        DepartmentRankings: deptRankings,
        RecentKpis: recentKpis,
        MonthlyTrend: monthlyTrend
    );
}

    private static string GetTrafficLight(double progress) => progress switch
    {
        >= 70 => "green",
        >= 40 => "yellow",
        _ => "red"
    };
}
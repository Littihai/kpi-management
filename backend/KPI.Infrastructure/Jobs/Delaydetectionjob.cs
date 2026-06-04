using KPI.Application.Jobs;
using KPI.Application.Notifications;
using KPI.Domain.Enums;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Jobs;

public class DelayDetectionJob : IDelayDetectionJob
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;

    public DelayDetectionJob(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("=== Running Delay Detection Job ===");

        var activeKpis = await _context.Kpis
            .Include(k => k.Owner)
            .Where(k => k.Status == KpiStatus.Active)
            .ToListAsync();

        var today = DateTime.UtcNow;

        foreach (var kpi in activeKpis)
        {
            var totalDays = (kpi.DueDate - kpi.StartDate).TotalDays;
            var elapsedDays = (today - kpi.StartDate).TotalDays;
            var expectedProgress = totalDays > 0
                ? (decimal)(elapsedDays / totalDays) * kpi.Target
                : 0;

            var isDelayed = kpi.Progress < expectedProgress - 15;

            if (isDelayed && kpi.Owner != null)
            {
                var daysDelayed = (int)(today - kpi.DueDate).TotalDays;
                daysDelayed = daysDelayed < 0 ? 0 : daysDelayed;

                kpi.Status = KpiStatus.AtRisk;

                await _emailService.SendKpiDelayedAsync(
                    kpi.Owner.Email,
                    kpi.Owner.FirstName + " " + kpi.Owner.LastName,
                    kpi.Name,
                    daysDelayed
                );

                Console.WriteLine($"=== Delay alert sent for: {kpi.Name} ===");
            }
        }

        await _context.SaveChangesAsync();
        Console.WriteLine("=== Delay Detection Job Complete ===");
    }
}
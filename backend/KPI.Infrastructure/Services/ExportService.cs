using ClosedXML.Excel;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Application.Export;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KPI.Infrastructure.Services;

public class ExportService : IExportService
{
    private readonly AppDbContext _context;

    public ExportService(AppDbContext context)
    {
        _context = context;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>KPIs the user may export: every department for SuperAdmin/Director, otherwise their own.</summary>
    private async Task<List<Kpi>> GetVisibleKpisAsync(Guid userId)
    {
        var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || !user.IsActive)
            throw new ForbiddenException("Your account is not active.");

        var query = _context.Kpis
            .Include(k => k.Department)
            .Include(k => k.Owner)
            .AsQueryable();

        if (!RoleNames.SeesAllDepartments(user.Role.Name))
        {
            var departmentId = user.DepartmentId;
            query = query.Where(k => departmentId != null && k.DepartmentId == departmentId);
        }

        return await query.OrderBy(k => k.Department.Name).ToListAsync();
    }

    public async Task<byte[]> ExportKpiToPdfAsync(Guid userId)
    {
        var kpis = await GetVisibleKpisAsync(userId);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);

                page.Header().Text("KPI Summary Report")
                    .FontSize(18).SemiBold();

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    // Header
                    table.Header(header =>
                    {
                        foreach (var h in new[] { "KPI Name", "Department", "Progress", "Target", "Status" })
                        {
                            header.Cell().Background("#534AB7")
                                .Padding(6)
                                .Text(h).FontColor("#FFFFFF").FontSize(10).SemiBold();
                        }
                    });

                    // Rows
                    foreach (var kpi in kpis)
                    {
                        var isOdd = kpis.IndexOf(kpi) % 2 == 0;
                        var bg = isOdd ? "#F8F8FF" : "#FFFFFF";

                        table.Cell().Background(bg).Padding(6)
                            .Text(kpi.Name).FontSize(9);
                        table.Cell().Background(bg).Padding(6)
                            .Text(kpi.Department?.Name ?? "").FontSize(9);
                        table.Cell().Background(bg).Padding(6)
                            .Text($"{kpi.Progress}%").FontSize(9);
                        table.Cell().Background(bg).Padding(6)
                            .Text($"{kpi.Target}%").FontSize(9);
                        table.Cell().Background(bg).Padding(6)
                            .Text(kpi.Status.ToString()).FontSize(9);
                    }
                });

                page.Footer()
                    .AlignRight()
                    .Text(x =>
                    {
                        x.Span("Generated: ");
                        x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    });
            });
        });

        return doc.GeneratePdf();
    }

    public async Task<byte[]> ExportKpiToExcelAsync(Guid userId)
    {
        var kpis = await GetVisibleKpisAsync(userId);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("KPI Report");

        // Header
        var headers = new[] { "KPI Name", "Department", "Owner", "Year", "Progress (%)", "Target (%)", "Status", "Due Date" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#534AB7");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Rows
        for (int i = 0; i < kpis.Count; i++)
        {
            var kpi = kpis[i];
            var row = i + 2;
            var bg = i % 2 == 0 ? XLColor.FromHtml("#F8F8FF") : XLColor.White;

            ws.Cell(row, 1).Value = kpi.Name;
            ws.Cell(row, 2).Value = kpi.Department?.Name ?? "";
            ws.Cell(row, 3).Value = kpi.Owner != null
                ? kpi.Owner.FirstName + " " + kpi.Owner.LastName : "";
            ws.Cell(row, 4).Value = kpi.Year;
            ws.Cell(row, 5).Value = (double)kpi.Progress;
            ws.Cell(row, 6).Value = (double)kpi.Target;
            ws.Cell(row, 7).Value = kpi.Status.ToString();
            ws.Cell(row, 8).Value = kpi.DueDate.ToString("yyyy-MM-dd");

            for (int col = 1; col <= 8; col++)
                ws.Cell(row, col).Style.Fill.BackgroundColor = bg;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
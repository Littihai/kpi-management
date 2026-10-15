using System.Security.Claims;
using KPI.Application.Export;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Export.View)]
public class ExportController : ControllerBase
{
    private readonly IExportService _exportService;

    public ExportController(IExportService exportService)
    {
        _exportService = exportService;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("kpi/pdf")]
    public async Task<IActionResult> ExportPdf()
    {
        var bytes = await _exportService.ExportKpiToPdfAsync(GetUserId());
        return File(bytes, "application/pdf", $"kpi-report-{DateTime.Now:yyyyMMdd}.pdf");
    }

    [HttpGet("kpi/excel")]
    public async Task<IActionResult> ExportExcel()
    {
        var bytes = await _exportService.ExportKpiToExcelAsync(GetUserId());
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"kpi-report-{DateTime.Now:yyyyMMdd}.xlsx");
    }
}

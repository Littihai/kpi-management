using KPI.Application.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExportController : ControllerBase
{
    private readonly IExportService _exportService;

    public ExportController(IExportService exportService)
    {
        _exportService = exportService;
    }

    [HttpGet("kpi/pdf")]
    public async Task<IActionResult> ExportPdf()
    {
        var bytes = await _exportService.ExportKpiToPdfAsync();
        return File(bytes, "application/pdf", $"kpi-report-{DateTime.Now:yyyyMMdd}.pdf");
    }

    [HttpGet("kpi/excel")]
    public async Task<IActionResult> ExportExcel()
    {
        var bytes = await _exportService.ExportKpiToExcelAsync();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"kpi-report-{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
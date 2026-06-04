namespace KPI.Application.Export;

public interface IExportService
{
    Task<byte[]> ExportKpiToPdfAsync();
    Task<byte[]> ExportKpiToExcelAsync();
}
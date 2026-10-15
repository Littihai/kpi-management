namespace KPI.Application.Export;

public interface IExportService
{
    /// <summary>Exports the KPIs the given user is allowed to see.</summary>
    Task<byte[]> ExportKpiToPdfAsync(Guid userId);
    Task<byte[]> ExportKpiToExcelAsync(Guid userId);
}

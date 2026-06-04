namespace KPI.Application.Audit;

public interface IAuditService
{
    Task LogAsync(string action, string entityName, string entityId,
        string? oldValues = null, string? newValues = null,
        Guid? userId = null, string? userEmail = null);

    Task<List<AuditLogDto>> GetLogsAsync(string? entityName = null, Guid? userId = null);
}

public record AuditLogDto(
    Guid Id,
    string Action,
    string EntityName,
    string EntityId,
    string? OldValues,
    string? NewValues,
    string? UserEmail,
    DateTime CreatedAt
);
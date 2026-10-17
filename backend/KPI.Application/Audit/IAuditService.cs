using KPI.Application.Common;

namespace KPI.Application.Audit;

public interface IAuditService
{
    Task LogAsync(string action, string entityName, string entityId,
        string? oldValues = null, string? newValues = null,
        Guid? userId = null, string? userEmail = null);

    Task<PagedResult<AuditLogDto>> SearchAsync(AuditQuery query);

    /// <summary>The events of one record, oldest first (used for an issue's timeline).</summary>
    Task<List<AuditLogDto>> ForEntityAsync(string entityName, string entityId);
}

public record AuditQuery(
    string? Search = null,
    string? Entity = null,
    string? Action = null,
    Guid? UserId = null,
    string? Ip = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 50);

public record AuditLogDto(
    Guid Id,
    string Action,
    string EntityName,
    string EntityId,
    string? OldValues,
    string? NewValues,
    Guid? UserId,
    string? UserEmail,
    string? UserName,
    string? UserRole,
    string? DepartmentName,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAt
);

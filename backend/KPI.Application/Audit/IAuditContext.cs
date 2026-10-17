namespace KPI.Application.Audit;

/// <summary>
/// Who is acting and from where, for the current request. Stamped onto every audit row automatically,
/// so call sites only say *what* happened. Background jobs have no request and use <see cref="NullAuditContext"/>.
/// </summary>
public interface IAuditContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? RequestId { get; }
    string? UserName { get; }
    string? UserRole { get; }
    string? DepartmentName { get; }
}

public sealed class NullAuditContext : IAuditContext
{
    public static readonly NullAuditContext Instance = new();
    public string? IpAddress => null;
    public string? UserAgent => null;
    public string? RequestId => null;
    public string? UserName => null;
    public string? UserRole => null;
    public string? DepartmentName => null;
}

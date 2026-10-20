using KPI.Application.Common;

namespace KPI.Application.Admin;

/// <summary>One row of the department admin list (inactive ones included).</summary>
public record DepartmentDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    Guid? ManagerId,
    string? ManagerName,
    int UserCount);

/// <summary>Create or update a department. Deactivating hides it from pickers; tickets that name it keep it.</summary>
public record SaveDepartmentRequest(string? Code, string? Name, Guid? ManagerId, bool IsActive = true);

public record UserRowDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    string Role,
    Guid? DepartmentId,
    string? Department);

public record UserQuery(
    string? Search = null,
    string? Role = null,
    Guid? DepartmentId = null,
    bool? Active = null,
    int Page = 1,
    int PageSize = 20);

public record UpdateUserRequest(string? FirstName, string? LastName, string? Email, Guid? DepartmentId);

public record ResetPasswordRequest(string? NewPassword);

/// <summary>People and departments: the organisation data every other feature (routing, approvals, reports) leans on.</summary>
public interface IOrgAdminService
{
    Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync();
    Task<DepartmentDto> CreateDepartmentAsync(SaveDepartmentRequest request);
    Task<DepartmentDto> UpdateDepartmentAsync(Guid id, SaveDepartmentRequest request);

    /// <summary>SuperAdmin and Director see everyone; anyone else only their own department.</summary>
    Task<PagedResult<UserRowDto>> SearchUsersAsync(Guid callerId, UserQuery query);
    /// <summary>The next three refuse (403) when the target is a SuperAdmin and the actor is not one.</summary>
    Task<UserRowDto> UpdateUserAsync(Guid actorId, Guid id, UpdateUserRequest request);
    Task<UserRowDto> ReactivateUserAsync(Guid actorId, Guid id);
    Task ResetPasswordAsync(Guid actorId, Guid id, ResetPasswordRequest request);
}

using KPI.Domain.Constants;

namespace KPI.Application.Admin;

public record PermissionDto(string Name, string Module, string Group, string Label, string Description);

public record RoleDto(
    Guid Id,
    string Name,
    string Description,
    /// <summary>Built-in roles are fixed in code and cannot be edited or deleted.</summary>
    bool IsSystem,
    int UserCount,
    IReadOnlyList<string> Permissions,
    IssueVisibility IssueVisibility,
    bool SeesAllDepartments);

/// <summary>Create or update a custom role. The permissions are the full list the role will hold.</summary>
public record SaveRoleRequest(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Permissions,
    IssueVisibility IssueVisibility = IssueVisibility.Own,
    bool SeesAllDepartments = false);

/// <summary>Roles and what they may do. Nobody can hand out more than they hold.</summary>
public interface IRoleAdminService
{
    Task<IReadOnlyList<RoleDto>> ListAsync();
    IReadOnlyList<PermissionDto> Catalog();
    Task<RoleDto> CreateAsync(Guid actorId, SaveRoleRequest request);
    Task<RoleDto> UpdateAsync(Guid actorId, Guid id, SaveRoleRequest request);
    Task DeleteAsync(Guid actorId, Guid id);

    /// <summary>Throws unless the actor holds every right (and sees as much) as the role they are about to give someone.</summary>
    Task EnsureMayAssignAsync(Guid actorId, Guid roleId);
}

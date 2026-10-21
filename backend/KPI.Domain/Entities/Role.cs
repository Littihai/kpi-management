using KPI.Domain.Constants;

namespace KPI.Domain.Entities;

public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Which issues the role sees (the permission says what it may do; this says on whose issues).</summary>
    public IssueVisibility IssueVisibility { get; set; } = IssueVisibility.Own;

    /// <summary>Whether the role sees people of every department in the user list, not just its own.</summary>
    public bool SeesAllDepartments { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
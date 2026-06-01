namespace KPI.Domain.Entities;

public class Department : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}
namespace KPI.Domain.Entities;

/// <summary>A setting an administrator changed. The key is one of SettingCatalog; settings left at their default have no row.</summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedById { get; set; }
    public string? UpdatedByName { get; set; }
}

/// <summary>Every change to a setting. A null value means "back to the default".</summary>
public class AppSettingHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public Guid? ChangedById { get; set; }
    public string? ChangedByName { get; set; }
}

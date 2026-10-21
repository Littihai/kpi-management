using KPI.Domain.Issues;
using KPI.Domain.Settings;

namespace KPI.Application.Settings;

public record SettingDto(
    string Key,
    SettingType Type,
    string Category,
    string Label,
    string Description,
    string Default,
    string Value,
    bool IsDefault,
    double? Min,
    double? Max,
    string? Unit,
    bool RequiresRestart,
    DateTime? UpdatedAt,
    string? UpdatedByName);

public record SettingHistoryDto(string Key, string? OldValue, string? NewValue, DateTime ChangedAt, string? ChangedByName);

public record SaveSettingRequest(string? Value);

/// <summary>The values the rest of the system reads. Cached briefly; a change made here is seen at once on this server.</summary>
public interface ISystemSettings
{
    Task<string> GetAsync(string key);
    Task<double> GetNumberAsync(string key);
    Task<bool> GetBoolAsync(string key);
    Task<IssueSlaPolicy> SlaAsync();
    void Invalidate();
}

/// <summary>Reading and changing settings (the admin screen).</summary>
public interface ISettingsAdminService
{
    Task<IReadOnlyList<SettingDto>> ListAsync();
    Task<SettingDto> UpdateAsync(Guid actorId, string key, SaveSettingRequest request);
    Task<SettingDto> ResetAsync(Guid actorId, string key);
    Task<IReadOnlyList<SettingHistoryDto>> HistoryAsync(string key);
}

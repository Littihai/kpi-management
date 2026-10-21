using System.Globalization;
using KPI.Application.Settings;
using KPI.Domain.Issues;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Services;

/// <summary>
/// A shared snapshot of every changed setting, refreshed every minute and whenever a change is made on this server.
/// If the database cannot be reached, the last good snapshot (or the defaults) keeps being served.
/// </summary>
public sealed class SystemSettingsService : ISystemSettings
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<SystemSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Dictionary<string, string> _overrides = new();
    private DateTime _loadedAt = DateTime.MinValue;

    public SystemSettingsService(IServiceScopeFactory scopes, TimeProvider time, ILogger<SystemSettingsService> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    public void Invalidate() => _loadedAt = DateTime.MinValue;

    public async Task<string> GetAsync(string key)
    {
        var definition = SettingCatalog.Find(key) ?? throw new ArgumentException($"Unknown setting '{key}'", nameof(key));
        await EnsureFreshAsync();
        return _overrides.TryGetValue(key, out var value) ? value : definition.Default;
    }

    public async Task<double> GetNumberAsync(string key) =>
        double.Parse(await GetAsync(key), CultureInfo.InvariantCulture);

    public async Task<bool> GetBoolAsync(string key) =>
        string.Equals(await GetAsync(key), "true", StringComparison.OrdinalIgnoreCase);

    public Task<IssueSlaPolicy> SlaAsync() => SettingsDefaults.SlaAsync(this);

    private async Task EnsureFreshAsync()
    {
        if (_time.GetUtcNow().UtcDateTime - _loadedAt < Ttl) return;
        await _gate.WaitAsync();
        try
        {
            if (_time.GetUtcNow().UtcDateTime - _loadedAt < Ttl) return;
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                _overrides = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh the settings; using the last known values");
            }
            _loadedAt = _time.GetUtcNow().UtcDateTime;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Settings that are all at their defaults: for tests and for code that runs without a database.</summary>
public sealed class DefaultSettings : ISystemSettings
{
    private readonly Dictionary<string, string> _values = new();

    public DefaultSettings Set(string key, string value)
    {
        _values[key] = SettingCatalog.Normalize(SettingCatalog.Find(key)!, value);
        return this;
    }

    public Task<string> GetAsync(string key) =>
        Task.FromResult(_values.TryGetValue(key, out var v) ? v : (SettingCatalog.Find(key) ?? throw new ArgumentException(key)).Default);

    public async Task<double> GetNumberAsync(string key) => double.Parse(await GetAsync(key), CultureInfo.InvariantCulture);
    public async Task<bool> GetBoolAsync(string key) => string.Equals(await GetAsync(key), "true", StringComparison.OrdinalIgnoreCase);
    public Task<IssueSlaPolicy> SlaAsync() => SettingsDefaults.SlaAsync(this);
    public void Invalidate() { }
}

internal static class SettingsDefaults
{
    public static async Task<IssueSlaPolicy> SlaAsync(ISystemSettings settings) => new(
        TimeSpan.FromHours(await settings.GetNumberAsync(SettingCatalog.Keys.FirstResponseHours)),
        TimeSpan.FromHours(await settings.GetNumberAsync(SettingCatalog.Keys.ResolveHoursCritical)),
        TimeSpan.FromHours(await settings.GetNumberAsync(SettingCatalog.Keys.ResolveHoursHigh)),
        TimeSpan.FromHours(await settings.GetNumberAsync(SettingCatalog.Keys.ResolveHoursMedium)),
        TimeSpan.FromHours(await settings.GetNumberAsync(SettingCatalog.Keys.ResolveHoursLow)));
}

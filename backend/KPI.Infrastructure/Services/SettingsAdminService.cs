using KPI.Application.Settings;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class SettingsAdminService : ISettingsAdminService
{
    private readonly AppDbContext _db;
    private readonly ISystemSettings _settings;
    private readonly ISecretProtector _secrets;
    private readonly TimeProvider _time;

    public SettingsAdminService(AppDbContext db, ISystemSettings settings, ISecretProtector secrets, TimeProvider time)
    {
        _db = db;
        _settings = settings;
        _secrets = secrets;
        _time = time;
    }

    public async Task<IReadOnlyList<SettingDto>> ListAsync()
    {
        var stored = await _db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key);
        return SettingCatalog.All.Select(d => ToDto(d, stored.GetValueOrDefault(d.Key))).ToList();
    }

    public async Task<SettingDto> UpdateAsync(Guid actorId, string key, SaveSettingRequest request)
    {
        var definition = FindOrThrow(key);
        var value = SettingCatalog.Normalize(definition, request.Value);
        var current = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        // A secret is compared in plain text but only ever stored encrypted and never written to the history.
        var effective = current is null ? definition.Default
            : definition.Sensitive ? _secrets.Unprotect(current.Value) ?? definition.Default : current.Value;
        if (value == effective) return ToDto(definition, current);       // nothing changed: no history line either

        if (key == SettingCatalog.Keys.EmailProvider && value == "smtp")
            await EnsureSmtpIsConfiguredAsync();

        var actorName = await ActorNameAsync(actorId);
        var now = _time.GetUtcNow().UtcDateTime;
        string? newStored = value == definition.Default ? null : definition.Sensitive ? _secrets.Protect(value) : value;   // back at the default needs no row

        if (newStored is null)
        {
            if (current is not null) _db.AppSettings.Remove(current);
            current = null;
        }
        else if (current is null)
        {
            current = new AppSetting { Key = key, Value = newStored, UpdatedAt = now, UpdatedById = actorId, UpdatedByName = actorName };
            _db.AppSettings.Add(current);
        }
        else
        {
            current.Value = newStored;
            current.UpdatedAt = now;
            current.UpdatedById = actorId;
            current.UpdatedByName = actorName;
        }

        _db.AppSettingHistory.Add(new AppSettingHistory
        {
            Key = key,
            OldValue = definition.Sensitive ? Mask(effective) : effective,
            NewValue = definition.Sensitive ? Mask(value) : value,
            ChangedAt = now, ChangedById = actorId, ChangedByName = actorName,
        });
        await _db.SaveChangesAsync();
        _settings.Invalidate();
        return ToDto(definition, current);
    }

    public Task<SettingDto> ResetAsync(Guid actorId, string key) =>
        UpdateAsync(actorId, key, new SaveSettingRequest(FindOrThrow(key).Default));

    public async Task<IReadOnlyList<SettingHistoryDto>> HistoryAsync(string key)
    {
        FindOrThrow(key);
        var rows = await _db.AppSettingHistory.AsNoTracking().Where(h => h.Key == key).ToListAsync();
        return rows.OrderByDescending(h => h.ChangedAt).Take(50)
            .Select(h => new SettingHistoryDto(h.Key, h.OldValue, h.NewValue, h.ChangedAt, h.ChangedByName)).ToList();
    }

    private static string Mask(string value) => value.Length == 0 ? "" : "••••••••";

    private async Task EnsureSmtpIsConfiguredAsync()
    {
        var stored = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == SettingCatalog.Keys.SmtpHost || s.Key == SettingCatalog.Keys.SmtpFromAddress)
            .Select(s => s.Key).ToListAsync();
        if (!stored.Contains(SettingCatalog.Keys.SmtpHost) || !stored.Contains(SettingCatalog.Keys.SmtpFromAddress))
            throw new RequestValidationException("ตั้งค่าเซิร์ฟเวอร์ SMTP และที่อยู่ผู้ส่งให้ครบก่อนเลือกช่องทางนี้");
    }

    private static SettingDefinition FindOrThrow(string key) =>
        SettingCatalog.Find(key) ?? throw new NotFoundException("ไม่พบการตั้งค่านี้");

    private async Task<string?> ActorNameAsync(Guid actorId)
    {
        var user = await _db.Users.AsNoTracking().Where(u => u.Id == actorId).Select(u => new { u.FirstName, u.LastName }).FirstOrDefaultAsync();
        return user is null ? null : $"{user.FirstName} {user.LastName}".Trim();
    }

    private static SettingDto ToDto(SettingDefinition d, AppSetting? stored) =>
        new(d.Key, d.Type, d.Category, d.Label, d.Description, d.Default,
            d.Sensitive ? "" : stored?.Value ?? d.Default,       // a secret is never sent back
            stored is null, d.Min, d.Max, d.Unit, d.RequiresRestart, stored?.UpdatedAt, stored?.UpdatedByName,
            d.Sensitive, d.Options);
}

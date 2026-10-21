using System.Globalization;
using KPI.Domain.Exceptions;

namespace KPI.Domain.Settings;

public enum SettingType { Number, Bool, Text }

/// <summary>One setting an administrator can change. Only values that differ from <see cref="Default"/> are stored.</summary>
public sealed record SettingDefinition(
    string Key,
    SettingType Type,
    string Category,
    string Label,
    string Description,
    string Default,
    double? Min = null,
    double? Max = null,
    string? Unit = null,
    bool RequiresRestart = false);

/// <summary>
/// Every setting, in code: the label shown to the administrator, its limits and its default. Adding one needs no
/// migration. Settings that nothing reads are not listed here.
/// </summary>
public static class SettingCatalog
{
    public const string CategorySla = "เป้าหมายเวลา (SLA)";
    public const string CategoryIssues = "การแจ้งปัญหา";
    public const string CategoryEmail = "อีเมล";
    public const string CategoryRetention = "การเก็บข้อมูล";

    public static class Keys
    {
        public const string FirstResponseHours = "sla.firstResponseHours";
        public const string ResolveHoursCritical = "sla.resolveHours.critical";
        public const string ResolveHoursHigh = "sla.resolveHours.high";
        public const string ResolveHoursMedium = "sla.resolveHours.medium";
        public const string ResolveHoursLow = "sla.resolveHours.low";
        public const string AutoCloseDays = "issues.autoCloseDays";
        public const string MaxAttachments = "issues.maxAttachments";
        public const string MaxAttachmentMb = "issues.maxAttachmentMb";
        public const string EmailEnabled = "email.enabled";
        public const string RequestLogDays = "retention.requestLogDays";
    }

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(Keys.FirstResponseHours, SettingType.Number, CategorySla, "ตอบรับครั้งแรกภายใน",
            "เวลาที่ทีม IT ต้องรับเรื่องหรือตอบผู้แจ้งครั้งแรก นับจากเวลาที่แจ้ง (ชั่วโมงตามปฏิทิน)", "2", 0.25, 168, "ชั่วโมง"),
        new(Keys.ResolveHoursCritical, SettingType.Number, CategorySla, "แก้ไขเรื่องวิกฤตภายใน",
            "เป้าหมายเวลาแก้ไขเรื่องความสำคัญ \"วิกฤต\" มีผลกับเรื่องที่แจ้งหรือเปลี่ยนความสำคัญหลังจากนี้", "4", 1, 720, "ชั่วโมง"),
        new(Keys.ResolveHoursHigh, SettingType.Number, CategorySla, "แก้ไขเรื่องความสำคัญสูงภายใน",
            "เป้าหมายเวลาแก้ไขเรื่องความสำคัญ \"สูง\"", "24", 1, 720, "ชั่วโมง"),
        new(Keys.ResolveHoursMedium, SettingType.Number, CategorySla, "แก้ไขเรื่องความสำคัญปานกลางภายใน",
            "เป้าหมายเวลาแก้ไขเรื่องความสำคัญ \"ปานกลาง\"", "72", 1, 720, "ชั่วโมง"),
        new(Keys.ResolveHoursLow, SettingType.Number, CategorySla, "แก้ไขเรื่องความสำคัญต่ำภายใน",
            "เป้าหมายเวลาแก้ไขเรื่องความสำคัญ \"ต่ำ\"", "168", 1, 720, "ชั่วโมง"),

        new(Keys.AutoCloseDays, SettingType.Number, CategoryIssues, "ปิดเรื่องอัตโนมัติหลังแก้ไขแล้ว",
            "ถ้าผู้แจ้งไม่ตอบหลังเรื่องถูกทำเครื่องหมายว่าแก้ไขแล้ว ระบบจะปิดเรื่องให้เมื่อครบกี่วัน", "3", 1, 60, "วัน"),
        new(Keys.MaxAttachments, SettingType.Number, CategoryIssues, "จำนวนรูปแนบสูงสุดต่อเรื่อง",
            "รวมรูปจากตอนแจ้งและตอนแนบเพิ่ม", "5", 1, 6, "รูป"),
        new(Keys.MaxAttachmentMb, SettingType.Number, CategoryIssues, "ขนาดรูปแนบสูงสุดต่อไฟล์",
            "รูปที่ใหญ่กว่านี้จะถูกปฏิเสธ (เซิร์ฟเวอร์รับคำขอรวมได้ไม่เกิน 30 MB)", "5", 1, 5, "MB"),

        new(Keys.EmailEnabled, SettingType.Bool, CategoryEmail, "ส่งอีเมลแจ้งเตือน",
            "ปิดเมื่อต้องการหยุดส่งอีเมลทั้งระบบชั่วคราว (การแจ้งเตือนในแอปยังทำงานตามปกติ)", "true"),

        new(Keys.RequestLogDays, SettingType.Number, CategoryRetention, "เก็บบันทึกคำขอ (request log)",
            "งานดูแลระบบจะลบบันทึกคำขอที่เก่ากว่านี้ บันทึกการเปลี่ยนแปลงข้อมูลทางธุรกิจ (audit) ไม่ถูกลบ", "90", 7, 730, "วัน"),
    ];

    private static readonly Dictionary<string, SettingDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static SettingDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>Checks a new value against the setting's type and limits and returns it in its stored form.</summary>
    public static string Normalize(SettingDefinition definition, string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        switch (definition.Type)
        {
            case SettingType.Bool:
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
                if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
                throw new RequestValidationException("ค่าต้องเป็นเปิดหรือปิด");
            case SettingType.Number:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))
                    throw new RequestValidationException("ค่าต้องเป็นตัวเลข");
                if (definition.Min is { } min && number < min || definition.Max is { } max && number > max)
                    throw new RequestValidationException($"ค่าต้องอยู่ระหว่าง {definition.Min:0.##} ถึง {definition.Max:0.##}");
                return number.ToString("0.####", CultureInfo.InvariantCulture);
            default:
                if (value.Length > 200) throw new RequestValidationException("ค่ายาวเกินไป");
                return value;
        }
    }
}

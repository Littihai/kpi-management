using System.Globalization;
using KPI.Domain.Exceptions;

namespace KPI.Domain.Settings;

public enum SettingType { Number, Bool, Text }

/// <summary>What a text setting must look like. Checked when it is saved.</summary>
public enum TextFormat { Any, EmailAddress, Host }

public sealed record SettingOption(string Value, string Label);

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
    bool RequiresRestart = false,
    IReadOnlyList<SettingOption>? Options = null,
    /// <summary>Stored encrypted and never sent back to the browser (passwords, keys).</summary>
    bool Sensitive = false,
    TextFormat Format = TextFormat.Any);

/// <summary>
/// Every setting, in code: the label shown to the administrator, its limits and its default. Adding one needs no
/// migration. Settings that nothing reads are not listed here.
/// </summary>
public static class SettingCatalog
{
    public const string CategoryOrganization = "องค์กร";
    public const string CategorySla = "เป้าหมายเวลา (SLA)";
    public const string CategoryIssues = "การแจ้งปัญหา";
    public const string CategoryEmail = "อีเมล";
    public const string CategoryRetention = "การเก็บข้อมูล";
    public const string CategorySmtp = "อีเมลผ่าน SMTP";
    public const string CategorySecurity = "ความปลอดภัย";

    public static class Keys
    {
        public const string CompanyName = "org.companyName";
        public const string FirstResponseHours = "sla.firstResponseHours";
        public const string ResolveHoursCritical = "sla.resolveHours.critical";
        public const string ResolveHoursHigh = "sla.resolveHours.high";
        public const string ResolveHoursMedium = "sla.resolveHours.medium";
        public const string ResolveHoursLow = "sla.resolveHours.low";
        public const string AutoCloseDays = "issues.autoCloseDays";
        public const string MaxAttachments = "issues.maxAttachments";
        public const string MaxAttachmentMb = "issues.maxAttachmentMb";
        public const string EmailEnabled = "email.enabled";
        public const string SmtpHost = "smtp.host";
        public const string SmtpPort = "smtp.port";
        public const string SmtpSecurity = "smtp.security";
        public const string SmtpUsername = "smtp.username";
        public const string SmtpPassword = "smtp.password";
        public const string SmtpFromAddress = "smtp.fromAddress";
        public const string SmtpFromName = "smtp.fromName";
        public const string SmtpTimeoutSeconds = "smtp.timeoutSeconds";
        public const string RequestLogDays = "retention.requestLogDays";
        public const string AuthLogDays = "retention.authLogDays";
        public const string PasswordMinLength = "security.password.minLength";
        public const string PasswordRequireUpper = "security.password.requireUpper";
        public const string PasswordRequireLower = "security.password.requireLower";
        public const string PasswordRequireDigit = "security.password.requireDigit";
        public const string PasswordRequireSpecial = "security.password.requireSpecial";
        public const string PasswordHistoryCount = "security.password.historyCount";
        public const string PasswordMaxAgeDays = "security.password.maxAgeDays";
        public const string ForceChangeOnAdminSet = "security.forceChangeOnAdminSet";
        public const string LoginMaxFailures = "security.login.maxFailures";
        public const string LoginLockMinutes = "security.login.lockMinutes";
        public const string SessionDays = "security.sessionDays";
    }

    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(Keys.CompanyName, SettingType.Text, CategoryOrganization, "ชื่อองค์กร",
            "แสดงบนหน้าเข้าสู่ระบบ แถบด้านข้าง ชื่อหน้าต่าง และอีเมลทดสอบ เว้นว่างไว้เพื่อใช้ชื่อที่ตั้งตอนติดตั้ง (ถ้ามี) หรือคำว่า Helpdesk", "", Max: 80),

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

        new(Keys.SmtpHost, SettingType.Text, CategorySmtp, "เซิร์ฟเวอร์ SMTP", "ชื่อโฮสต์หรือ IP เช่น smtp.office365.com", "", Format: TextFormat.Host),
        new(Keys.SmtpPort, SettingType.Number, CategorySmtp, "พอร์ต", "587 สำหรับ STARTTLS, 465 สำหรับ SSL/TLS, 25 สำหรับไม่เข้ารหัส", "587", 1, 65535),
        new(Keys.SmtpSecurity, SettingType.Text, CategorySmtp, "การเข้ารหัสการเชื่อมต่อ", "ควรใช้ STARTTLS หรือ SSL/TLS เสมอ เพื่อไม่ให้รหัสผ่านและเนื้อหาอีเมลถูกดักระหว่างทาง", "starttls",
            Options: [new("starttls", "STARTTLS (แนะนำ)"), new("ssl", "SSL/TLS"), new("none", "ไม่เข้ารหัส")]),
        new(Keys.SmtpUsername, SettingType.Text, CategorySmtp, "ชื่อผู้ใช้", "เว้นว่างถ้าเซิร์ฟเวอร์ไม่ต้องล็อกอิน", ""),
        new(Keys.SmtpPassword, SettingType.Text, CategorySmtp, "รหัสผ่าน", "เก็บแบบเข้ารหัสและไม่แสดงกลับ ถ้าเปลี่ยน JWT key ของเซิร์ฟเวอร์ ต้องกรอกใหม่", "", Sensitive: true),
        new(Keys.SmtpFromAddress, SettingType.Text, CategorySmtp, "ที่อยู่ผู้ส่ง", "อีเมลที่ผู้รับเห็นเป็นผู้ส่ง ต้องเป็นที่อยู่ที่เซิร์ฟเวอร์อนุญาตให้ใช้", "", Format: TextFormat.EmailAddress),
        new(Keys.SmtpFromName, SettingType.Text, CategorySmtp, "ชื่อผู้ส่ง", "ชื่อที่แสดงข้างที่อยู่ผู้ส่ง", "Helpdesk"),
        new(Keys.SmtpTimeoutSeconds, SettingType.Number, CategorySmtp, "เวลารอเชื่อมต่อสูงสุด", "ถ้าเซิร์ฟเวอร์ไม่ตอบภายในเวลานี้ ถือว่าส่งไม่สำเร็จ", "15", 3, 60, "วินาที"),

        new(Keys.PasswordMinLength, SettingType.Number, CategorySecurity, "ความยาวรหัสผ่านขั้นต่ำ", "รหัสผ่านใหม่ต้องยาวอย่างน้อยกี่ตัวอักษร", "8", 6, 128, "ตัวอักษร"),
        new(Keys.PasswordRequireUpper, SettingType.Bool, CategorySecurity, "ต้องมีตัวพิมพ์ใหญ่ (A-Z)", "รหัสผ่านใหม่ต้องมีอักษรอังกฤษพิมพ์ใหญ่อย่างน้อยหนึ่งตัว", "false"),
        new(Keys.PasswordRequireLower, SettingType.Bool, CategorySecurity, "ต้องมีตัวพิมพ์เล็ก (a-z)", "รหัสผ่านใหม่ต้องมีอักษรอังกฤษพิมพ์เล็กอย่างน้อยหนึ่งตัว", "false"),
        new(Keys.PasswordRequireDigit, SettingType.Bool, CategorySecurity, "ต้องมีตัวเลข (0-9)", "รหัสผ่านใหม่ต้องมีตัวเลขอย่างน้อยหนึ่งตัว", "false"),
        new(Keys.PasswordRequireSpecial, SettingType.Bool, CategorySecurity, "ต้องมีอักขระพิเศษ", "รหัสผ่านใหม่ต้องมีเครื่องหมายหรืออักขระพิเศษ เช่น ! @ # $ อย่างน้อยหนึ่งตัว", "false"),
        new(Keys.PasswordHistoryCount, SettingType.Number, CategorySecurity, "ห้ามใช้รหัสผ่านเดิมซ้ำ", "จำนวนรหัสผ่านล่าสุดที่ห้ามนำกลับมาใช้ 0 = ไม่ตรวจ", "0", 0, 24, "ครั้งล่าสุด"),
        new(Keys.PasswordMaxAgeDays, SettingType.Number, CategorySecurity, "รหัสผ่านหมดอายุ", "บังคับเปลี่ยนรหัสผ่านเมื่อใช้มานานเกินกี่วัน 0 = ไม่หมดอายุ (เมื่อเปิดใช้ ผู้ที่ไม่เคยเปลี่ยนรหัสและสร้างบัญชีนานเกินกำหนดจะต้องเปลี่ยนทันที)", "0", 0, 3650, "วัน"),
        new(Keys.ForceChangeOnAdminSet, SettingType.Bool, CategorySecurity, "ให้เปลี่ยนรหัสผ่านหลังผู้ดูแลตั้งให้", "ผู้ใช้ที่ผู้ดูแลสร้างบัญชีหรือตั้งรหัสผ่านใหม่ให้ ต้องตั้งรหัสผ่านของตัวเองก่อนใช้งานต่อ", "false"),
        new(Keys.LoginMaxFailures, SettingType.Number, CategorySecurity, "ล็อกบัญชีเมื่อใส่รหัสผิดติดกัน", "จำนวนครั้งที่ผิดติดกันก่อนล็อกบัญชีชั่วคราว 0 = ไม่ล็อก (ปิดไว้ก่อน เพราะเมื่อเปิด คนอื่นที่เดารหัสผิดซ้ำ ๆ จะล็อกบัญชีของเจ้าของได้ ระบบมีตัวหน่วงการเดารหัสตามเครื่องที่ใช้อยู่แล้ว ผู้ดูแลปลดล็อกได้จากหน้าผู้ใช้งาน)", "0", 0, 20, "ครั้ง"),
        new(Keys.LoginLockMinutes, SettingType.Number, CategorySecurity, "ระยะเวลาที่ล็อกบัญชี", "บัญชีที่ถูกล็อกจะเข้าสู่ระบบไม่ได้จนครบเวลานี้", "15", 1, 1440, "นาที"),
        new(Keys.SessionDays, SettingType.Number, CategorySecurity, "อายุการเข้าสู่ระบบ", "เข้าสู่ระบบแล้วใช้งานได้นานกี่วันก่อนต้องเข้าใหม่ มีผลกับการเข้าสู่ระบบครั้งถัดไป", "7", 1, 30, "วัน"),

        new(Keys.AuthLogDays, SettingType.Number, CategoryRetention, "เก็บบันทึกการเข้าสู่ระบบ",
            "งานดูแลระบบจะลบบันทึกการเข้าสู่ระบบที่เก่ากว่านี้", "180", 30, 1095, "วัน"),
        new(Keys.RequestLogDays, SettingType.Number, CategoryRetention, "เก็บบันทึกคำขอ (request log)",
            "งานดูแลระบบจะลบบันทึกคำขอที่เก่ากว่านี้ บันทึกการเปลี่ยนแปลงข้อมูลทางธุรกิจ (audit) ไม่ถูกลบ", "90", 7, 730, "วัน"),
    ];

    private static readonly Dictionary<string, SettingDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    private static bool IsEmailAddress(string value) =>
        System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value && value.Contains('@') && !value.Contains(' ');

    private static readonly System.Text.RegularExpressions.Regex HostPattern =
        new(@"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsHost(string value) => HostPattern.IsMatch(value);

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
                if (value.Length > (definition.Max is { } limit ? (int)limit : 200)) throw new RequestValidationException("ค่ายาวเกินไป");
                // a name or address is one line; a secret may hold anything
                if (!definition.Sensitive && value.Any(char.IsControl)) throw new RequestValidationException("ใส่ได้เฉพาะข้อความบรรทัดเดียว");
                if (definition.Options is { } options && !options.Any(o => o.Value == value))
                    throw new RequestValidationException("เลือกได้เฉพาะตัวเลือกที่กำหนด");
                if (value.Length > 0)
                    switch (definition.Format)
                    {
                        case TextFormat.EmailAddress when !IsEmailAddress(value):
                            throw new RequestValidationException("รูปแบบอีเมลไม่ถูกต้อง");
                        case TextFormat.Host when !IsHost(value):
                            throw new RequestValidationException("ชื่อโฮสต์ไม่ถูกต้อง (ใส่เฉพาะชื่อหรือ IP ไม่ต้องมี http:// หรือพอร์ต)");
                    }
                return value;
        }
    }
}

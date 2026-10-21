namespace KPI.Domain.Constants;

/// <summary>What each permission means, in words an administrator can read (shown when building a role).</summary>
public static class PermissionCatalog
{
    public sealed record Entry(string Name, string Group, string Label, string Description);

    public static readonly IReadOnlyList<Entry> Entries =
    [
        new(Permissions.Issue.Create, "งานแจ้งปัญหา", "แจ้งปัญหา", "ส่งเรื่องแจ้งปัญหาและติดตามเรื่องของตัวเอง ทุกบทบาทมีสิทธิ์นี้"),
        new(Permissions.Issue.View, "งานแจ้งปัญหา", "ดูเรื่องแจ้งปัญหา", "ดูรายการและรายละเอียดของเรื่อง ตามขอบเขตที่บทบาทกำหนด"),
        new(Permissions.Issue.Manage, "งานแจ้งปัญหา", "ทำงานในคิว", "มอบหมาย เริ่มแก้ไข ขอข้อมูล พักเรื่อง แก้ไขแล้ว ไม่ดำเนินการ เปลี่ยนความสำคัญ และบันทึกภายใน"),
        new(Permissions.User.View, "ผู้ใช้งาน", "ดูรายชื่อผู้ใช้", "เปิดรายชื่อผู้ใช้ (ผู้จัดการเห็นเฉพาะแผนกตัวเอง)"),
        new(Permissions.User.Create, "ผู้ใช้งาน", "เพิ่มผู้ใช้", "สร้างบัญชีผู้ใช้ใหม่"),
        new(Permissions.User.Edit, "ผู้ใช้งาน", "แก้ไขผู้ใช้", "แก้ข้อมูล เปลี่ยนบทบาท ตั้งรหัสผ่านใหม่ เปิดหรือปิดใช้งานบัญชี"),
        new(Permissions.User.Delete, "ผู้ใช้งาน", "ลบผู้ใช้", "ลบบัญชีผู้ใช้"),
        new(Permissions.Department.View, "แผนก", "ดูแผนก", "เลือกแผนกในแบบฟอร์มและรายชื่อ"),
        new(Permissions.Department.Manage, "แผนก", "จัดการแผนก", "สร้าง แก้ไข เปิดหรือปิดแผนก"),
        new(Permissions.Role.View, "บทบาทและสิทธิ์", "ดูบทบาทและสิทธิ์", "ดูบทบาท สิทธิ์ และตารางเทียบ"),
        new(Permissions.Role.Manage, "บทบาทและสิทธิ์", "จัดการบทบาท", "สร้าง แก้ไข ลบบทบาทที่ตั้งเอง ให้ได้เฉพาะสิทธิ์ที่ตัวเองมี"),
        new(Permissions.Audit.View, "ระบบ", "ดูบันทึกการตรวจสอบ", "ดูว่าใครทำอะไร เมื่อไหร่"),
        new(Permissions.Overview.View, "ระบบ", "ดูภาพรวมระบบ", "ภาพรวมการใช้งานและบันทึกคำขอของทั้งระบบ"),
        new(Permissions.Job.Trigger, "ระบบ", "สั่งงานเบื้องหลัง", "สั่งรันงานดูแลระบบด้วยมือ"),
    ];

    public static Entry? Find(string name) => Entries.FirstOrDefault(e => e.Name == name);
}

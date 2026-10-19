using System.Net;
using KPI.Application.Notifications;

namespace KPI.Infrastructure.Services;

/// <summary>Subject and HTML body of each issue e-mail (Thai). Pure, so it is easy to test.</summary>
public static class IssueEmailTemplates
{
    public static (string Subject, string Html) Build(IssueEmailMessage m, string? appBaseUrl)
    {
        var number = Encode(m.Number);
        var title = Encode(m.Title);
        var actor = Encode(m.ActorName ?? "ทีมงาน");
        var detail = m.Detail is { Length: > 0 } ? Quote(Encode(m.Detail)) : "";
        var greeting = $"สวัสดีคุณ {Encode(m.ToName)},";
        var link = $"/issues/{m.IssueId}";

        return m.Kind switch
        {
            IssueEmailKind.Received => (
                $"[{m.Number}] รับเรื่องแล้ว: {m.Title}",
                Layout("#066FD1", "รับเรื่องแจ้งปัญหาแล้ว", greeting,
                    $"<p>ระบบบันทึกเรื่อง <strong>{number}</strong> เรียบร้อย</p>{Box(title)}" +
                    $"<p>ทีม IT จะตอบรับภายใน <strong>{FormatDue(m.ResponseDueAt)}</strong> และแจ้งความคืบหน้าให้ทราบทางอีเมลนี้</p>",
                    link, appBaseUrl, "ดูสถานะ")),

            IssueEmailKind.NewForIt => (
                $"[{m.Number}] เรื่องใหม่: {m.Title}",
                Layout("#066FD1", "มีเรื่องแจ้งปัญหาใหม่", greeting,
                    $"<p><strong>{actor}</strong> แจ้งปัญหา <strong>{number}</strong>:</p>{Box(title)}" +
                    $"<p>ต้องตอบรับภายใน <strong>{FormatDue(m.ResponseDueAt)}</strong></p>",
                    link, appBaseUrl, "เปิดเรื่อง")),

            IssueEmailKind.Assigned => (
                $"[{m.Number}] มอบหมายให้คุณ: {m.Title}",
                Layout("#182433", "คุณได้รับมอบหมายงาน", greeting,
                    $"<p><strong>{actor}</strong> มอบหมายเรื่อง <strong>{number}</strong> ให้คุณ:</p>{Box(title)}",
                    link, appBaseUrl, "เปิดเรื่อง")),

            IssueEmailKind.NeedsInfo => (
                $"[{m.Number}] ทีม IT ขอข้อมูลเพิ่ม: {m.Title}",
                Layout("#C77700", "ทีม IT ต้องการข้อมูลเพิ่มเติม", greeting,
                    $"<p>เรื่อง <strong>{number}</strong> {Box(title)}</p>{detail}" +
                    "<p style=\"color:#5F656D\">กรุณาตอบกลับในระบบ เพื่อให้ทีมดำเนินการต่อได้</p>",
                    link, appBaseUrl, "ตอบกลับ")),

            IssueEmailKind.ReporterReplied => (
                $"[{m.Number}] ผู้แจ้งตอบกลับ: {m.Title}",
                Layout("#066FD1", "ผู้แจ้งตอบกลับแล้ว", greeting,
                    $"<p><strong>{actor}</strong> ตอบกลับเรื่อง <strong>{number}</strong>:</p>{Box(title)}{detail}",
                    link, appBaseUrl, "เปิดเรื่อง")),

            IssueEmailKind.StaffReplied => (
                $"[{m.Number}] ทีม IT ตอบกลับ: {m.Title}",
                Layout("#066FD1", "ทีม IT ตอบกลับเรื่องของคุณ", greeting,
                    $"<p>เรื่อง <strong>{number}</strong> {Box(title)}</p>{detail}",
                    link, appBaseUrl, "ดูข้อความ")),

            IssueEmailKind.Resolved => (
                $"[{m.Number}] แก้ไขแล้ว: {m.Title}",
                Layout("#1E9E63", "แก้ไขปัญหาแล้ว", greeting,
                    $"<p><strong>{actor}</strong> แก้ไขเรื่อง <strong>{number}</strong> เรียบร้อย:</p>{Box(title)}" +
                    $"<p><strong>วิธีแก้ไข</strong></p>{detail}" +
                    "<p>กรุณาตรวจสอบและยืนยันว่าใช้งานได้ปกติ หากยังไม่หายให้กด \"ยังไม่หาย\" ในระบบ " +
                    "ถ้าไม่มีการตอบกลับภายใน 3 วัน ระบบจะปิดเรื่องให้อัตโนมัติ</p>",
                    link, appBaseUrl, "ตรวจสอบและยืนยัน")),

            IssueEmailKind.Rejected => (
                $"[{m.Number}] ไม่ดำเนินการต่อ: {m.Title}",
                Layout("#5F656D", "เรื่องนี้ไม่ได้ดำเนินการต่อ", greeting,
                    $"<p>เรื่อง <strong>{number}</strong> {Box(title)}</p><p><strong>เหตุผล</strong></p>{detail}" +
                    "<p style=\"color:#5F656D\">หากไม่เห็นด้วย ตอบกลับหรือแจ้งเรื่องใหม่พร้อมรายละเอียดเพิ่มเติมได้</p>",
                    link, appBaseUrl, "ดูรายละเอียด")),

            IssueEmailKind.Reopened => (
                $"[{m.Number}] เปิดเรื่องใหม่: {m.Title}",
                Layout("#E24B4A", "เรื่องถูกเปิดใหม่", greeting,
                    $"<p><strong>{actor}</strong> แจ้งว่าเรื่อง <strong>{number}</strong> ยังไม่หาย:</p>{Box(title)}{detail}",
                    link, appBaseUrl, "เปิดเรื่อง")),

            _ => throw new ArgumentOutOfRangeException(nameof(m), m.Kind, "Unknown e-mail kind."),
        };
    }

    /// <summary>How long until the deadline, in plain Thai.</summary>
    public static string FormatDue(DateTime dueUtc)
    {
        var left = dueUtc - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) return "ทันที";
        if (left.TotalHours < 1) return $"{Math.Max(1, (int)Math.Round(left.TotalMinutes))} นาที";
        if (left.TotalHours < 48) return $"{Math.Round(left.TotalHours)} ชั่วโมง";
        return $"{Math.Round(left.TotalDays)} วัน";
    }

    /// <summary>A mail header value must be a single line.</summary>
    public static string SingleLine(string subject) =>
        System.Text.RegularExpressions.Regex.Replace(subject, @"\s+", " ").Trim();

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Box(string encoded) =>
        $"<p style=\"background:#F6F8FB;padding:12px 14px;border-radius:10px\"><strong>{encoded}</strong></p>";

    private static string Quote(string encoded) =>
        $"<p style=\"border-left:3px solid #066FD1;margin:0;padding:6px 12px;white-space:pre-wrap\">{encoded}</p>";

    private static string Layout(string color, string heading, string greeting, string body,
        string path, string? appBaseUrl, string buttonText)
    {
        var button = string.IsNullOrWhiteSpace(appBaseUrl)
            ? ""
            : $"<p><a href=\"{WebUtility.HtmlEncode(appBaseUrl.TrimEnd('/'))}{path}\" " +
              "style=\"display:inline-block;background:#066FD1;color:#fff;padding:10px 18px;border-radius:10px;text-decoration:none\">" +
              $"{buttonText}</a></p>";

        return $"""
            <div style="font-family:'Noto Sans Thai',Segoe UI,sans-serif;max-width:520px;margin:0 auto;color:#182433">
              <h2 style="color:{color};margin-bottom:4px">{heading}</h2>
              <p>{greeting}</p>
              {body}
              {button}
            </div>
            """;
    }
}

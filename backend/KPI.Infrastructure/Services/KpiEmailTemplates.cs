using System.Net;
using KPI.Application.Notifications;

namespace KPI.Infrastructure.Services;

/// <summary>Builds the subject and HTML body of each KPI e-mail. Pure, so it is easy to test.</summary>
public static class KpiEmailTemplates
{
    public static (string Subject, string Html) Build(KpiEmailMessage m, string? appBaseUrl)
    {
        var kpi = Encode(m.KpiName);
        var greeting = $"Hi {Encode(m.ToName)},";
        var actor = Encode(m.ActorName ?? "Someone");
        var owner = Encode(m.OwnerName ?? "the owner");
        var department = m.DepartmentName is { Length: > 0 } ? $" ({Encode(m.DepartmentName)})" : "";
        var due = m.DueDate.ToString("d MMM yyyy");
        var status = $"Progress {m.Progress:0.##} of {m.Target:0.##} · due {due}";

        return m.Kind switch
        {
            KpiEmailKind.Created => (
                $"New KPI created: {m.KpiName}",
                Layout("#534AB7", "New KPI created 📋", greeting,
                    $"<p><strong>{actor}</strong> created a new KPI in your department{department}:</p>{Box(kpi)}" +
                    "<p style=\"color:#666\">It stays a draft until it is submitted for approval.</p>",
                    m.KpiId, appBaseUrl)),

            KpiEmailKind.Submitted => (
                $"KPI pending approval: {m.KpiName}",
                Layout("#EF9F27", "KPI awaiting approval ⏳", greeting,
                    $"<p><strong>{actor}</strong> submitted a KPI for your approval{department}:</p>{Box(kpi)}" +
                    "<p style=\"color:#666\">Please review and approve or send it back.</p>",
                    m.KpiId, appBaseUrl)),

            KpiEmailKind.Approved => (
                $"KPI approved: {m.KpiName}",
                Layout("#1D9E75", "KPI approved ✅", greeting,
                    $"<p>Your KPI {Box(kpi)} was approved by <strong>{actor}</strong> and is now Active.</p>" +
                    "<p style=\"color:#666\">You can start reporting progress.</p>",
                    m.KpiId, appBaseUrl)),

            KpiEmailKind.Rejected => (
                $"KPI returned for changes: {m.KpiName}",
                Layout("#E24B4A", "KPI returned for changes ↩️", greeting,
                    $"<p><strong>{actor}</strong> sent your KPI back to draft:</p>{Box(kpi)}" +
                    $"<p><strong>Reason:</strong> {Encode(m.Reason ?? "(none given)")}</p>" +
                    "<p style=\"color:#666\">Update it and submit it again.</p>",
                    m.KpiId, appBaseUrl)),

            KpiEmailKind.AtRisk => (
                $"KPI at risk: {m.KpiName}",
                Layout("#EF9F27", "KPI at risk ⚠️", greeting,
                    $"<p>The KPI {Box(kpi)} is behind the expected pace{department}.</p><p>{status}</p>" +
                    "<p style=\"color:#666\">Please update the progress or talk to your manager.</p>",
                    m.KpiId, appBaseUrl)),

            KpiEmailKind.Delayed => (
                $"KPI delayed: {m.KpiName}",
                Layout("#E24B4A", "KPI delayed 🚨", greeting,
                    $"<p>The KPI {Box(kpi)} is past its due date{department}.</p><p>{status}</p>" +
                    "<p style=\"color:#666\">Please update the progress or contact your manager.</p>",
                    m.KpiId, appBaseUrl)),

            KpiEmailKind.Completed => (
                $"KPI completed: {m.KpiName}",
                Layout("#1D9E75", "KPI completed 🎉", greeting,
                    $"<p>The KPI {Box(kpi)} owned by <strong>{owner}</strong> reached its target{department}.</p>" +
                    "<p style=\"color:#666\">It can now be closed by a director.</p>",
                    m.KpiId, appBaseUrl)),

            _ => throw new ArgumentOutOfRangeException(nameof(m), m.Kind, "Unknown e-mail kind."),
        };
    }

    /// <summary>Header values must be a single line.</summary>
    public static string SingleLine(string subject) =>
        System.Text.RegularExpressions.Regex.Replace(subject, @"\s+", " ").Trim();

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Box(string encodedName) =>
        $"<p style=\"background:#f5f5f5;padding:12px;border-radius:8px\"><strong>{encodedName}</strong></p>";

    private static string Layout(string color, string title, string greeting, string body, Guid kpiId, string? appBaseUrl)
    {
        var link = string.IsNullOrWhiteSpace(appBaseUrl)
            ? ""
            : $"<p><a href=\"{WebUtility.HtmlEncode(appBaseUrl.TrimEnd('/'))}/kpi/{kpiId}\" " +
              "style=\"display:inline-block;background:#534AB7;color:#fff;padding:10px 16px;border-radius:8px;text-decoration:none\">Open KPI</a></p>";

        return $"""
            <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
              <h2 style="color:{color}">{title}</h2>
              <p>{greeting}</p>
              {body}
              {link}
            </div>
            """;
    }
}

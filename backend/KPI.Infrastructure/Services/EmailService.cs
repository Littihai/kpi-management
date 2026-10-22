using KPI.Application.Notifications;
using KPI.Application.Settings;
using KPI.Domain.Settings;
using Microsoft.Extensions.Configuration;

namespace KPI.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IReadOnlyList<IMailTransport> _transports;
    private readonly ISystemSettings _settings;
    private readonly string? _appBaseUrl;

    public EmailService(IEnumerable<IMailTransport> transports, ISystemSettings settings, IConfiguration config)
    {
        _transports = transports.ToList();
        _settings = settings;
        _appBaseUrl = config["App:BaseUrl"];
    }

    public async Task SendIssueAsync(IssueEmailMessage message, CancellationToken cancellationToken = default)
    {
        var (subject, html) = IssueEmailTemplates.Build(message, _appBaseUrl);
        await SendAsync(new OutgoingMail(message.ToEmail, IssueEmailTemplates.SingleLine(subject), html), cancellationToken);
    }

    public async Task SendTestAsync(string toEmail, CancellationToken cancellationToken = default)
    {
        var company = (await _settings.GetAsync(SettingCatalog.Keys.CompanyName)).Trim();
        var system = company.Length > 0 ? $"Helpdesk {company}" : "Helpdesk";
        await SendAsync(new OutgoingMail(toEmail, $"ทดสอบการส่งอีเมลจาก {system}",
            $"<p>นี่คืออีเมลทดสอบจากระบบ {System.Net.WebUtility.HtmlEncode(system)}</p><p>ถ้าคุณเห็นข้อความนี้ แสดงว่าการตั้งค่าอีเมลใช้งานได้</p>"), cancellationToken);
    }

    private async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        var provider = await _settings.GetAsync(SettingCatalog.Keys.EmailProvider);
        var transport = _transports.FirstOrDefault(t => t.Name == provider)
            ?? throw new InvalidOperationException($"No e-mail transport named '{provider}'");
        await transport.SendAsync(mail, cancellationToken);
    }
}

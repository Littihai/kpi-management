using KPI.Application.Settings;
using KPI.Domain.Exceptions;
using KPI.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace KPI.Infrastructure.Services;

public sealed record OutgoingMail(string ToEmail, string Subject, string HtmlBody);

/// <summary>A way of getting a message out of the building. SMTP is the only one today.</summary>
public interface IMailTransport
{
    string Name { get; }
    Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken);
}

/// <summary>Any SMTP server, set up in the settings screen (host, port, encryption, login, sender).</summary>
public sealed class SmtpTransport : IMailTransport
{
    private readonly ISystemSettings _settings;

    public SmtpTransport(ISystemSettings settings) => _settings = settings;

    public string Name => "smtp";

    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        var host = await _settings.GetAsync(SettingCatalog.Keys.SmtpHost);
        var fromAddress = await _settings.GetAsync(SettingCatalog.Keys.SmtpFromAddress);
        if (host.Length == 0 || fromAddress.Length == 0)
            throw new InvalidStateException("ยังไม่ได้ตั้งค่าเซิร์ฟเวอร์ SMTP หรือที่อยู่ผู้ส่ง");

        var port = (int)await _settings.GetNumberAsync(SettingCatalog.Keys.SmtpPort);
        var security = await _settings.GetAsync(SettingCatalog.Keys.SmtpSecurity) switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        var username = await _settings.GetAsync(SettingCatalog.Keys.SmtpUsername);
        var password = await _settings.GetAsync(SettingCatalog.Keys.SmtpPassword);
        var fromName = await _settings.GetAsync(SettingCatalog.Keys.SmtpFromName);
        var timeout = (int)await _settings.GetNumberAsync(SettingCatalog.Keys.SmtpTimeoutSeconds) * 1000;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(mail.ToEmail));
        message.Subject = mail.Subject;
        message.Body = new BodyBuilder { HtmlBody = mail.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = timeout };
        try
        {
            await client.ConnectAsync(host, port, security, cancellationToken);
            if (username.Length > 0) await client.AuthenticateAsync(username, password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            if (client.IsConnected) await client.DisconnectAsync(true, CancellationToken.None);
        }
    }
}

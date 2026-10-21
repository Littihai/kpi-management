using KPI.Application.Settings;
using KPI.Domain.Exceptions;
using KPI.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Resend;

namespace KPI.Infrastructure.Services;

public sealed record OutgoingMail(string ToEmail, string Subject, string HtmlBody);

/// <summary>One way of getting a message out of the building. EmailService picks one by the "email.provider" setting.</summary>
public interface IMailTransport
{
    string Name { get; }
    Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken);
}

/// <summary>Resend's HTTP API. The key and sender are configured on the server (Resend__ApiKey, Resend__From).</summary>
public sealed class ResendTransport : IMailTransport
{
    // Resend only delivers from this address to the account owner. Set Resend__From to an address on a
    // verified domain (e.g. "Helpdesk <noreply@yourcompany.com>") to e-mail everyone else.
    private const string DefaultFrom = "Helpdesk <onboarding@resend.dev>";

    private readonly IResend _resend;
    private readonly string _from;

    public ResendTransport(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _from = config["Resend:From"] is { Length: > 0 } from ? from : DefaultFrom;
    }

    public string Name => "resend";

    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        var email = new EmailMessage { From = _from, To = { mail.ToEmail }, Subject = mail.Subject, HtmlBody = mail.HtmlBody };
        await _resend.EmailSendAsync(email, cancellationToken);
    }
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

using KPI.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Resend;

namespace KPI.Infrastructure.Services;

public class EmailService : IEmailService
{
    // Resend only delivers from this address to the account owner. Set Resend__From to an address on a
    // verified domain (e.g. "Helpdesk <noreply@yourcompany.com>") to e-mail everyone else.
    private const string DefaultFrom = "Helpdesk <onboarding@resend.dev>";

    private readonly IResend _resend;
    private readonly string _from;
    private readonly string? _appBaseUrl;

    public EmailService(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _from = config["Resend:From"] is { Length: > 0 } from ? from : DefaultFrom;
        _appBaseUrl = config["App:BaseUrl"];
    }

    public async Task SendIssueAsync(IssueEmailMessage message, CancellationToken cancellationToken = default)
    {
        var (subject, html) = IssueEmailTemplates.Build(message, _appBaseUrl);

        var email = new EmailMessage
        {
            From = _from,
            To = { message.ToEmail },
            Subject = IssueEmailTemplates.SingleLine(subject),
            HtmlBody = html,
        };

        await _resend.EmailSendAsync(email, cancellationToken);
    }
}

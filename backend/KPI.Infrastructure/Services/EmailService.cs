using KPI.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Resend;

namespace KPI.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IResend _resend;
    private readonly string _from;

    public EmailService(IResend resend, IConfiguration config)
    {
        _resend = resend;
        _from = "KPI System <onboarding@resend.dev>";
    }

    public async Task SendKpiApprovedAsync(string toEmail, string fullName, string kpiName)
    {
        var message = new EmailMessage
        {
            From = _from,
            To = { toEmail },
            Subject = $"KPI Approved: {kpiName}",
            HtmlBody = $"""
                <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
                  <h2 style="color:#534AB7">KPI Approved ✅</h2>
                  <p>Hi {fullName},</p>
                  <p>Your KPI <strong>{kpiName}</strong> has been approved and is now Active.</p>
                  <p style="color:#666">You can now start tracking progress.</p>
                </div>
            """
        };
        await _resend.EmailSendAsync(message);
    }

    public async Task SendKpiDelayedAsync(string toEmail, string fullName, string kpiName, int daysDelayed)
    {
        var message = new EmailMessage
        {
            From = _from,
            To = { toEmail },
            Subject = $"⚠️ KPI Delayed: {kpiName}",
            HtmlBody = $"""
                <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
                  <h2 style="color:#E24B4A">KPI Delayed ⚠️</h2>
                  <p>Hi {fullName},</p>
                  <p>Your KPI <strong>{kpiName}</strong> is delayed by <strong>{daysDelayed} days</strong>.</p>
                  <p style="color:#666">Please update the progress or contact your manager.</p>
                </div>
            """
        };
        await _resend.EmailSendAsync(message);
    }

    public async Task SendTaskAssignedAsync(string toEmail, string fullName, string taskName)
    {
        var message = new EmailMessage
        {
            From = _from,
            To = { toEmail },
            Subject = $"New Task Assigned: {taskName}",
            HtmlBody = $"""
                <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
                  <h2 style="color:#534AB7">New Task Assigned 📋</h2>
                  <p>Hi {fullName},</p>
                  <p>You have been assigned to task <strong>{taskName}</strong>.</p>
                  <p style="color:#666">Please log in to view the details.</p>
                </div>
            """
        };
        await _resend.EmailSendAsync(message);
    }

    public async Task SendKpiCreatedAsync(string toEmail, string managerName, string kpiName, string ownerName)
{
    var message = new EmailMessage
    {
        From = _from,
        To = { toEmail },
        Subject = $"New KPI Created: {kpiName}",
        HtmlBody = $"""
            <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
              <h2 style="color:#534AB7">New KPI Created 📋</h2>
              <p>Hi {managerName},</p>
              <p><strong>{ownerName}</strong> has created a new KPI:</p>
              <p style="background:#f5f5f5;padding:12px;border-radius:8px">
                <strong>{kpiName}</strong>
              </p>
              <p style="color:#666">Please review and submit for approval when ready.</p>
            </div>
        """
    };
    await _resend.EmailSendAsync(message);
}

    public async Task SendKpiSubmittedAsync(string toEmail, string directorName, string kpiName, string ownerName)
    {
        var message = new EmailMessage
        {
            From = _from,
            To = { toEmail },
            Subject = $"KPI Pending Approval: {kpiName}",
            HtmlBody = $"""
                <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
                <h2 style="color:#EF9F27">KPI Awaiting Approval ⏳</h2>
                <p>Hi {directorName},</p>
                <p><strong>{ownerName}</strong> has submitted a KPI for your approval:</p>
                <p style="background:#f5f5f5;padding:12px;border-radius:8px">
                    <strong>{kpiName}</strong>
                </p>
                <p style="color:#666">Please log in to review and approve.</p>
                </div>
            """
        };
        await _resend.EmailSendAsync(message);
    }
}
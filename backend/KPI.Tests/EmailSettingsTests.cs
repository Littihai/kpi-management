using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using KPI.Application.Notifications;
using KPI.Application.Settings;
using KPI.Domain.Constants;
using KPI.Domain.Exceptions;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace KPI.Tests;

public class SecretProtectorTests
{
    [Fact]
    public void A_secret_round_trips_and_is_not_stored_in_the_clear()
    {
        var protector = new SecretProtector("unit-test-signing-key-0123456789");

        var stored = protector.Protect("p@ss w0rd ไทย");

        Assert.DoesNotContain("p@ss", stored);
        Assert.Equal("p@ss w0rd ไทย", protector.Unprotect(stored));
        Assert.NotEqual(stored, protector.Protect("p@ss w0rd ไทย"));   // a fresh nonce every time
    }

    [Fact]
    public void A_value_from_another_key_or_a_tampered_one_is_not_readable()
    {
        var stored = new SecretProtector("unit-test-signing-key-0123456789").Protect("secret");

        Assert.Null(new SecretProtector("a-different-signing-key-0123456789").Unprotect(stored));
        var tampered = stored[..^3] + (stored.EndsWith("AAA") ? "BBB" : "AAA");
        Assert.Null(new SecretProtector("unit-test-signing-key-0123456789").Unprotect(tampered));
        Assert.Null(new SecretProtector("unit-test-signing-key-0123456789").Unprotect("plain text"));
        Assert.Null(new SecretProtector("unit-test-signing-key-0123456789").Unprotect("enc1:not base64!"));
    }
}

public class EmailSettingValidationTests
{
    private static string Normalize(string key, string value) => SettingCatalog.Normalize(SettingCatalog.Find(key)!, value);

    [Theory]
    [InlineData("smtp.office365.com")]
    [InlineData("mail.example.co.th")]
    [InlineData("192.168.1.20")]
    [InlineData("localhost")]
    [InlineData("")]
    public void A_host_name_or_ip_is_accepted(string host) => Assert.Equal(host, Normalize(SettingCatalog.Keys.SmtpHost, host));

    [Theory]
    [InlineData("https://smtp.example.com")]
    [InlineData("smtp.example.com:587")]
    [InlineData("smtp example.com")]
    [InlineData("-bad.example.com")]
    [InlineData("a/b")]
    public void Something_that_is_not_a_bare_host_is_refused(string host) =>
        Assert.Throws<RequestValidationException>(() => Normalize(SettingCatalog.Keys.SmtpHost, host));

    [Theory]
    [InlineData("noreply@example.com", true)]
    [InlineData("", true)]
    [InlineData("Helpdesk <noreply@example.com>", false)]
    [InlineData("not-an-address", false)]
    [InlineData("a@b.com, c@d.com", false)]
    public void The_sender_must_be_a_plain_address(string value, bool ok)
    {
        if (ok) Assert.Equal(value, Normalize(SettingCatalog.Keys.SmtpFromAddress, value));
        else Assert.Throws<RequestValidationException>(() => Normalize(SettingCatalog.Keys.SmtpFromAddress, value));
    }

    [Theory]
    [InlineData(SettingCatalog.Keys.SmtpSecurity, "tls")]
    [InlineData(SettingCatalog.Keys.SmtpPort, "0")]
    [InlineData(SettingCatalog.Keys.SmtpPort, "70000")]
    [InlineData(SettingCatalog.Keys.SmtpTimeoutSeconds, "1")]
    public void Choices_and_ranges_are_enforced(string key, string value) =>
        Assert.Throws<RequestValidationException>(() => Normalize(key, value));

    [Fact]
    public void Only_the_password_is_sensitive() =>
        Assert.Equal([SettingCatalog.Keys.SmtpPassword], SettingCatalog.All.Where(d => d.Sensitive).Select(d => d.Key));
}

public class EmailSecretsAdminTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public Guid AdminId { get; private set; }
        public ISystemSettings Settings => _provider.GetRequiredService<ISystemSettings>();
        public IServiceScope Scope() => _provider.CreateScope();

        private Fixture(ServiceProvider provider) => _provider = provider;

        public static async Task<Fixture> CreateAsync()
        {
            var name = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
            services.AddSingleton<ISecretProtector>(new SecretProtector("unit-test-signing-key-0123456789"));
            services.AddSingleton<ISystemSettings, SystemSettingsService>();
            services.AddScoped<ISettingsAdminService, SettingsAdminService>();
            var f = new Fixture(services.BuildServiceProvider());
            using var scope = f.Scope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DbSeeder.SeedAsync(db);
            var admin = new KPI.Domain.Entities.User
            {
                FirstName = "Ada", LastName = "Min", Email = "ada@example.com", PasswordHash = "x", IsActive = true,
                RoleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.SuperAdmin)).Id,
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            f.AdminId = admin.Id;
            return f;
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    [Fact]
    public async Task The_smtp_password_is_stored_encrypted_read_back_in_plain_inside_the_server_and_never_sent_out()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Scope();
        var admin = scope.ServiceProvider.GetRequiredService<ISettingsAdminService>();

        var saved = await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.SmtpPassword, new SaveSettingRequest("S3cret!pass"));

        Assert.True(saved.Sensitive);
        Assert.Equal("", saved.Value);                     // not sent back, but "not default" says one is set
        Assert.False(saved.IsDefault);
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppSettings.SingleAsync();
        Assert.StartsWith("enc1:", row.Value);
        Assert.DoesNotContain("S3cret", row.Value);
        Assert.Equal("S3cret!pass", await f.Settings.GetAsync(SettingCatalog.Keys.SmtpPassword));
        var listed = (await admin.ListAsync()).Single(s => s.Key == SettingCatalog.Keys.SmtpPassword);
        Assert.Equal("", listed.Value);
        Assert.False(listed.IsDefault);
    }

    [Fact]
    public async Task The_history_of_a_secret_never_contains_it()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Scope();
        var admin = scope.ServiceProvider.GetRequiredService<ISettingsAdminService>();

        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.SmtpPassword, new SaveSettingRequest("first-secret"));
        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.SmtpPassword, new SaveSettingRequest("second-secret"));
        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.SmtpPassword, new SaveSettingRequest("second-secret")); // no change

        var history = await admin.HistoryAsync(SettingCatalog.Keys.SmtpPassword);
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.DoesNotContain("secret", (h.OldValue ?? "") + (h.NewValue ?? "")));
        Assert.Equal("••••••••", history[0].NewValue);
        Assert.Equal("", history[1].OldValue);
    }

    [Fact]
    public async Task Clearing_the_password_removes_it()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Scope();
        var admin = scope.ServiceProvider.GetRequiredService<ISettingsAdminService>();
        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.SmtpPassword, new SaveSettingRequest("abc"));

        var cleared = await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.SmtpPassword, new SaveSettingRequest(""));

        Assert.True(cleared.IsDefault);
        Assert.Equal("", await f.Settings.GetAsync(SettingCatalog.Keys.SmtpPassword));
    }

    [Fact]
    public async Task A_password_stored_under_another_key_is_treated_as_not_set()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AppSettings.Add(new KPI.Domain.Entities.AppSetting
        {
            Key = SettingCatalog.Keys.SmtpPassword, Value = new SecretProtector("another-signing-key-0123456789abc").Protect("old"),
        });
        await db.SaveChangesAsync();
        f.Settings.Invalidate();

        Assert.Equal("", await f.Settings.GetAsync(SettingCatalog.Keys.SmtpPassword));
    }
}

public class EmailServiceTests
{
    private sealed class FakeTransport : IMailTransport
    {
        public string Name => "smtp";
        public List<OutgoingMail> Sent { get; } = [];
        public Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken) { Sent.Add(mail); return Task.CompletedTask; }
    }

    private static IssueEmailMessage Message() =>
        new(IssueEmailKind.Received, "user@example.com", "User", Guid.NewGuid(), "ISS-2569-0001", "เครื่องพิมพ์เสีย", null, null, DateTime.UtcNow.AddHours(2));

    [Fact]
    public async Task Issue_mail_goes_out_through_the_transport()
    {
        var transport = new FakeTransport();
        var service = new EmailService(transport, new DefaultSettings(), new ConfigurationBuilder().Build());

        await service.SendIssueAsync(Message());

        var sent = Assert.Single(transport.Sent);
        Assert.Equal("user@example.com", sent.ToEmail);
        Assert.Contains("ISS-2569-0001", sent.Subject);
    }

    [Fact]
    public async Task The_test_message_goes_out_through_the_transport()
    {
        var transport = new FakeTransport();
        var service = new EmailService(transport, new DefaultSettings(), new ConfigurationBuilder().Build());

        await service.SendTestAsync("admin@example.com");

        var sent = Assert.Single(transport.Sent);
        Assert.Equal("admin@example.com", sent.ToEmail);
        Assert.Contains("ทดสอบ", sent.Subject);
    }
}

/// <summary>
/// A tiny SMTP server on this machine that serves one connection after another. Plain by default; given a certificate it
/// offers STARTTLS (or, with <c>implicitTls</c>, speaks TLS from the first byte).
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _loop;
    private readonly X509Certificate2? _certificate;
    private readonly bool _implicitTls;
    private readonly string _greeting;
    private readonly string[] _extensions;
    public List<string> Lines { get; } = [];
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <param name="certificate">Turns on encryption with this certificate.</param>
    /// <param name="implicitTls">With a certificate: TLS at once instead of after STARTTLS.</param>
    /// <param name="greeting">The first line the server sends; anything but 220 is a server that refuses the client at once.</param>
    /// <param name="extensions">What EHLO lists besides the greeting, for example "AUTH PLAIN LOGIN".</param>
    public FakeSmtpServer(X509Certificate2? certificate = null, bool implicitTls = false,
        string greeting = "220 fake ESMTP ready", params string[] extensions)
    {
        _certificate = certificate;
        _implicitTls = implicitTls;
        _greeting = greeting;
        _extensions = extensions;
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    /// <summary>A certificate no machine trusts, for the given name and for 127.0.0.1.</summary>
    public static X509Certificate2 SelfSignedCertificate(string dnsName, bool coversLoopback)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        if (coversLoopback) names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }

    public string[] Received()
    {
        lock (Lines) return [.. Lines];
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (Exception) { /* the listener was stopped: the test is over */ }
    }

    private async Task<Stream> UpgradeAsync(Stream stream)
    {
        var tls = new SslStream(stream, leaveInnerStreamOpen: false);
        await tls.AuthenticateAsServerAsync(_certificate!);
        return tls;
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            using var connection = client;
            Stream stream = client.GetStream();
            if (_certificate is not null && _implicitTls) stream = await UpgradeAsync(stream);
            var secure = _certificate is not null && _implicitTls;
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync(_greeting);
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (Lines) Lines.Add(line);
                if (inData)
                {
                    if (line == ".") { inData = false; await writer.WriteLineAsync("250 queued"); }
                    continue;
                }
                var cmd = line.ToUpperInvariant();
                if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO"))
                {
                    var listed = _certificate is not null && !secure ? _extensions.Append("STARTTLS").ToArray() : _extensions;
                    if (listed.Length == 0) await writer.WriteLineAsync("250 fake");
                    else
                    {
                        await writer.WriteLineAsync("250-fake");
                        for (var i = 0; i < listed.Length; i++)
                            await writer.WriteLineAsync((i == listed.Length - 1 ? "250 " : "250-") + listed[i]);
                    }
                }
                else if (cmd == "STARTTLS" && _certificate is not null && !secure)
                {
                    await writer.WriteLineAsync("220 ready to start TLS");
                    stream = await UpgradeAsync(stream);
                    reader = new StreamReader(stream, Encoding.ASCII);
                    writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                    secure = true;
                }
                else if (cmd.StartsWith("AUTH")) await writer.WriteLineAsync("535 5.7.8 Username and Password not accepted");
                else if (cmd == "DATA") { inData = true; await writer.WriteLineAsync("354 go ahead"); }
                else if (cmd == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 ok");
            }
        }
        catch (Exception) { /* the client left, or the test is over */ }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await Task.WhenAny(_loop, Task.Delay(1000));
    }
}

/// <summary>The real MailKit path against a tiny SMTP server listening on this machine.</summary>
public class SmtpTransportTests
{
    private static DefaultSettings Configured(int port) => new DefaultSettings()
        .Set(SettingCatalog.Keys.SmtpHost, "127.0.0.1")
        .Set(SettingCatalog.Keys.SmtpPort, port.ToString())
        .Set(SettingCatalog.Keys.SmtpSecurity, "none")
        .Set(SettingCatalog.Keys.SmtpFromAddress, "noreply@example.com")
        .Set(SettingCatalog.Keys.SmtpFromName, "Helpdesk");

    [Fact]
    public async Task A_message_reaches_the_smtp_server_with_the_right_sender_recipient_and_subject()
    {
        await using var server = new FakeSmtpServer();
        var transport = new SmtpTransport(Configured(server.Port));

        await transport.SendAsync(new OutgoingMail("someone@example.org", "Hello test", "<p>Hi there</p>"), CancellationToken.None);

        string[] lines;
        lock (server.Lines) lines = [.. server.Lines];
        Assert.Contains(lines, l => l.StartsWith("MAIL FROM:<noreply@example.com>", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.StartsWith("RCPT TO:<someone@example.org>", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l == "Subject: Hello test");
        Assert.Contains(lines, l => l.StartsWith("From: Helpdesk", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Hi there"));
    }

    [Fact]
    public async Task A_missing_host_or_sender_is_reported_before_any_connection_is_tried()
    {
        var transport = new SmtpTransport(new DefaultSettings());

        var ex = await Assert.ThrowsAsync<InvalidStateException>(() =>
            transport.SendAsync(new OutgoingMail("a@b.com", "s", "b"), CancellationToken.None));
        Assert.Contains("SMTP", ex.Message);
    }

    [Fact]
    public async Task A_server_that_is_not_there_fails_fast_with_an_error_instead_of_hanging()
    {
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        var transport = new SmtpTransport(Configured(port).Set(SettingCatalog.Keys.SmtpTimeoutSeconds, "3"));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            transport.SendAsync(new OutgoingMail("a@b.com", "s", "b"), CancellationToken.None));
    }
}

/// <summary>The plain-words explanation of why a message could not be sent, from real exceptions.</summary>
public class EmailErrorsTests
{
    private const string Host = "smtp.example.com";

    private static string Describe(Exception exception, int port = 587, string security = "starttls") =>
        EmailErrors.Describe(exception, Host, port, security);

    [Fact]
    public void A_timeout_or_a_refused_connection_says_the_server_cannot_be_reached()
    {
        foreach (var exception in new Exception[]
                 {
                     new TimeoutException(),
                     new SocketException((int)SocketError.TimedOut),
                     new SocketException((int)SocketError.ConnectionRefused),
                     new SocketException((int)SocketError.NetworkUnreachable),
                     new IOException("Unable to read data", new SocketException((int)SocketError.TimedOut)),
                 })
        {
            var text = Describe(exception);

            Assert.Contains("ติดต่อเซิร์ฟเวอร์อีเมล smtp.example.com:587 ไม่ได้", text);
            Assert.Contains("ไฟร์วอลล์", text);
            Assert.Contains("พอร์ต 587", text);
            Assert.EndsWith(")", text);   // the technical tail
        }
    }

    [Fact]
    public void A_name_that_does_not_resolve_is_told_apart_from_a_blocked_port()
    {
        var text = Describe(new SocketException((int)SocketError.HostNotFound));

        Assert.Contains("ไม่พบเครื่องชื่อ smtp.example.com", text);
        Assert.DoesNotContain("ไฟร์วอลล์", text);
        Assert.Contains("SocketException", text);
    }

    [Fact]
    public void A_refused_login_says_to_check_the_user_and_password()
    {
        foreach (var exception in new Exception[]
                 {
                     new MailKit.Security.AuthenticationException("535: 5.7.8 Username and Password not accepted"),
                     new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.AuthenticationInvalidCredentials, "535 5.7.8 not accepted"),
                 })
        {
            var text = Describe(exception);

            Assert.Contains("ปฏิเสธการเข้าสู่ระบบ", text);
            Assert.Contains("ชื่อผู้ใช้และรหัสผ่าน", text);
        }
    }

    [Fact]
    public void A_server_that_wants_a_login_first_says_so()
    {
        foreach (var exception in new Exception[]
                 {
                     new MailKit.ServiceNotAuthenticatedException("authenticate first"),
                     new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.AuthenticationRequired, "530 5.7.0 Authentication required"),
                 })
            Assert.Contains("ต้องการให้เข้าสู่ระบบก่อนส่งอีเมล", Describe(exception));
    }

    [Fact]
    public void A_refused_sender_or_recipient_says_which_address_to_check()
    {
        var sender = Describe(new SmtpCommandException(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable,
            new MimeKit.MailboxAddress("", "noreply@example.com"), "550 5.7.60 Client does not have permissions to send as this sender"));
        var recipient = Describe(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable,
            new MimeKit.MailboxAddress("", "who@example.org"), "550 5.1.1 User unknown"));

        Assert.Contains("ไม่ยอมรับที่อยู่ผู้ส่ง", sender);
        Assert.Contains("ที่อยู่ผู้ส่ง", sender);
        Assert.Contains("ไม่ยอมรับอีเมลผู้รับ", recipient);
        Assert.Contains("550", sender);   // the code stays in the technical tail
    }

    [Fact]
    public void A_refusal_that_is_not_about_an_address_does_not_send_the_administrator_to_check_addresses()
    {
        foreach (var status in new[] { SmtpStatusCode.MailboxUnavailable, SmtpStatusCode.MailboxNameNotAllowed, SmtpStatusCode.TransactionFailed })
        {
            var text = Describe(new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, status, $"{(int)status} refused"));

            Assert.Contains($"ปฏิเสธคำขอ (รหัส {(int)status})", text);
            Assert.Contains("ผู้ดูแลระบบอีเมล", text);
            Assert.DoesNotContain("ผู้ส่ง", text);
            Assert.DoesNotContain("ผู้รับ", text);
        }
    }

    [Fact]
    public void A_server_without_STARTTLS_says_to_choose_no_encryption_or_ask_the_mail_administrator()
    {
        var text = Describe(new NotSupportedException("The SMTP server does not support the STARTTLS extension."));

        Assert.Contains("ไม่รองรับ STARTTLS", text);
        Assert.Contains("\"ไม่เข้ารหัส\"", text);
        Assert.Contains("ผู้ดูแลระบบอีเมล", text);
    }

    [Fact]
    public void A_server_that_offers_no_login_is_told_apart_from_one_that_refuses_it()
    {
        var text = Describe(new NotSupportedException("The SMTP server does not support authentication."), security: "none");

        Assert.Contains("ลบชื่อผู้ใช้", text);
        Assert.DoesNotContain("ปฏิเสธการเข้าสู่ระบบ", text);
    }

    [Fact]
    public void A_failed_handshake_that_saw_no_certificate_points_at_the_encryption_choice_and_the_port()
    {
        var text = Describe(new SslHandshakeException("An error occurred while attempting to establish an SSL or TLS connection."), port: 465, security: "starttls");

        Assert.Contains("ตั้งการเข้ารหัสกับเซิร์ฟเวอร์ไม่สำเร็จ", text);
        Assert.Contains("ตอนนี้ตั้งพอร์ต 465 กับ \"STARTTLS\"", text);
        Assert.DoesNotContain("ตอนนี้ตั้งพอร์ต", Describe(new SslHandshakeException("x"), port: 465, security: "ssl"));
    }

    [Fact]
    public void A_dropped_connection_points_at_the_encryption_choice()
    {
        foreach (var exception in new Exception[]
                 {
                     new SmtpProtocolException("The SMTP server has unexpectedly disconnected."),
                     new IOException("The connection was closed", new SocketException((int)SocketError.ConnectionReset)),
                     new SocketException((int)SocketError.ConnectionReset),
                 })
        {
            var text = Describe(exception, port: 465, security: "none");

            Assert.Contains("ตัดการเชื่อมต่อ", text);
            Assert.Contains("ตอนนี้ตั้งพอร์ต 465 กับ \"ไม่เข้ารหัส\"", text);
        }
    }

    [Fact]
    public void An_error_nobody_recognises_reads_as_its_own_message_cut_to_a_reasonable_length()
    {
        Assert.Equal("boom", Describe(new InvalidOperationException("boom")));
        Assert.Equal("ยังไม่ได้ตั้งค่าเซิร์ฟเวอร์ SMTP หรือที่อยู่ผู้ส่ง", Describe(new InvalidStateException("ยังไม่ได้ตั้งค่าเซิร์ฟเวอร์ SMTP หรือที่อยู่ผู้ส่ง")));
        Assert.Equal(300, Describe(new InvalidOperationException(new string('x', 1000))).Length);
        Assert.Null(EmailErrors.Explain(new InvalidOperationException("boom"), Host, 587, "starttls"));
    }

    [Fact]
    public void The_technical_tail_keeps_the_type_the_first_line_and_the_status_code()
    {
        var text = Describe(new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.AuthenticationInvalidCredentials,
            "535 5.7.8 bad credentials\r\nsecond line that is not shown"));

        Assert.EndsWith("(SmtpCommandException 535: 535 5.7.8 bad credentials)", text);
    }

    [Fact]
    public void An_exception_wrapped_by_the_task_machinery_is_read_through()
    {
        var text = Describe(new AggregateException(new TimeoutException()));

        Assert.Contains("ติดต่อเซิร์ฟเวอร์อีเมล", text);
    }

    // The same sentences from the real thing: MailKit talking to servers on this machine.

    private static DefaultSettings Configured(int port, string security) => new DefaultSettings()
        .Set(SettingCatalog.Keys.SmtpHost, "127.0.0.1")
        .Set(SettingCatalog.Keys.SmtpPort, port.ToString())
        .Set(SettingCatalog.Keys.SmtpSecurity, security)
        .Set(SettingCatalog.Keys.SmtpFromAddress, "noreply@example.com")
        .Set(SettingCatalog.Keys.SmtpTimeoutSeconds, "3");

    private static async Task<string> SendAndDescribeAsync(DefaultSettings settings, string security)
    {
        var port = (int)await settings.GetNumberAsync(SettingCatalog.Keys.SmtpPort);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            new SmtpTransport(settings).SendAsync(new OutgoingMail("a@b.com", "s", "b"), CancellationToken.None));
        return EmailErrors.Describe(ex, "127.0.0.1", port, security);
    }

    [Fact]
    public async Task A_port_nobody_listens_on_reads_as_unreachable()
    {
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();

        var text = await SendAndDescribeAsync(Configured(port, "none"), "none");

        Assert.Contains($"ติดต่อเซิร์ฟเวอร์อีเมล 127.0.0.1:{port} ไม่ได้", text);
    }

    [Fact]
    public async Task A_plain_server_asked_for_STARTTLS_reads_as_no_STARTTLS()
    {
        await using var server = new FakeSmtpServer();

        var text = await SendAndDescribeAsync(Configured(server.Port, "starttls"), "starttls");

        Assert.Contains("ไม่รองรับ STARTTLS", text);
        Assert.Contains("\"ไม่เข้ารหัส\"", text);
    }

    [Fact]
    public async Task A_server_that_blocks_this_machine_at_the_greeting_does_not_blame_the_addresses()
    {
        await using var server = new FakeSmtpServer(greeting: "554 5.7.1 Service unavailable; Client host [1.2.3.4] blocked using zen.spamhaus.org");

        var text = await SendAndDescribeAsync(Configured(server.Port, "none"), "none");

        Assert.Contains("ปฏิเสธคำขอ (รหัส 554)", text);
        Assert.DoesNotContain("ผู้ส่ง", text);
        Assert.DoesNotContain("ผู้รับ", text);
        Assert.Contains("blocked using zen.spamhaus.org", text);   // the server's own words stay in the technical tail
    }

    [Fact]
    public async Task A_certificate_nobody_trusts_reads_as_not_trusted()
    {
        using var certificate = FakeSmtpServer.SelfSignedCertificate("localhost", coversLoopback: true);
        await using var server = new FakeSmtpServer(certificate);

        var text = await SendAndDescribeAsync(Configured(server.Port, "starttls"), "starttls");

        Assert.Contains("ไม่น่าเชื่อถือสำหรับเครื่องนี้", text);
        Assert.Contains("ติดตั้งใบรับรองของผู้ออกใบรับรอง (CA)", text);
    }

    [Fact]
    public async Task A_server_that_refuses_the_login_reads_as_a_refused_login_and_never_repeats_the_password()
    {
        await using var server = new FakeSmtpServer(extensions: "AUTH PLAIN LOGIN");
        var settings = Configured(server.Port, "none")
            .Set(SettingCatalog.Keys.SmtpUsername, "svc-helpdesk")
            .Set(SettingCatalog.Keys.SmtpPassword, "Not-In-The-Message-1");

        var text = await SendAndDescribeAsync(settings, "none");

        Assert.Contains("ปฏิเสธการเข้าสู่ระบบ", text);
        Assert.DoesNotContain("Not-In-The-Message-1", text);
    }
}

/// <summary>The connection check, against servers on this machine that behave in the ways real ones fail.</summary>
public class SmtpDiagnosticsTests
{
    private const string DnsStep = "DNS";
    private const string PortStep = "เชื่อมต่อพอร์ต";
    private const string ServerStep = "คำทักทายและความสามารถของเซิร์ฟเวอร์";
    private const string EncryptionStep = "การเข้ารหัส";

    private static DefaultSettings Configured(int port, string security, string host = "127.0.0.1") => new DefaultSettings()
        .Set(SettingCatalog.Keys.SmtpHost, host)
        .Set(SettingCatalog.Keys.SmtpPort, port.ToString())
        .Set(SettingCatalog.Keys.SmtpSecurity, security)
        .Set(SettingCatalog.Keys.SmtpTimeoutSeconds, "3");

    private static Task<EmailDiagnosis> DiagnoseAsync(DefaultSettings settings) => new SmtpDiagnostics(settings).DiagnoseAsync();

    private static string[] Names(EmailDiagnosis result) => [.. result.Steps.Select(s => s.Name)];

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void AssertNeverSignedInOrSent(FakeSmtpServer server) =>
        Assert.DoesNotContain(server.Received(), l =>
            l.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase) || l.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase) || l.StartsWith("DATA", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task A_server_that_answers_without_encryption_passes_when_encryption_is_off_and_nothing_is_sent()
    {
        await using var server = new FakeSmtpServer();

        var result = await DiagnoseAsync(Configured(server.Port, "none"));

        Assert.True(result.Ok);
        Assert.Equal("127.0.0.1", result.Host);
        Assert.Equal(server.Port, result.Port);
        Assert.Equal("none", result.Security);
        Assert.Equal([DnsStep, PortStep, ServerStep], Names(result));
        Assert.All(result.Steps, s => Assert.True(s.Ok && s.Detail.Length > 0, s.Name));
        Assert.Contains("ไม่ได้ล็อกอิน", Assert.Single(result.Advice));
        AssertNeverSignedInOrSent(server);
    }

    [Fact]
    public async Task A_plain_server_is_explained_when_STARTTLS_is_required()
    {
        await using var server = new FakeSmtpServer();

        var result = await DiagnoseAsync(Configured(server.Port, "starttls"));

        Assert.False(result.Ok);
        Assert.Equal([DnsStep, PortStep, ServerStep], Names(result));          // the encryption step is skipped: it cannot work
        Assert.Equal([true, true, false], result.Steps.Select(s => s.Ok));
        Assert.Contains("ไม่เสนอ STARTTLS", result.Steps[2].Detail);
        Assert.Contains(result.Advice, a => a.Contains("\"ไม่เข้ารหัส\"") && a.Contains("ผู้ดูแลระบบอีเมล"));
        AssertNeverSignedInOrSent(server);
    }

    [Fact]
    public async Task A_server_that_refuses_this_machine_at_the_greeting_says_so_rather_than_blaming_the_addresses()
    {
        await using var server = new FakeSmtpServer(greeting: "554 5.7.1 Service unavailable; Client host [1.2.3.4] blocked using zen.spamhaus.org");

        var result = await DiagnoseAsync(Configured(server.Port, "none"));

        Assert.False(result.Ok);
        Assert.Equal([DnsStep, PortStep, ServerStep], Names(result));
        Assert.Equal([true, true, false], result.Steps.Select(s => s.Ok));
        Assert.Contains("ปฏิเสธคำขอ (รหัส 554)", result.Steps[2].Detail);
        var advice = Assert.Single(result.Advice);
        Assert.Contains("ปฏิเสธคำขอ (รหัส 554)", advice);
        Assert.Contains("ผู้ดูแลระบบอีเมล", advice);
        Assert.DoesNotContain("ผู้ส่ง", result.Steps[2].Detail + advice);
        Assert.DoesNotContain("ผู้รับ", result.Steps[2].Detail + advice);
        AssertNeverSignedInOrSent(server);
    }

    [Fact]
    public async Task A_closed_port_fails_at_the_connect_step_and_the_rest_are_skipped()
    {
        var port = ClosedPort();

        var result = await DiagnoseAsync(Configured(port, "starttls"));

        Assert.False(result.Ok);
        Assert.Equal([DnsStep, PortStep], Names(result));
        Assert.True(result.Steps[0].Ok);
        Assert.False(result.Steps[1].Ok);
        Assert.Contains($"พอร์ต {port}", result.Steps[1].Detail);
        Assert.Contains(result.Advice, a => a.Contains($"พอร์ต {port}") && a.Contains("127.0.0.1"));
    }

    [Fact]
    public async Task A_name_that_does_not_resolve_fails_at_the_first_step()
    {
        var result = await DiagnoseAsync(Configured(25, "none", host: "no-such-host.invalid"));

        Assert.False(result.Ok);
        Assert.Equal([DnsStep], Names(result));
        Assert.False(result.Steps[0].Ok);
        Assert.Contains(result.Advice, a => a.Contains("no-such-host.invalid"));
    }

    [Fact]
    public async Task Port_465_with_STARTTLS_chosen_is_flagged_as_a_wrong_pair_whatever_the_server_answers()
    {
        var result = await DiagnoseAsync(Configured(465, "starttls"));

        Assert.False(result.Ok);
        Assert.Contains("พอร์ต 465 ใช้ SSL/TLS", result.Advice[0]);
        Assert.Contains("\"STARTTLS\"", result.Advice[0]);
    }

    [Fact]
    public async Task Nothing_is_tried_until_a_server_is_set()
    {
        var result = await DiagnoseAsync(new DefaultSettings());

        Assert.False(result.Ok);
        Assert.Empty(result.Steps);
        Assert.Contains("เซิร์ฟเวอร์ SMTP", Assert.Single(result.Advice));
    }

    [Fact]
    public async Task A_certificate_nobody_trusts_fails_at_the_encryption_step_and_says_to_install_the_issuer()
    {
        using var certificate = FakeSmtpServer.SelfSignedCertificate("localhost", coversLoopback: true);
        await using var server = new FakeSmtpServer(certificate);

        var result = await DiagnoseAsync(Configured(server.Port, "starttls"));

        Assert.False(result.Ok);
        Assert.Equal([DnsStep, PortStep, ServerStep, EncryptionStep], Names(result));
        Assert.Equal([true, true, true, false], result.Steps.Select(s => s.Ok));
        Assert.Contains("ไม่ยอมรับ", result.Steps[3].Detail);
        Assert.Contains(result.Advice, a => a.Contains("ติดตั้งใบรับรองของผู้ออก (CA)"));
        Assert.DoesNotContain(result.Advice, a => a.Contains("ไม่ได้ออกให้ชื่อ"));   // the name matches; only the issuer is the problem
        AssertNeverSignedInOrSent(server);
    }

    [Fact]
    public async Task A_certificate_for_another_name_is_reported_as_well_as_the_missing_trust()
    {
        using var certificate = FakeSmtpServer.SelfSignedCertificate("mail.example.test", coversLoopback: false);
        await using var server = new FakeSmtpServer(certificate);

        var result = await DiagnoseAsync(Configured(server.Port, "starttls"));

        Assert.False(result.Ok);
        Assert.Equal(EncryptionStep, result.Steps[^1].Name);
        Assert.Contains(result.Advice, a => a.Contains("ไม่ได้ออกให้ชื่อ 127.0.0.1"));
        Assert.Contains(result.Advice, a => a.Contains("ติดตั้งใบรับรองของผู้ออก (CA)"));
    }

    [Fact]
    public async Task With_SSL_the_certificate_is_judged_first_and_the_greeting_step_is_skipped_when_it_fails()
    {
        using var certificate = FakeSmtpServer.SelfSignedCertificate("localhost", coversLoopback: true);
        await using var server = new FakeSmtpServer(certificate, implicitTls: true);

        var result = await DiagnoseAsync(Configured(server.Port, "ssl"));

        Assert.False(result.Ok);
        Assert.Equal([DnsStep, PortStep, EncryptionStep], Names(result));
        Assert.Equal([true, true, false], result.Steps.Select(s => s.Ok));
        Assert.Contains(result.Advice, a => a.Contains("ติดตั้งใบรับรองของผู้ออก (CA)"));
    }

    [Fact]
    public async Task SSL_asked_of_a_server_that_speaks_plain_SMTP_says_to_match_the_encryption_to_the_port()
    {
        await using var server = new FakeSmtpServer();

        var result = await DiagnoseAsync(Configured(server.Port, "ssl"));

        Assert.False(result.Ok);
        Assert.Equal(EncryptionStep, result.Steps[^1].Name);
        Assert.False(result.Steps[^1].Ok);
        Assert.Contains(result.Advice, a => a.Contains("ตรวจว่าตั้งการเข้ารหัสตรงกับพอร์ต"));
    }

    [Fact]
    public async Task A_login_that_the_server_never_offers_is_reported_without_trying_it()
    {
        await using var server = new FakeSmtpServer();
        var settings = Configured(server.Port, "none").Set(SettingCatalog.Keys.SmtpUsername, "svc-helpdesk");

        var result = await DiagnoseAsync(settings);

        Assert.False(result.Ok);
        Assert.Equal(ServerStep, result.Steps[^1].Name);
        Assert.Contains(result.Advice, a => a.Contains("ลบชื่อผู้ใช้"));
        AssertNeverSignedInOrSent(server);
    }

    [Fact]
    public async Task The_logins_a_server_offers_are_listed_but_never_tried_and_the_password_is_never_in_the_result()
    {
        await using var server = new FakeSmtpServer(extensions: "AUTH PLAIN LOGIN");
        var settings = Configured(server.Port, "none")
            .Set(SettingCatalog.Keys.SmtpUsername, "svc-helpdesk")
            .Set(SettingCatalog.Keys.SmtpPassword, "Diag-Secret-1");

        var result = await DiagnoseAsync(settings);

        Assert.True(result.Ok);
        Assert.Contains("PLAIN", result.Steps[2].Detail);
        Assert.Contains("LOGIN", result.Steps[2].Detail);
        Assert.DoesNotContain("Diag-Secret-1", JsonSerializer.Serialize(result));
        AssertNeverSignedInOrSent(server);
    }
}

public class EmailSettingsApiTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public EmailSettingsApiTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_password_is_accepted_but_never_returned_by_the_api()
    {
        var admin = await _factory.ClientAsync("admin");

        var put = await admin.PutAsJsonAsync("/api/settings/smtp.password", new { value = "Api-Secret-1" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Api-Secret-1", body);

        var list = await admin.GetStringAsync("/api/settings");
        Assert.DoesNotContain("Api-Secret-1", list);
        var item = JsonDocument.Parse(list).RootElement.EnumerateArray().Single(e => e.GetProperty("key").GetString() == "smtp.password");
        Assert.True(item.GetProperty("sensitive").GetBoolean());
        Assert.Equal("", item.GetProperty("value").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/settings/smtp.password", new { value = "" })).StatusCode);
    }

    [Fact]
    public async Task The_test_message_needs_the_manage_right_and_a_valid_recipient()
    {
        var staff = await _factory.ClientAsync("staff-a");
        var admin = await _factory.ClientAsync("admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/settings/email/test", new { to = "a@b.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/settings/email/test", new { to = "not-an-address" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/settings/email/test", new { to = "" })).StatusCode);
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Saves some settings, runs the check, and puts the defaults back so no other test sees them.</summary>
    private static async Task WithSettingsAsync(HttpClient admin, Dictionary<string, string> values, Func<Task> check)
    {
        try
        {
            foreach (var (key, value) in values)
                Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/settings/{key}", new { value })).StatusCode);
            await check();
        }
        finally
        {
            foreach (var key in values.Keys) await admin.DeleteAsync($"/api/settings/{key}");
        }
    }

    [Fact]
    public async Task The_connection_check_needs_the_manage_right_and_a_sign_in()
    {
        foreach (var user in new[] { "staff-a", "it-a", "manager-a" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await _factory.ClientAsync(user)).PostAsync("/api/settings/email/diagnose", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsync("/api/settings/email/diagnose", null)).StatusCode);
    }

    [Fact]
    public async Task The_connection_check_reports_the_saved_settings_and_never_returns_the_password()
    {
        var admin = await _factory.ClientAsync("admin");
        var port = ClosedPort();

        await WithSettingsAsync(admin, new()
        {
            ["smtp.host"] = "127.0.0.1", ["smtp.port"] = port.ToString(), ["smtp.security"] = "none", ["smtp.timeoutSeconds"] = "3",
            ["smtp.username"] = "svc-helpdesk", ["smtp.password"] = "Diag-Api-Secret-2",
        }, async () =>
        {
            var response = await admin.PostAsync("/api/settings/email/diagnose", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Diag-Api-Secret-2", raw);
            var body = JsonDocument.Parse(raw).RootElement;
            Assert.False(body.GetProperty("ok").GetBoolean());
            Assert.Equal("127.0.0.1", body.GetProperty("host").GetString());
            Assert.Equal(port, body.GetProperty("port").GetInt32());
            Assert.Equal("none", body.GetProperty("security").GetString());
            var steps = body.GetProperty("steps").EnumerateArray().ToList();
            Assert.Equal(["DNS", "เชื่อมต่อพอร์ต"], steps.Select(s => s.GetProperty("name").GetString()));
            Assert.Equal([true, false], steps.Select(s => s.GetProperty("ok").GetBoolean()));
            Assert.All(steps, s => Assert.False(string.IsNullOrEmpty(s.GetProperty("detail").GetString())));
            Assert.Contains($"พอร์ต {port}", Assert.Single(body.GetProperty("advice").EnumerateArray()).GetString());
        });
    }

    [Fact]
    public async Task A_failed_test_message_says_what_to_fix_in_plain_words_and_keeps_the_reason_in_brackets()
    {
        var admin = await _factory.ClientAsync("admin");
        var port = ClosedPort();

        await WithSettingsAsync(admin, new()
        {
            ["smtp.host"] = "127.0.0.1", ["smtp.port"] = port.ToString(), ["smtp.security"] = "none", ["smtp.timeoutSeconds"] = "3",
            ["smtp.fromAddress"] = "noreply@example.com",
        }, async () =>
        {
            var response = await admin.PostAsJsonAsync("/api/settings/email/test", new { to = "someone@example.org" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var message = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
            Assert.StartsWith("ส่งอีเมลทดสอบไม่สำเร็จ: ", message);
            Assert.Contains($"ติดต่อเซิร์ฟเวอร์อีเมล 127.0.0.1:{port} ไม่ได้", message);
            Assert.Contains("(SocketException", message);
        });
    }
}

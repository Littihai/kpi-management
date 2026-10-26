using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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

/// <summary>The real MailKit path against a tiny SMTP server listening on this machine.</summary>
public class SmtpTransportTests
{
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _loop;
        public List<string> Lines { get; } = [];
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeSmtpServer()
        {
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        private async Task AcceptAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 fake ESMTP ready");
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
                    if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO")) await writer.WriteLineAsync("250 fake");
                    else if (cmd == "DATA") { inData = true; await writer.WriteLineAsync("354 go ahead"); }
                    else if (cmd == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                    else await writer.WriteLineAsync("250 ok");
                }
            }
            catch (Exception) { /* the test is over */ }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await Task.WhenAny(_loop, Task.Delay(1000));
        }
    }

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
}

using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using KPI.Application.Notifications;
using KPI.Application.Settings;
using KPI.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace KPI.Infrastructure.Services;

/// <summary>
/// Finds where sending e-mail stops working: name lookup, opening the port, the server's greeting and, when the settings
/// ask for it, encryption. It stops at the first step that fails, since the later ones cannot work without it.
/// It never signs in and never sends a message, so it does not even read the password.
/// </summary>
public sealed class SmtpDiagnostics : IEmailDiagnostics
{
    private const string DnsStep = "DNS";
    private const string PortStep = "เชื่อมต่อพอร์ต";
    private const string ServerStep = "คำทักทายและความสามารถของเซิร์ฟเวอร์";
    private const string EncryptionStep = "การเข้ารหัส";

    /// <summary>Each step may use the SMTP timeout setting (up to a minute); the whole check never runs longer than this.</summary>
    private static readonly TimeSpan OverallLimit = TimeSpan.FromSeconds(40);

    private readonly ISystemSettings _settings;

    public SmtpDiagnostics(ISystemSettings settings) => _settings = settings;

    public async Task<EmailDiagnosis> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        var host = (await _settings.GetAsync(SettingCatalog.Keys.SmtpHost)).Trim();
        var port = (int)await _settings.GetNumberAsync(SettingCatalog.Keys.SmtpPort);
        // Anything but ssl and none means STARTTLS, as it does when a message is sent.
        var saved = await _settings.GetAsync(SettingCatalog.Keys.SmtpSecurity);
        var security = saved is "ssl" or "none" ? saved : "starttls";
        var timeout = TimeSpan.FromSeconds(await _settings.GetNumberAsync(SettingCatalog.Keys.SmtpTimeoutSeconds));
        var expectsLogin = (await _settings.GetAsync(SettingCatalog.Keys.SmtpUsername)).Length > 0;

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(OverallLimit);
        return await new Run(host, port, security, timeout, expectsLogin, cancellationToken, overall.Token).ExecuteAsync();
    }

    /// <summary>A step that failed in a way worth saying exactly, rather than as whatever the exception happens to read.</summary>
    private sealed class StepFailed(string detail, string? advice) : Exception(detail)
    {
        public string? Advice { get; } = advice;
    }

    /// <summary>What the TLS handshake showed of the server's certificate, kept even when the handshake is then refused.</summary>
    private sealed class TlsObservation
    {
        public bool Seen { get; private set; }
        public SslPolicyErrors Errors { get; private set; }
        public X509ChainStatusFlags ChainStatus { get; private set; }
        public string Subject { get; private set; } = string.Empty;
        public string Issuer { get; private set; } = string.Empty;
        public DateTime? NotAfter { get; private set; }

        /// <summary>Takes the same decision as the default check (only a clean certificate passes) and notes what it saw.</summary>
        public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            Seen = true;
            Errors = errors;
            Subject = certificate?.Subject ?? string.Empty;
            Issuer = certificate?.Issuer ?? string.Empty;
            NotAfter = (certificate as X509Certificate2)?.NotAfter.ToUniversalTime();
            if (chain is not null)
                foreach (var status in chain.ChainStatus) ChainStatus |= status.Status;
            return errors == SslPolicyErrors.None;
        }
    }

    private sealed class Run
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _security;
        private readonly TimeSpan _timeout;
        private readonly bool _expectsLogin;
        private readonly CancellationToken _caller;
        private readonly CancellationToken _overall;

        public Run(string host, int port, string security, TimeSpan timeout, bool expectsLogin,
            CancellationToken caller, CancellationToken overall)
        {
            _host = host;
            _port = port;
            _security = security;
            _timeout = timeout;
            _expectsLogin = expectsLogin;
            _caller = caller;
            _overall = overall;
        }

        private List<EmailDiagnosisStep> Steps { get; } = [];
        private List<string> Advice { get; } = [];

        /// <summary>Port 465 speaks SSL/TLS from the first byte, whatever the settings say, so that is how it is probed.</summary>
        private bool ImplicitTls => _security == "ssl" || _port == 465;

        private int Seconds => (int)Math.Ceiling(_timeout.TotalSeconds);

        public async Task<EmailDiagnosis> ExecuteAsync()
        {
            if (_host.Length == 0)
            {
                Advice.Add("ยังไม่ได้ตั้งเซิร์ฟเวอร์ SMTP ให้กรอกชื่อเซิร์ฟเวอร์ในช่อง \"เซิร์ฟเวอร์ SMTP\" แล้วบันทึกก่อนตรวจการเชื่อมต่อ");
                return Result(false);
            }

            var ok = await RunStepsAsync();
            if (_port == 465 && _security != "ssl")
            {
                ok = false;
                Advice.Insert(0, $"พอร์ต 465 ใช้ SSL/TLS แต่ตั้งการเข้ารหัสไว้เป็น \"{EmailErrors.EncryptionName(_security)}\" ให้เปลี่ยนการเข้ารหัสเป็น \"SSL/TLS\"");
            }
            if (ok)
                Advice.Add("เชื่อมต่อกับเซิร์ฟเวอร์ได้ตามปกติ การตรวจนี้ไม่ได้ล็อกอินหรือส่งอีเมลจริง ถ้าอีเมลทดสอบยังส่งไม่ได้ ให้ตรวจชื่อผู้ใช้ รหัสผ่าน และที่อยู่ผู้ส่ง");
            return Result(ok);
        }

        private EmailDiagnosis Result(bool ok) => new(ok, _host, _port, _security, Steps, Advice);

        private async Task<bool> RunStepsAsync()
        {
            if (await CheckNameAsync() is not { } addresses) return false;
            if (!await CheckPortAsync(addresses)) return false;
            if (ImplicitTls) return await CheckImplicitTlsAsync();
            return await CheckServerAsync() && (_security != "starttls" || await CheckStartTlsAsync());
        }

        // ───────────────────────── The steps ─────────────────────────

        private async Task<IPAddress[]?> CheckNameAsync()
        {
            if (IPAddress.TryParse(_host, out var literal))
            {
                Steps.Add(new(DnsStep, true, $"ใช้เลขไอพี {literal} โดยตรง ไม่ต้องค้นหาชื่อ"));
                return [literal];
            }

            IPAddress[] addresses = [];
            var failure = await TryAsync(async ct => addresses = await Dns.GetHostAddressesAsync(_host, ct));
            if (failure is null && addresses.Length > 0)
            {
                Steps.Add(new(DnsStep, true, $"พบ {addresses.Length} ที่อยู่: {string.Join(", ", addresses.Take(4))}"));
                return addresses;
            }

            Fail(DnsStep,
                failure is null ? $"ไม่พบที่อยู่ของ {_host}"
                : IsTimeout(failure) ? $"ค้นหาชื่อ {_host} ไม่เสร็จภายใน {Seconds} วินาที"
                : $"ค้นหาชื่อ {_host} ไม่สำเร็จ ({failure.GetType().Name})",
                failure is not null && IsTimeout(failure)
                    ? $"เครื่องนี้ค้นหาชื่อ {_host} ไม่ได้ภายในเวลาที่กำหนด ให้ตรวจว่าเครื่องนี้ใช้ DNS ได้ หรือใช้เลขไอพีแทนชื่อ"
                    : EmailErrors.HostNotFound(_host));
            return null;
        }

        private async Task<bool> CheckPortAsync(IPAddress[] addresses)
        {
            var clock = Stopwatch.StartNew();
            using var tcp = new TcpClient();
            var failure = await TryAsync(ct => tcp.ConnectAsync(addresses, _port, ct).AsTask());
            if (failure is null)
            {
                Steps.Add(new(PortStep, true, $"เชื่อมต่อ {_host}:{_port} ได้ ใช้เวลา {clock.ElapsedMilliseconds} มิลลิวินาที"));
                return true;
            }

            var refused = failure is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
            Fail(PortStep,
                refused ? $"เซิร์ฟเวอร์ปฏิเสธการเชื่อมต่อที่พอร์ต {_port}"
                : IsTimeout(failure) ? $"ไม่มีการตอบกลับจาก {_host}:{_port} ภายใน {Seconds} วินาที"
                : $"เชื่อมต่อ {_host}:{_port} ไม่ได้ ({Brief(failure)})",
                refused
                    ? $"พอร์ต {_port} ของ {_host} ไม่มีโปรแกรมรับอยู่ หรือไฟร์วอลล์ปฏิเสธ ให้ตรวจเลขพอร์ต (587 = STARTTLS, 465 = SSL/TLS, 25 = ไม่เข้ารหัส) และแจ้งผู้ดูแลเครือข่ายตรวจไฟร์วอลล์"
                    : $"เครื่องนี้ติดต่อ {_host} พอร์ต {_port} ไม่ได้ ให้แจ้งผู้ดูแลเครือข่ายเปิดการเชื่อมต่อ TCP ขาออกที่พอร์ต {_port} ไปยัง {_host} และตรวจว่าชื่อเครื่องกับพอร์ตถูกต้อง");
            return false;
        }

        /// <summary>The server's greeting and what it offers, over a plain connection (no encryption yet).</summary>
        private async Task<bool> CheckServerAsync()
        {
            var offersStartTls = false;
            string[] mechanisms = [];
            var failure = await TryAsync(async ct =>
            {
                using var client = NewClient();
                await client.ConnectAsync(_host, _port, SecureSocketOptions.None, ct);
                offersStartTls = client.Capabilities.HasFlag(SmtpCapabilities.StartTLS);
                mechanisms = [.. client.AuthenticationMechanisms.Order()];
                await DisconnectAsync(client);
            });

            if (failure is not null)
            {
                Fail(ServerStep,
                    IsTimeout(failure) ? $"เชื่อมต่อพอร์ตได้ แต่เซิร์ฟเวอร์ไม่ทักทายกลับมาภายใน {Seconds} วินาที"
                    : EmailErrors.Describe(failure, _host, _port, _security),
                    IsTimeout(failure)
                        ? "พอร์ตนี้อาจต้องใช้ SSL/TLS ตั้งแต่เริ่มเชื่อมต่อ (เช่น พอร์ต 465) ให้ตรวจเลขพอร์ตและเลือกการเข้ารหัสให้ตรงกัน"
                        : EmailErrors.Explain(failure, _host, _port, _security));
                return false;
            }

            var starttls = offersStartTls ? "เสนอ STARTTLS" : "ไม่เสนอ STARTTLS";
            var login = $"การล็อกอิน (AUTH): {Mechanisms(mechanisms)}";
            if (_security == "starttls" && !offersStartTls)
            {
                Fail(ServerStep, $"เซิร์ฟเวอร์ตอบรับแล้ว แต่ไม่เสนอ STARTTLS ({login})", EmailErrors.NoStartTls);
                return false;
            }
            if (_security == "none" && _expectsLogin && mechanisms.Length == 0)
            {
                Fail(ServerStep, $"เซิร์ฟเวอร์ตอบรับแล้ว แต่ไม่เสนอการล็อกอินในการเชื่อมต่อแบบไม่เข้ารหัส ({starttls})", EmailErrors.NoLoginOnThisConnection);
                return false;
            }

            Steps.Add(new(ServerStep, true, $"เซิร์ฟเวอร์ตอบรับแล้ว: {starttls}, {login}"));
            return true;
        }

        /// <summary>Upgrades the connection with STARTTLS and judges the certificate the way a real send would.</summary>
        private async Task<bool> CheckStartTlsAsync()
        {
            var tls = new TlsObservation();
            var detail = string.Empty;
            var failure = await TryAsync(async ct =>
            {
                using var client = NewClient(tls);
                await client.ConnectAsync(_host, _port, SecureSocketOptions.StartTls, ct);
                detail = Encrypted(client, tls);
                var noLogin = _expectsLogin && client.AuthenticationMechanisms.Count == 0;
                await DisconnectAsync(client);
                if (noLogin) throw new StepFailed($"{detail}; แต่หลังเข้ารหัสเซิร์ฟเวอร์ไม่เสนอการล็อกอิน (AUTH)", EmailErrors.NoLoginOnThisConnection);
            });

            return Conclude(EncryptionStep, failure, detail, tls);
        }

        /// <summary>SSL/TLS from the first byte: the handshake comes before the greeting, so both are told from one connection.</summary>
        private async Task<bool> CheckImplicitTlsAsync()
        {
            var tls = new TlsObservation();
            var encrypted = string.Empty;
            var offered = string.Empty;
            var failure = await TryAsync(async ct =>
            {
                using var client = NewClient(tls);
                await client.ConnectAsync(_host, _port, SecureSocketOptions.SslOnConnect, ct);
                encrypted = Encrypted(client, tls);
                offered = $"การล็อกอิน (AUTH): {Mechanisms([.. client.AuthenticationMechanisms.Order()])}";
                var noLogin = _expectsLogin && client.AuthenticationMechanisms.Count == 0;
                await DisconnectAsync(client);
                if (noLogin) throw new StepFailed($"เซิร์ฟเวอร์ตอบรับแล้ว แต่ไม่เสนอการล็อกอิน ({offered})", EmailErrors.NoLoginOnThisConnection);
            });

            if (failure is StepFailed noLogin)
            {
                Fail(ServerStep, noLogin.Message, noLogin.Advice);
                return false;
            }

            // A certificate that was accepted means the handshake worked and it was the greeting that failed.
            if (failure is not null and not SslHandshakeException && tls is { Seen: true, Errors: SslPolicyErrors.None })
            {
                Fail(ServerStep,
                    IsTimeout(failure) ? $"เข้ารหัสสำเร็จ แต่เซิร์ฟเวอร์ไม่ทักทายกลับมาภายใน {Seconds} วินาที" : EmailErrors.Describe(failure, _host, _port, _security),
                    EmailErrors.Explain(failure, _host, _port, _security));
                return false;
            }

            if (failure is null) Steps.Add(new(ServerStep, true, $"เซิร์ฟเวอร์ตอบรับแล้ว: {offered}"));
            return Conclude(EncryptionStep, failure, encrypted, tls);
        }

        // ───────────────────────── Helpers ─────────────────────────

        private bool Conclude(string step, Exception? failure, string detail, TlsObservation tls)
        {
            switch (failure)
            {
                case null:
                    Steps.Add(new(step, true, detail));
                    return true;
                case StepFailed planned:
                    Fail(step, planned.Message, planned.Advice);
                    return false;
            }

            if (IsTimeout(failure))
                Fail(step, $"เซิร์ฟเวอร์ไม่ตอบรับการเข้ารหัสภายใน {Seconds} วินาที", EmailErrors.NoEncryptionOnThisPort(_port, _security));
            else if (tls is { Seen: true } && tls.Errors != SslPolicyErrors.None)
            {
                var problems = CertificateProblems(tls).ToList();
                Fail(step,
                    $"เซิร์ฟเวอร์ส่งใบรับรอง {tls.Subject} (ออกโดย {tls.Issuer}) แต่เครื่องนี้ไม่ยอมรับ: {string.Join("; ", problems.Select(p => p.Problem))}",
                    [.. problems.Select(p => p.Advice)]);
            }
            else
                Fail(step, EmailErrors.Describe(failure, _host, _port, _security), EmailErrors.Explain(failure, _host, _port, _security));
            return false;
        }

        private IEnumerable<(string Problem, string Advice)> CertificateProblems(TlsObservation tls)
        {
            if (tls.Errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                yield return ("เซิร์ฟเวอร์ไม่ส่งใบรับรองมา", "เซิร์ฟเวอร์ไม่ส่งใบรับรองมา ตรวจว่าเปิด SSL/TLS ที่พอร์ตนี้จริง");

            if (tls.Errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                yield return ($"ชื่อในใบรับรองไม่ตรงกับ {_host}",
                    $"ใบรับรองของเซิร์ฟเวอร์ไม่ได้ออกให้ชื่อ {_host} ให้ตั้งชื่อเซิร์ฟเวอร์ SMTP ให้ตรงกับชื่อในใบรับรอง (ไม่ใช้เลขไอพีหรือชื่อย่อ) หรือขอใบรับรองที่ครอบคลุมชื่อนี้");

            if (!tls.Errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors)) yield break;

            var status = tls.ChainStatus;
            var untrusted = status.HasFlag(X509ChainStatusFlags.UntrustedRoot) || status.HasFlag(X509ChainStatusFlags.PartialChain);
            if (status.HasFlag(X509ChainStatusFlags.NotTimeValid))
                yield return ("หมดอายุหรือยังไม่ถึงเวลาใช้งาน",
                    "ใบรับรองของเซิร์ฟเวอร์หมดอายุหรือยังไม่ถึงวันเริ่มใช้ ให้แจ้งผู้ดูแลระบบอีเมลต่ออายุใบรับรอง และตรวจวันเวลาของเครื่องนี้");
            if (status.HasFlag(X509ChainStatusFlags.Revoked))
                yield return ("ถูกเพิกถอน", "ใบรับรองของเซิร์ฟเวอร์ถูกเพิกถอนแล้ว ให้แจ้งผู้ดูแลระบบอีเมลออกใบรับรองใหม่");
            if (untrusted)
                yield return ("ไม่เชื่อถือผู้ออกใบรับรอง",
                    "เครื่องนี้ไม่เชื่อถือผู้ออกใบรับรองของเซิร์ฟเวอร์ ให้ติดตั้งใบรับรองของผู้ออก (CA) ลงใน Trusted Root Certification Authorities ของเครื่องที่รันระบบนี้");
            else if (status.HasFlag(X509ChainStatusFlags.RevocationStatusUnknown) || status.HasFlag(X509ChainStatusFlags.OfflineRevocation))
                yield return ("ตรวจสถานะการเพิกถอนไม่ได้",
                    "เครื่องนี้ตรวจรายการเพิกถอนใบรับรอง (CRL หรือ OCSP) ไม่ได้ ให้แจ้งผู้ดูแลเครือข่ายเปิดการเข้าถึงที่อยู่ที่ใบรับรองระบุไว้");
        }

        /// <summary>What a good handshake looked like: the protocol, and who the certificate is and until when.</summary>
        private static string Encrypted(SmtpClient client, TlsObservation tls)
        {
            var protocol = client.SslProtocol switch
            {
                System.Security.Authentication.SslProtocols.Tls13 => "TLS 1.3",
                System.Security.Authentication.SslProtocols.Tls12 => "TLS 1.2",
                System.Security.Authentication.SslProtocols.None => "SSL/TLS",
                var other => other.ToString(),
            };
            var expires = tls.NotAfter is { } notAfter ? $" ใช้ได้ถึง {notAfter:yyyy-MM-dd}" : string.Empty;
            return $"เข้ารหัสด้วย {protocol} ได้ ใบรับรองของ {(tls.Subject.Length > 0 ? tls.Subject : "เซิร์ฟเวอร์")} เชื่อถือได้{expires}";
        }

        private static string Mechanisms(string[] mechanisms) => mechanisms.Length == 0 ? "ไม่เสนอ" : string.Join(" ", mechanisms);

        private static string Brief(Exception exception) =>
            exception is SocketException socket ? socket.SocketErrorCode.ToString() : exception.GetType().Name;

        private SmtpClient NewClient(TlsObservation? tls = null)
        {
            var client = new SmtpClient { Timeout = (int)_timeout.TotalMilliseconds };
            if (tls is not null) client.ServerCertificateValidationCallback = tls.Validate;
            return client;
        }

        private static async Task DisconnectAsync(SmtpClient client)
        {
            try
            {
                if (client.IsConnected) await client.DisconnectAsync(true, CancellationToken.None);
            }
            catch (Exception)
            {
                // The check is over; a server that is rude about being left says nothing more about its settings.
            }
        }

        /// <summary>Runs one check within the step time limit and the overall one. A failure is returned for the caller to describe; only the caller giving up is thrown.</summary>
        private async Task<Exception?> TryAsync(Func<CancellationToken, Task> check)
        {
            using var step = CancellationTokenSource.CreateLinkedTokenSource(_overall);
            step.CancelAfter(_timeout);
            try
            {
                await check(step.Token);
                return null;
            }
            catch (OperationCanceledException) when (_caller.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static bool IsTimeout(Exception exception) => exception is
            OperationCanceledException or TimeoutException
            or SocketException { SocketErrorCode: SocketError.TimedOut }
            or IOException { InnerException: SocketException { SocketErrorCode: SocketError.TimedOut } };

        private void Fail(string step, string detail, params string?[] advice)
        {
            Steps.Add(new(step, false, detail));
            Advice.AddRange(advice.OfType<string>().Where(a => a.Length > 0));
        }
    }
}

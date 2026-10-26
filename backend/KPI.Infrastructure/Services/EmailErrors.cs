using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace KPI.Infrastructure.Services;

/// <summary>
/// Turns what MailKit and the network throw into a sentence an administrator can act on, followed by the technical
/// detail in parentheses for whoever has to dig further. Pure: the same exception always reads the same.
/// </summary>
public static class EmailErrors
{
    private const int MaxTechnicalLength = 160;

    /// <summary>What went wrong and what to do about it, then the exception in brief. An exception nobody recognises reads as its own message.</summary>
    public static string Describe(Exception exception, string host, int port, string security)
    {
        exception = Unwrap(exception);
        return Explain(exception, host, port, security) is { } explanation
            ? $"{explanation} ({Technical(exception)})"
            : Truncate(exception.Message, 300);
    }

    /// <summary>Only the advice for a known failure, or null when the exception is not one of the usual mail problems.</summary>
    public static string? Explain(Exception exception, string host, int port, string security) => Unwrap(exception) switch
    {
        MailKit.Security.AuthenticationException => LoginRefused,
        ServiceNotAuthenticatedException => LoginRequired,
        SmtpCommandException command => ExplainCommand(command),
        SslHandshakeException { ServerCertificate: not null } or System.Security.Authentication.AuthenticationException => CertificateNotTrusted,
        SslHandshakeException => NoEncryptionOnThisPort(port, security),
        NotSupportedException unsupported when Mentions(unsupported, "STARTTLS") => NoStartTls,
        NotSupportedException unsupported when Mentions(unsupported, "authentication") => NoLoginOnThisConnection,
        SocketException socket => ExplainSocket(socket, host, port, security),
        IOException { InnerException: SocketException socket } => ExplainSocket(socket, host, port, security),
        TimeoutException => CannotReach(host, port),
        SmtpProtocolException or IOException => Dropped(port, security),
        _ => null,
    };

    private const string LoginRefused =
        "เซิร์ฟเวอร์ปฏิเสธการเข้าสู่ระบบ ตรวจชื่อผู้ใช้และรหัสผ่านของบัญชีอีเมล (บางบริการต้องใช้รหัสผ่านสำหรับแอปแทนรหัสผ่านปกติ)";

    private const string LoginRequired =
        "เซิร์ฟเวอร์ต้องการให้เข้าสู่ระบบก่อนส่งอีเมล ให้กรอกชื่อผู้ใช้และรหัสผ่านในการตั้งค่า SMTP";

    private const string CertificateNotTrusted =
        "ใบรับรองความปลอดภัย (SSL/TLS) ของเซิร์ฟเวอร์ไม่น่าเชื่อถือสำหรับเครื่องนี้ ให้ติดตั้งใบรับรองของผู้ออกใบรับรอง (CA) ลงในเครื่องที่รันระบบ หรือตรวจว่าชื่อเซิร์ฟเวอร์ตรงกับชื่อในใบรับรอง";

    public const string NoStartTls =
        "เซิร์ฟเวอร์นี้ไม่รองรับ STARTTLS ให้เลือกการเข้ารหัสเป็น \"ไม่เข้ารหัส\" (ใช้ได้เฉพาะเครือข่ายภายในที่เชื่อถือได้) หรือแจ้งผู้ดูแลระบบอีเมลให้เปิด STARTTLS หรือ SSL/TLS";

    public const string NoLoginOnThisConnection =
        "เซิร์ฟเวอร์ไม่เปิดให้เข้าสู่ระบบ (AUTH) ในการเชื่อมต่อแบบนี้ ถ้าเซิร์ฟเวอร์ไม่ต้องล็อกอินให้ลบชื่อผู้ใช้ออก หรือเลือกการเข้ารหัสเป็น STARTTLS หรือ SSL/TLS เพราะบางเซิร์ฟเวอร์เปิดให้ล็อกอินหลังเข้ารหัสแล้วเท่านั้น";

    private const string EncryptionRequiredForLogin =
        "เซิร์ฟเวอร์ไม่ให้เข้าสู่ระบบผ่านการเชื่อมต่อที่ไม่เข้ารหัส ให้เลือกการเข้ารหัสเป็น STARTTLS หรือ SSL/TLS";

    private const string SenderRefused =
        "เซิร์ฟเวอร์ไม่ยอมรับที่อยู่ผู้ส่ง ตรวจว่า \"ที่อยู่ผู้ส่ง\" เป็นกล่องจดหมายที่บัญชีนี้ใช้ส่งได้";

    private const string RecipientRefused =
        "เซิร์ฟเวอร์ไม่ยอมรับอีเมลผู้รับ ตรวจว่าที่อยู่ถูกต้อง และเซิร์ฟเวอร์อนุญาตให้ส่งไปยังโดเมนนั้น";

    /// <summary>A refusal that is not about an address (the greeting, a blocked machine, the message itself), so it says nothing about the sender or the recipient.</summary>
    private static string RequestRefused(SmtpStatusCode status) =>
        $"เซิร์ฟเวอร์ปฏิเสธคำขอ (รหัส {(int)status}) อาจไม่อนุญาตให้เครื่องนี้ใช้งาน เช่น ที่อยู่ไอพีของเครื่องถูกบล็อกหรือไม่ได้รับอนุญาตให้ส่งอีเมลผ่านเซิร์ฟเวอร์นี้ ให้แจ้งผู้ดูแลระบบอีเมล";

    private static string CannotReach(string host, int port) =>
        $"ติดต่อเซิร์ฟเวอร์อีเมล {host}:{port} ไม่ได้ เครือข่ายหรือไฟร์วอลล์อาจปิดพอร์ตนี้ ให้ตรวจชื่อเครื่องกับพอร์ต หรือแจ้งผู้ดูแลเครือข่ายให้เปิดพอร์ต {port} ขาออก";

    public static string HostNotFound(string host) =>
        $"ไม่พบเครื่องชื่อ {host} (หาที่อยู่ของเซิร์ฟเวอร์ไม่ได้) ให้ตรวจการสะกดของเซิร์ฟเวอร์ SMTP หรือใช้เลขไอพีแทน";

    /// <summary>The handshake failed before any certificate was seen: most often the encryption chosen is not the one the port speaks.</summary>
    public static string NoEncryptionOnThisPort(int port, string security) =>
        $"ตั้งการเข้ารหัสกับเซิร์ฟเวอร์ไม่สำเร็จ ตรวจว่าตั้งการเข้ารหัสตรงกับพอร์ต (587 = STARTTLS, 465 = SSL/TLS, 25 = ไม่เข้ารหัส){PortHint(port, security)}";

    /// <summary>The server closed the connection or answered in a way SMTP does not allow, which is what a wrong encryption choice looks like.</summary>
    private static string Dropped(int port, string security) =>
        $"เซิร์ฟเวอร์ตัดการเชื่อมต่อหรือตอบกลับผิดรูปแบบ ตรวจว่าตั้งการเข้ารหัสตรงกับพอร์ต (587 = STARTTLS, 465 = SSL/TLS, 25 = ไม่เข้ารหัส){PortHint(port, security)}";

    /// <summary>The settings page's name for an encryption choice.</summary>
    public static string EncryptionName(string security) => security switch
    {
        "ssl" => "SSL/TLS",
        "none" => "ไม่เข้ารหัส",
        _ => "STARTTLS",
    };

    /// <summary>Port 465 speaks SSL/TLS from the first byte, and SSL/TLS is rarely offered on any other port.</summary>
    private static string PortHint(int port, string security) =>
        (port == 465) != (security == "ssl")
            ? $" ตอนนี้ตั้งพอร์ต {port} กับ \"{EncryptionName(security)}\" ซึ่งมักไม่ใช่คู่ที่เซิร์ฟเวอร์ใช้"
            : string.Empty;

    private static string? ExplainCommand(SmtpCommandException command) => command switch
    {
        { ErrorCode: SmtpErrorCode.SenderNotAccepted } => SenderRefused,
        { ErrorCode: SmtpErrorCode.RecipientNotAccepted } => RecipientRefused,
        { StatusCode: SmtpStatusCode.AuthenticationInvalidCredentials } => LoginRefused,
        { StatusCode: SmtpStatusCode.AuthenticationRequired } => LoginRequired,
        { StatusCode: SmtpStatusCode.EncryptionRequiredForAuthenticationMechanism } => EncryptionRequiredForLogin,
        { StatusCode: SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed or SmtpStatusCode.TransactionFailed } => RequestRefused(command.StatusCode),
        _ => null,
    };

    private static string ExplainSocket(SocketException socket, string host, int port, string security) => socket.SocketErrorCode switch
    {
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => HostNotFound(host),
        SocketError.ConnectionReset or SocketError.ConnectionAborted => Dropped(port, security),
        _ => CannotReach(host, port),
    };

    private static bool Mentions(Exception exception, string text) =>
        exception.Message.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException { InnerExceptions.Count: 1 } aggregate ? Unwrap(aggregate.InnerExceptions[0]) : exception;

    /// <summary>The exception's type and the first line of its message, short enough to sit in brackets after the advice.</summary>
    private static string Technical(Exception exception)
    {
        var name = exception is SmtpCommandException command
            ? $"{exception.GetType().Name} {(int)command.StatusCode}"
            : exception.GetType().Name;
        var firstLine = exception.Message.Split('\n', 2)[0].Trim();
        return firstLine.Length == 0 ? name : $"{name}: {Truncate(firstLine, MaxTechnicalLength)}";
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}

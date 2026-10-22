using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.Extensions.FileProviders;

namespace KPI.API.Startup.Setup;

/// <summary>What the person types into the setup page: how to reach the database.</summary>
public sealed record SetupConnection(string? Server, string? Database, string? Auth, string? User, string? Password, bool Trust = true)
{
    public bool Windows => string.Equals(Auth, "windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>Null when fine, otherwise what is wrong (in Thai: it is shown to the person).</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Server) || Server.Length > 200 || Server.Any(char.IsControl)) return "กรอกชื่อหรือที่อยู่ SQL Server";
        if (string.IsNullOrWhiteSpace(Database) || Database.Trim().Length > 128 || Database.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
            return "ชื่อฐานข้อมูลใช้ได้เฉพาะตัวอักษร ตัวเลข _ - และ .";
        if (!Windows && !string.Equals(Auth, "sql", StringComparison.OrdinalIgnoreCase)) return "เลือกวิธีล็อกอิน";
        if (!Windows && (string.IsNullOrWhiteSpace(User) || string.IsNullOrEmpty(Password))) return "กรอกชื่อผู้ใช้และรหัสผ่านของ SQL Server";
        return null;
    }

    public string ConnectionString(int connectTimeoutSeconds = 10) =>
        DatabaseProvider.BuildSqlServer(Server!, Database!, Windows, User, Password, Trust, connectTimeoutSeconds);
}

public sealed record SetupAdmin(string? Email, string? FirstName, string? LastName, string? Password);

/// <summary>The one-time code that proves the person at the setup page is the one who can read the server's files.</summary>
public sealed class SetupKey
{
    public const int MaxFailures = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly string _value;
    private readonly string? _file;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private int _failures;
    private DateTimeOffset _blockedUntil;

    public SetupKey(string value, string? file, TimeProvider? time = null)
    {
        _value = value;
        _file = file;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Where it is kept for the administrator to read, or null when it was given in the configuration instead.</summary>
    public string? File => _file;

    /// <summary>
    /// The code from the configuration (Setup:Key) if there is one; otherwise one that is generated and written next to
    /// the settings file (setup-key.txt), and reused until setup is done, so a restart does not change it.
    /// </summary>
    public static SetupKey Resolve(IConfiguration config, string configFile, TimeProvider? time = null)
    {
        if (config["Setup:Key"] is { Length: >= 8 } given) return new SetupKey(given, null, time);

        var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configFile))!, "setup-key.txt");
        try
        {
            if (System.IO.File.Exists(file) && System.IO.File.ReadAllText(file).Trim() is { Length: >= 16 } existing) return new SetupKey(existing, file, time);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var created = Generate();
            System.IO.File.WriteAllText(file, created + Environment.NewLine);
            return new SetupKey(created, file, time);
        }
        catch (Exception)
        {
            // cannot keep it in a file: use one for this run only, shown in the log
            return new SetupKey(Generate(), null, time);
        }
    }

    public static string Generate()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(24);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    /// <summary>The value, for the log line that tells the administrator where to find it (never for a response).</summary>
    public string Reveal() => _value;

    public TimeSpan? BlockedFor()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return _blockedUntil > now ? _blockedUntil - now : null;
        }
    }

    public bool Matches(string? provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(_value)));
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            if (++_failures >= MaxFailures) { _blockedUntil = _time.GetUtcNow() + Window; _failures = 0; }
        }
    }

    public void RecordSuccess() { lock (_gate) _failures = 0; }

    /// <summary>Removes the file once setup is done: the code is of no use any more.</summary>
    public void Discard()
    {
        try { if (_file is not null) System.IO.File.Delete(_file); } catch { /* a leftover file is harmless: a new run makes a new code */ }
    }
}

/// <summary>The file the setup page writes, and the one the application reads at start-up (it wins over everything else).</summary>
public static class DatabaseConfigFile
{
    public const string DefaultFileName = "database.json";

    public static string PathFor(IConfiguration config, string contentRoot) =>
        config["Setup:ConfigFile"] is { Length: > 0 } path ? path : Path.Combine(contentRoot, DefaultFileName);

    /// <summary>Adds the file as the last, strongest source of settings. Safe to call again after the file was written.</summary>
    public static void Add(IConfigurationBuilder configuration, IConfiguration current, string contentRoot, ILogger? log = null)
    {
        var path = Path.GetFullPath(PathFor(current, contentRoot));
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return;
        if (System.IO.File.Exists(path))
        {
            // A file that was edited by hand and is no longer valid JSON must not stop the service from starting: the setup page replaces it.
            try { using var _ = JsonDocument.Parse(System.IO.File.ReadAllText(path)); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                log?.LogError("The database settings file {File} cannot be read ({Reason}); it is ignored. Fix it, or enter the database on the setup page.", path, ex.Message);
                return;
            }
        }
        configuration.AddJsonFile(new PhysicalFileProvider(directory), Path.GetFileName(path), optional: true, reloadOnChange: false);
    }

    /// <summary>Writes the connection (password encrypted) so that a half-written file is never seen.</summary>
    public static void Write(string path, SetupConnection connection, string? protectedPassword)
    {
        var database = new Dictionary<string, object>
        {
            ["Provider"] = "SqlServer",
            ["Server"] = connection.Server!.Trim(),
            ["Name"] = connection.Database!.Trim(),
            ["Auth"] = connection.Windows ? "windows" : "sql",
            ["TrustServerCertificate"] = connection.Trust,
        };
        if (!connection.Windows)
        {
            database["User"] = connection.User!.Trim();
            if (protectedPassword is not null) database["PasswordProtected"] = protectedPassword;
        }
        var document = new Dictionary<string, object> { ["Database"] = database };
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + ".tmp";
        System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        System.IO.File.Move(temporary, full, overwrite: true);
    }

    /// <summary>Whether the folder can be written to, found out before asking someone to type a password for it.</summary>
    public static bool CanWrite(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
            System.IO.File.WriteAllText(probe, "x");
            System.IO.File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Protect(IConfiguration config, string password)
    {
        var key = config["Jwt:Key"];
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Jwt:Key is not set");
        return new SecretProtector(key).Protect(password);
    }
}

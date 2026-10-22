using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace KPI.Infrastructure.Data;

/// <summary>How to reach a SQL Server as someone who may create things on it (usually sa), or as the application's own account.</summary>
public sealed record ServerAccess(string Server, bool Windows, string? User, string? Password, bool Trust = true);

/// <summary>What a SQL Server says about itself and about this account, before anything is changed.</summary>
public sealed record ServerProbe(
    bool Connected,
    string? Problem,
    string? Message,
    string? Version,
    bool CanCreateDatabase,
    bool CanCreateLogin,
    bool DatabaseExists,
    bool CanOpenDatabase);

/// <summary>The account made for the application. The password is known only here and is returned once; it is never read back from the server.</summary>
public sealed record AppLoginResult(string Login, string Password, bool Created);

/// <summary>Raised when SQL Server refuses or the request cannot be done safely; the message is for the administrator.</summary>
public sealed class ProvisioningException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The work of <c>deploy/sqlserver/01-create-database.sql</c>, done from inside the application: create the database (once) and
/// an account for the application that owns that database only. Every step can be repeated: a database that exists is not
/// created again, so a second machine that is pointed at the same SQL Server finds the database and only gets an account of its own.
/// </summary>
public static partial class SqlServerProvisioner
{
    /// <summary>Case-insensitive on purpose: a person's e-mail address must be unique whatever its capitals.</summary>
    public const string Collation = "Thai_CI_AS";

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z0-9_]{1,100}$")]
    private static partial Regex LoginPattern();

    public static bool IsValidDatabaseName(string? name) => name is not null && NamePattern().IsMatch(name);

    /// <summary>"helpdesk_" plus this computer's name, so every machine that connects to the same SQL Server has an account of its own (and can be removed on its own).</summary>
    public static string DefaultLoginName(string? machineName = null)
    {
        var cleaned = Regex.Replace((machineName ?? Environment.MachineName).ToLowerInvariant(), "[^a-z0-9]", "_").Trim('_');
        if (cleaned.Length > 60) cleaned = cleaned[..60];
        return cleaned.Length == 0 ? "helpdesk_app" : "helpdesk_" + cleaned;
    }

    private static string ConnectionString(ServerAccess access, string database, int timeoutSeconds = 10) =>
        DatabaseProvider.QuickConnectionString(DatabaseProvider.BuildSqlServer(access.Server, database, access.Windows, access.User, access.Password, access.Trust, timeoutSeconds))!;

    /// <summary>Connects to the server (not to the database) and reports what this account may do, and whether the database is already there.</summary>
    public static async Task<ServerProbe> ProbeAsync(ServerAccess access, string database, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(ConnectionString(access, "master"));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CONCAT(CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)), N' (', CAST(SERVERPROPERTY('Edition') AS nvarchar(100)), N')'),
                       ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, 'CREATE ANY DATABASE'), 0),
                       ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY LOGIN'), 0),
                       DB_ID(@db),
                       ISNULL(HAS_DBACCESS(@db), 0)
                """;
            command.Parameters.AddWithValue("@db", database);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new ServerProbe(true, null, null, reader.GetString(0), reader.GetInt32(1) == 1, reader.GetInt32(2) == 1, !reader.IsDBNull(3), reader.GetInt32(4) == 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var (problem, message) = DatabaseDoctor.ClassifyConnectionFailure(ex is OperationCanceledException ? new TimeoutException("The server did not answer in time.") : ex);
            return new ServerProbe(false, problem, message, null, false, false, false, false);
        }
    }

    /// <summary>Creates the database unless it is there. Returns true when this call created it (false: it already existed, perhaps made a moment ago by another machine).</summary>
    public static async Task<bool> EnsureDatabaseAsync(ServerAccess access, string database, CancellationToken cancellationToken = default)
    {
        if (!IsValidDatabaseName(database)) throw new ProvisioningException("invalid", "ชื่อฐานข้อมูลใช้ได้เฉพาะตัวอักษร ตัวเลข _ - และ .");
        try
        {
            await using var connection = new SqlConnection(ConnectionString(access, "master"));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 180;   // a new database file can take a while on a slow disk
            // AUTO_CLOSE (on by default for new databases on Express and LocalDB) shuts the database after its last connection and makes the next one wait for it to reopen
            command.CommandText = $"IF DB_ID(@db) IS NULL BEGIN CREATE DATABASE [{database}] COLLATE {Collation}; ALTER DATABASE [{database}] SET AUTO_CLOSE OFF; SELECT 1; END ELSE SELECT 0;";
            command.Parameters.AddWithValue("@db", database);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        catch (SqlException ex) when (ex.Number == 1801)
        {
            return false;   // another machine created it between our check and our statement
        }
        catch (SqlException ex) when (ex.Number is 262 or 229 or 297)
        {
            throw new ProvisioningException("no-permission", "บัญชีนี้สร้างฐานข้อมูลไม่ได้ ต้องเป็น sysadmin หรือมีบทบาท dbcreator (ใช้บัญชี sa หรือให้ผู้ดูแล SQL สร้างฐานข้อมูลนี้ไว้ก่อน)");
        }
    }

    /// <summary>
    /// Makes a SQL login that owns this one database and nothing else, with a new random password. A login of that name that was made
    /// before (by this machine) gets a new password; a login that is a sysadmin, or is not a SQL login, is never touched.
    /// </summary>
    public static async Task<AppLoginResult> EnsureAppLoginAsync(ServerAccess access, string database, string login, CancellationToken cancellationToken = default)
    {
        if (!IsValidDatabaseName(database)) throw new ProvisioningException("invalid", "ชื่อฐานข้อมูลใช้ได้เฉพาะตัวอักษร ตัวเลข _ - และ .");
        if (!LoginPattern().IsMatch(login)) throw new ProvisioningException("invalid", "ชื่อบัญชีใช้ได้เฉพาะตัวอักษร ตัวเลข และ _");

        var password = NewPassword();
        bool created;
        try
        {
            await using var master = new SqlConnection(ConnectionString(access, "master"));
            await master.OpenAsync(cancellationToken);

            string? kind;
            bool isAdmin;
            await using (var look = master.CreateCommand())
            {
                look.CommandText = "SELECT [type] FROM sys.server_principals WHERE [name] = @login";
                look.Parameters.AddWithValue("@login", login);
                kind = (string?)await look.ExecuteScalarAsync(cancellationToken);
            }
            await using (var admin = master.CreateCommand())
            {
                admin.CommandText = "SELECT ISNULL(IS_SRVROLEMEMBER('sysadmin', @login), 0)";
                admin.Parameters.AddWithValue("@login", login);
                isAdmin = Convert.ToInt32(await admin.ExecuteScalarAsync(cancellationToken)) == 1;
            }
            if (kind is not null && (kind != "S" || isAdmin))
                throw new ProvisioningException("login-in-use", $"มีบัญชี {login} อยู่แล้วและไม่ใช่บัญชี SQL ธรรมดา (หรือเป็น sysadmin) จึงไม่แก้ไข เลือกชื่อบัญชีอื่น");

            created = kind is null;
            await using (var change = master.CreateCommand())
            {
                // The password is generated here from letters and digits only, so it can be written into the statement; the names are checked above.
                change.CommandText = created
                    ? $"CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{database}]"
                    : $"ALTER LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{database}]";
                await change.ExecuteNonQueryAsync(cancellationToken);
            }

            await master.ChangeDatabaseAsync(database, cancellationToken);
            await using var grant = master.CreateCommand();
            // A login that can already do everything is the database's dbo and needs neither a user nor the role.
            grant.CommandText = """
                DECLARE @user sysname = (SELECT [name] FROM sys.database_principals WHERE [sid] = SUSER_SID(@login) AND [type] IN ('S', 'U'));
                IF @user IS NULL
                BEGIN
                    DECLARE @create nvarchar(400) = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
                    EXEC (@create);
                    SET @user = @login;
                END
                IF @user <> N'dbo' AND IS_ROLEMEMBER(N'db_owner', @user) <> 1
                BEGIN
                    DECLARE @role nvarchar(400) = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@user);
                    EXEC (@role);
                END
                """;
            grant.Parameters.AddWithValue("@login", login);
            await grant.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 15247 or 300 or 262 or 229)
        {
            throw new ProvisioningException("no-permission", "บัญชีนี้สร้างบัญชีสำหรับโปรแกรมไม่ได้ ต้องเป็น sysadmin (หรือมีบทบาท securityadmin) หรือเลือก \"ใช้บัญชีนี้เป็นบัญชีของโปรแกรมด้วย\"");
        }
        catch (SqlException ex) when (ex.Number == 15118)
        {
            throw new ProvisioningException("weak-password", "SQL Server ปฏิเสธรหัสผ่านที่สร้างให้ (นโยบายรหัสผ่านของเครื่องเซิร์ฟเวอร์) ลองใหม่อีกครั้ง");
        }
        return new AppLoginResult(login, password, created);
    }

    /// <summary>A random password of 28 letters and digits plus a suffix that satisfies the usual complexity rule.</summary>
    public static string NewPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(28);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray()) + "Aa1";
    }
}

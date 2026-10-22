-- Helpdesk on SQL Server, step 1 of 3: the database and the account the application connects with.
--
-- Run it with sqlcmd as someone who may create databases and logins (install.ps1 does this for you):
--   sqlcmd -S <server> -E -b -f 65001 -i 01-create-database.sql ^
--          -v DbName="helpdesk" AuthMode="SQL" AppLogin="helpdesk_app" AppPassword="<a long password without a quote mark>"
--   AuthMode = SQL      the application signs in with the SQL login AppLogin / AppPassword
--   AuthMode = WINDOWS  the application runs as the Windows account in AppLogin (DOMAIN\account, or NT SERVICE\Helpdesk
--                       for a service on the same machine as SQL Server); AppPassword is ignored
--
-- In SQL Server Management Studio (no command line): open this file, choose Query > SQLCMD Mode, add these four lines at the very
-- top (with your own values) and press Execute:
--   :setvar DbName "helpdesk"
--   :setvar AuthMode "SQL"
--   :setvar AppLogin "helpdesk_app"
--   :setvar AppPassword "<a long password without a quote mark>"
--
-- When it has run, tell Helpdesk where the database is: the program shows a setup page for it (install.ps1 -ConfigureLater, or
-- any server that does not know its database yet); the page checks which tables are missing and creates them. See README-th.md.
-- Safe to run again.
--
-- The collation is case-insensitive on purpose: a person's e-mail address must be unique whatever its capitals.
-- The account is db_owner of this one database only: the application creates and updates its own tables (and
-- Hangfire's) when it starts, which needs that right. It has no rights on the rest of the server.

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'$(DbName)') IS NULL
BEGIN
    PRINT N'Creating database $(DbName)';
    EXEC (N'CREATE DATABASE [$(DbName)] COLLATE Thai_CI_AS');
END
ELSE
    PRINT N'Database $(DbName) already exists';
GO

IF N'$(AuthMode)' = N'SQL'
BEGIN
    IF SUSER_ID(N'$(AppLogin)') IS NULL
    BEGIN
        PRINT N'Creating login $(AppLogin)';
        EXEC (N'CREATE LOGIN [$(AppLogin)] WITH PASSWORD = N''$(AppPassword)'', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [$(DbName)]');
    END
    ELSE
        PRINT N'Login $(AppLogin) already exists (its password is left as it is)';
END
ELSE IF N'$(AuthMode)' = N'WINDOWS'
BEGIN
    IF SUSER_ID(N'$(AppLogin)') IS NULL
    BEGIN
        PRINT N'Creating Windows login $(AppLogin)';
        EXEC (N'CREATE LOGIN [$(AppLogin)] FROM WINDOWS WITH DEFAULT_DATABASE = [$(DbName)]');
    END
    ELSE
        PRINT N'Login $(AppLogin) already exists';
END
ELSE
    THROW 50000, N'AuthMode must be SQL or WINDOWS', 1;
GO

USE [$(DbName)];
GO

-- A login that can already do everything (a sysadmin, or whoever created the database) is the database's dbo and needs
-- neither a user nor the role.
DECLARE @user sysname = (SELECT [name] FROM sys.database_principals WHERE [sid] = SUSER_SID(N'$(AppLogin)') AND [type] IN ('S', 'U'));
IF @user IS NULL
BEGIN
    EXEC (N'CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)]');
    SET @user = N'$(AppLogin)';
END
IF @user <> N'dbo' AND IS_ROLEMEMBER(N'db_owner', @user) <> 1
BEGIN
    DECLARE @grant nvarchar(400) = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@user);
    EXEC (@grant);
END
GO

PRINT N'Database $(DbName) is ready for $(AppLogin).';
GO

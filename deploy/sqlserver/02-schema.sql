-- Helpdesk on SQL Server, step 2 of 3: the tables.
-- GENERATED from the SQL Server migrations (see DeploymentScriptTests). Do not edit by hand.
-- Run it against the database from step 1:  sqlcmd -S <server> -d <database> -b -f 65001 -i 02-schema.sql
-- Safe to run again: it only applies the migrations the database does not have yet. The application does the
-- same thing itself when it starts (unless Database__AutoMigrate=false), so this file is for people who want
-- to review and run the change by hand.

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [AppSettingHistory] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [OldValue] nvarchar(500) NULL,
        [NewValue] nvarchar(500) NULL,
        [ChangedAt] datetime2 NOT NULL,
        [ChangedById] uniqueidentifier NULL,
        [ChangedByName] nvarchar(200) NULL,
        CONSTRAINT [PK_AppSettingHistory] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [AppSettings] (
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(500) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedByName] nvarchar(200) NULL,
        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [Action] nvarchar(max) NOT NULL,
        [EntityName] nvarchar(200) NOT NULL,
        [EntityId] nvarchar(200) NOT NULL,
        [OldValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NULL,
        [UserId] uniqueidentifier NULL,
        [UserEmail] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UserName] nvarchar(200) NULL,
        [UserRole] nvarchar(50) NULL,
        [DepartmentName] nvarchar(200) NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(400) NULL,
        [RequestId] nvarchar(64) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [AuthLogs] (
        [Id] uniqueidentifier NOT NULL,
        [At] datetime2 NOT NULL,
        [Event] nvarchar(40) NOT NULL,
        [Success] bit NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Email] nvarchar(254) NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(400) NULL,
        [Reason] nvarchar(100) NULL,
        [Detail] nvarchar(300) NULL,
        CONSTRAINT [PK_AuthLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Module] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [RequestLogs] (
        [Id] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UserId] uniqueidentifier NULL,
        [UserEmail] nvarchar(254) NULL,
        [UserRole] nvarchar(50) NULL,
        [Method] nvarchar(10) NOT NULL,
        [Path] nvarchar(300) NOT NULL,
        [StatusCode] int NOT NULL,
        [DurationMs] int NOT NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(400) NULL,
        [RequestId] nvarchar(64) NULL,
        CONSTRAINT [PK_RequestLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [IssueVisibility] int NOT NULL,
        [SeesAllDepartments] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] uniqueidentifier NOT NULL,
        [PermissionId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Departments] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Code] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [ManagerId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [FirstName] nvarchar(max) NOT NULL,
        [LastName] nvarchar(max) NOT NULL,
        [Email] nvarchar(450) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [IsActive] bit NOT NULL,
        [PasswordChangedAt] datetime2 NULL,
        [MustChangePassword] bit NOT NULL,
        [FailedLoginCount] int NOT NULL,
        [LockedUntil] datetime2 NULL,
        [RoleId] uniqueidentifier NOT NULL,
        [DepartmentId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Users_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Issues] (
        [Id] uniqueidentifier NOT NULL,
        [Number] int NOT NULL,
        [NumberYear] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(4000) NOT NULL,
        [Category] int NOT NULL,
        [Module] int NOT NULL,
        [Impact] int NOT NULL,
        [Priority] int NOT NULL,
        [Status] int NOT NULL,
        [ReporterUserId] uniqueidentifier NULL,
        [ReporterName] nvarchar(200) NOT NULL,
        [ReporterEmail] nvarchar(254) NULL,
        [ReporterPhone] nvarchar(40) NULL,
        [IsPublicSubmission] bit NOT NULL,
        [DepartmentId] uniqueidentifier NULL,
        [AssigneeId] uniqueidentifier NULL,
        [ResponseDueAt] datetime2 NOT NULL,
        [FirstResponseAt] datetime2 NULL,
        [ResolveDueAt] datetime2 NOT NULL,
        [WaitingSince] datetime2 NULL,
        [ResolvedAt] datetime2 NULL,
        [ClosedAt] datetime2 NULL,
        [ReopenCount] int NOT NULL,
        [RootCause] int NOT NULL,
        [Resolution] nvarchar(4000) NULL,
        [Prevention] nvarchar(2000) NULL,
        [FixReference] nvarchar(500) NULL,
        [TimeSpentMinutes] int NULL,
        [RejectReason] nvarchar(1000) NULL,
        [DuplicateOfId] uniqueidentifier NULL,
        [PageUrl] nvarchar(500) NULL,
        [UserAgent] nvarchar(400) NULL,
        [ClientInfo] nvarchar(1000) NULL,
        [ErrorRequestId] nvarchar(100) NULL,
        [ReporterIp] nvarchar(64) NULL,
        [Version] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Issues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Issues_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]),
        CONSTRAINT [FK_Issues_Users_AssigneeId] FOREIGN KEY ([AssigneeId]) REFERENCES [Users] ([Id]),
        CONSTRAINT [FK_Issues_Users_ReporterUserId] FOREIGN KEY ([ReporterUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [PasswordHistory] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PasswordHash] nvarchar(100) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PasswordHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PasswordHistory_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [IssueAttachments] (
        [Id] uniqueidentifier NOT NULL,
        [IssueId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] int NOT NULL,
        [Data] varbinary(max) NOT NULL,
        [UploadedById] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IssueAttachments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IssueAttachments_Issues_IssueId] FOREIGN KEY ([IssueId]) REFERENCES [Issues] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [IssueComments] (
        [Id] uniqueidentifier NOT NULL,
        [IssueId] uniqueidentifier NOT NULL,
        [AuthorId] uniqueidentifier NULL,
        [AuthorName] nvarchar(200) NOT NULL,
        [IsStaff] bit NOT NULL,
        [IsInternal] bit NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IssueComments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_IssueComments_Issues_IssueId] FOREIGN KEY ([IssueId]) REFERENCES [Issues] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AppSettingHistory_Key_ChangedAt] ON [AppSettingHistory] ([Key], [ChangedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityName_EntityId] ON [AuditLogs] ([EntityName], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_IpAddress] ON [AuditLogs] ([IpAddress]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId_CreatedAt] ON [AuditLogs] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AuthLogs_At] ON [AuthLogs] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AuthLogs_Event_At] ON [AuthLogs] ([Event], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_AuthLogs_UserId_At] ON [AuthLogs] ([UserId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Departments_ManagerId] ON [Departments] ([ManagerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_IssueAttachments_IssueId] ON [IssueAttachments] ([IssueId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_IssueComments_IssueId_CreatedAt] ON [IssueComments] ([IssueId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Issues_AssigneeId_Status] ON [Issues] ([AssigneeId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Issues_CreatedAt] ON [Issues] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Issues_DepartmentId_Status] ON [Issues] ([DepartmentId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Issues_NumberYear_Number] ON [Issues] ([NumberYear], [Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Issues_ReporterUserId_CreatedAt] ON [Issues] ([ReporterUserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Issues_Status] ON [Issues] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Issues_Status_ResolveDueAt] ON [Issues] ([Status], [ResolveDueAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_PasswordHistory_UserId_CreatedAt] ON [PasswordHistory] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_RequestLogs_CreatedAt] ON [RequestLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_RequestLogs_IpAddress_CreatedAt] ON [RequestLogs] ([IpAddress], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_RequestLogs_StatusCode_CreatedAt] ON [RequestLogs] ([StatusCode], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_RequestLogs_UserId_CreatedAt] ON [RequestLogs] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Users_DepartmentId] ON [Users] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    CREATE INDEX [IX_Users_RoleId] ON [Users] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    ALTER TABLE [Departments] ADD CONSTRAINT [FK_Departments_Users_ManagerId] FOREIGN KEY ([ManagerId]) REFERENCES [Users] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021092941_InitialSqlServer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021092941_InitialSqlServer', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026023812_AddSearchAndSummaryIndexes'
)
BEGIN
    DROP INDEX [IX_RequestLogs_CreatedAt] ON [RequestLogs];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026023812_AddSearchAndSummaryIndexes'
)
BEGIN
    DROP INDEX [IX_Issues_Status] ON [Issues];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026023812_AddSearchAndSummaryIndexes'
)
BEGIN
    CREATE INDEX [IX_RequestLogs_CreatedAt] ON [RequestLogs] ([CreatedAt]) INCLUDE ([Method], [StatusCode], [IpAddress], [Path]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026023812_AddSearchAndSummaryIndexes'
)
BEGIN
    CREATE INDEX [IX_Issues_ResolvedAt] ON [Issues] ([ResolvedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026023812_AddSearchAndSummaryIndexes'
)
BEGIN
    CREATE INDEX [IX_Issues_Status] ON [Issues] ([Status]) INCLUDE ([ResponseDueAt], [ResolveDueAt], [FirstResponseAt], [ReporterUserId], [AssigneeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026023812_AddSearchAndSummaryIndexes'
)
BEGIN
    CREATE INDEX [IX_Issues_Title] ON [Issues] ([Title]) INCLUDE ([ReporterName], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026023812_AddSearchAndSummaryIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026023812_AddSearchAndSummaryIndexes', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029091459_AddNotifications'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [IssueId] uniqueidentifier NULL,
        [Kind] nvarchar(40) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [Level] nvarchar(10) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ReadAt] datetime2 NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029091459_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_CreatedAt] ON [Notifications] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029091459_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_CreatedAt] ON [Notifications] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029091459_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_IssueId] ON [Notifications] ([UserId], [IssueId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029091459_AddNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_ReadAt] ON [Notifications] ([UserId], [ReadAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029091459_AddNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029091459_AddNotifications', N'10.0.8');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029093413_AddIssueFeedback'
)
BEGIN
    CREATE TABLE [IssueFeedbacks] (
        [IssueId] uniqueidentifier NOT NULL,
        [Rating] int NOT NULL,
        [Note] nvarchar(1000) NULL,
        [RaterUserId] uniqueidentifier NOT NULL,
        [AssigneeId] uniqueidentifier NULL,
        [RatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_IssueFeedbacks] PRIMARY KEY ([IssueId]),
        CONSTRAINT [CK_IssueFeedbacks_Rating] CHECK ([Rating] BETWEEN 1 AND 5),
        CONSTRAINT [FK_IssueFeedbacks_Issues_IssueId] FOREIGN KEY ([IssueId]) REFERENCES [Issues] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029093413_AddIssueFeedback'
)
BEGIN
    CREATE INDEX [IX_IssueFeedbacks_RatedAt] ON [IssueFeedbacks] ([RatedAt]) INCLUDE ([Rating], [AssigneeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029093413_AddIssueFeedback'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029093413_AddIssueFeedback', N'10.0.8');
END;

COMMIT;
GO


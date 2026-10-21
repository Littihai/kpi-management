-- Roles, permissions and what each built-in role may do.
-- GENERATED from DbSeeder and RolePermissionMap (see DeploymentScriptTests). Do not edit by hand.
-- Safe to run again: it only adds what is missing and puts the built-in roles back to their defaults.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Built-in roles
IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = N'SuperAdmin')
    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])
    VALUES ('00000000-0000-0000-0000-000000000001', N'SuperAdmin', N'ผู้ดูแลระบบสูงสุด: ทำได้ทุกอย่าง', 2, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
ELSE
    UPDATE [Roles] SET [Description] = N'ผู้ดูแลระบบสูงสุด: ทำได้ทุกอย่าง', [IssueVisibility] = 2, [SeesAllDepartments] = 1
    WHERE [Name] = N'SuperAdmin';
IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = N'Director')
    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])
    VALUES ('00000000-0000-0000-0000-000000000002', N'Director', N'ผู้อำนวยการ: ดูได้ทุกแผนก', 2, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
ELSE
    UPDATE [Roles] SET [Description] = N'ผู้อำนวยการ: ดูได้ทุกแผนก', [IssueVisibility] = 2, [SeesAllDepartments] = 1
    WHERE [Name] = N'Director';
IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = N'Manager')
    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])
    VALUES ('00000000-0000-0000-0000-000000000003', N'Manager', N'ผู้จัดการ: ดูเรื่องและผู้ใช้ของแผนกตัวเอง', 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
ELSE
    UPDATE [Roles] SET [Description] = N'ผู้จัดการ: ดูเรื่องและผู้ใช้ของแผนกตัวเอง', [IssueVisibility] = 1, [SeesAllDepartments] = 0
    WHERE [Name] = N'Manager';
IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = N'TeamLeader')
    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])
    VALUES ('00000000-0000-0000-0000-000000000004', N'TeamLeader', N'หัวหน้าทีม: แจ้งและติดตามเรื่อง', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
ELSE
    UPDATE [Roles] SET [Description] = N'หัวหน้าทีม: แจ้งและติดตามเรื่อง', [IssueVisibility] = 0, [SeesAllDepartments] = 0
    WHERE [Name] = N'TeamLeader';
IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = N'Staff')
    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])
    VALUES ('00000000-0000-0000-0000-000000000005', N'Staff', N'พนักงาน: แจ้งและติดตามเรื่องของตัวเอง', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
ELSE
    UPDATE [Roles] SET [Description] = N'พนักงาน: แจ้งและติดตามเรื่องของตัวเอง', [IssueVisibility] = 0, [SeesAllDepartments] = 0
    WHERE [Name] = N'Staff';
IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = N'Viewer')
    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])
    VALUES ('00000000-0000-0000-0000-000000000006', N'Viewer', N'ผู้ชม: แจ้งและติดตามเรื่องของตัวเอง', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
ELSE
    UPDATE [Roles] SET [Description] = N'ผู้ชม: แจ้งและติดตามเรื่องของตัวเอง', [IssueVisibility] = 0, [SeesAllDepartments] = 0
    WHERE [Name] = N'Viewer';
IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = N'ITSupport')
    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])
    VALUES ('00000000-0000-0000-0000-000000000007', N'ITSupport', N'ทีม IT: ทำงานในคิวแจ้งปัญหา', 2, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
ELSE
    UPDATE [Roles] SET [Description] = N'ทีม IT: ทำงานในคิวแจ้งปัญหา', [IssueVisibility] = 2, [SeesAllDepartments] = 0
    WHERE [Name] = N'ITSupport';

-- Permissions
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'user.view')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('a344211d-e218-0e4a-1645-75b3f91d0619', N'user.view', N'user', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'user.create')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('7bec8326-fbd9-a190-6699-cd46b49b637b', N'user.create', N'user', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'user.edit')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('202a67dc-7cc7-9129-cb72-9c3d8b859eb7', N'user.edit', N'user', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'user.delete')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('f51e9368-ee23-92e2-4709-d0ff50469445', N'user.delete', N'user', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'role.view')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('3c12eb18-0720-729e-b38f-afa699383b74', N'role.view', N'role', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'role.manage')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('43949423-144a-8360-038c-cabbe9a25b5e', N'role.manage', N'role', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'settings.view')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('3edc5ee2-1cf4-d068-4ef4-8b63ba93c27b', N'settings.view', N'settings', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'settings.manage')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('bc15adca-17c6-0dd3-f319-db10b53491e9', N'settings.manage', N'settings', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'department.view')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('3890232b-edc4-6f13-5249-1294d97c42eb', N'department.view', N'department', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'department.manage')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('f8b5fbde-729a-5a22-bb04-f98e07f293ac', N'department.manage', N'department', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'audit.view')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('48feee1d-3e46-1f86-e2b9-a3b5d768002d', N'audit.view', N'audit', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'job.trigger')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('06179a51-97e4-8ab7-8a75-ee252dad8f30', N'job.trigger', N'job', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'issue.create')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('ddc097b2-3893-ebf5-b523-c61f9803558f', N'issue.create', N'issue', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'issue.view')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('d2870716-c4ba-fee1-9e79-fad69e47095d', N'issue.view', N'issue', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'issue.manage')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('666992f8-3d6f-620e-b5bc-0e49f0bcd4d9', N'issue.manage', N'issue', SYSUTCDATETIME(), SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = N'overview.view')
    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])
    VALUES ('3c66f1e7-133a-c2c8-49dc-244acc03e484', N'overview.view', N'overview', SYSUTCDATETIME(), SYSUTCDATETIME());

-- What each built-in role may do (roles an administrator created are not touched)
DELETE rp FROM [RolePermissions] rp
    JOIN [Roles] r ON r.[Id] = rp.[RoleId]
    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Name] = N'SuperAdmin' AND p.[Name] NOT IN (N'audit.view', N'department.manage', N'department.view', N'issue.create', N'issue.manage', N'issue.view', N'job.trigger', N'overview.view', N'role.manage', N'role.view', N'settings.manage', N'settings.view', N'user.create', N'user.delete', N'user.edit', N'user.view');
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p
    WHERE r.[Name] = N'SuperAdmin' AND p.[Name] IN (N'audit.view', N'department.manage', N'department.view', N'issue.create', N'issue.manage', N'issue.view', N'job.trigger', N'overview.view', N'role.manage', N'role.view', N'settings.manage', N'settings.view', N'user.create', N'user.delete', N'user.edit', N'user.view')
      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);
DELETE rp FROM [RolePermissions] rp
    JOIN [Roles] r ON r.[Id] = rp.[RoleId]
    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Name] = N'Director' AND p.[Name] NOT IN (N'department.view', N'issue.create', N'issue.view', N'user.view');
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p
    WHERE r.[Name] = N'Director' AND p.[Name] IN (N'department.view', N'issue.create', N'issue.view', N'user.view')
      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);
DELETE rp FROM [RolePermissions] rp
    JOIN [Roles] r ON r.[Id] = rp.[RoleId]
    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Name] = N'Manager' AND p.[Name] NOT IN (N'department.view', N'issue.create', N'issue.view', N'user.view');
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p
    WHERE r.[Name] = N'Manager' AND p.[Name] IN (N'department.view', N'issue.create', N'issue.view', N'user.view')
      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);
DELETE rp FROM [RolePermissions] rp
    JOIN [Roles] r ON r.[Id] = rp.[RoleId]
    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Name] = N'TeamLeader' AND p.[Name] NOT IN (N'department.view', N'issue.create', N'issue.view');
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p
    WHERE r.[Name] = N'TeamLeader' AND p.[Name] IN (N'department.view', N'issue.create', N'issue.view')
      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);
DELETE rp FROM [RolePermissions] rp
    JOIN [Roles] r ON r.[Id] = rp.[RoleId]
    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Name] = N'Staff' AND p.[Name] NOT IN (N'department.view', N'issue.create', N'issue.view');
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p
    WHERE r.[Name] = N'Staff' AND p.[Name] IN (N'department.view', N'issue.create', N'issue.view')
      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);
DELETE rp FROM [RolePermissions] rp
    JOIN [Roles] r ON r.[Id] = rp.[RoleId]
    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Name] = N'Viewer' AND p.[Name] NOT IN (N'department.view', N'issue.create', N'issue.view');
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p
    WHERE r.[Name] = N'Viewer' AND p.[Name] IN (N'department.view', N'issue.create', N'issue.view')
      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);
DELETE rp FROM [RolePermissions] rp
    JOIN [Roles] r ON r.[Id] = rp.[RoleId]
    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Name] = N'ITSupport' AND p.[Name] NOT IN (N'department.view', N'issue.create', N'issue.manage', N'issue.view');
INSERT INTO [RolePermissions] ([RoleId], [PermissionId])
    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p
    WHERE r.[Name] = N'ITSupport' AND p.[Name] IN (N'department.view', N'issue.create', N'issue.manage', N'issue.view')
      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);

COMMIT TRANSACTION;

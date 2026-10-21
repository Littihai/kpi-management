using System.Text;
using KPI.Domain.Constants;

namespace KPI.Infrastructure.Data;

/// <summary>
/// The roles, permissions and role grants as a T-SQL script, built from the same data the app seeds at start-up
/// (DbSeeder, RolePermissionMap), so the script cannot drift from the code. A test compares it with the file in
/// deploy/sqlserver and fails when they differ. Idempotent: running it again changes nothing.
/// </summary>
public static class SeedScript
{
    public static string BuildSqlServer()
    {
        var sql = new StringBuilder();
        void Line(string text = "") => sql.Append(text).Append('\n');
        static string N(string text) => "N'" + text.Replace("'", "''") + "'";
        static int Bit(bool value) => value ? 1 : 0;

        Line("-- Roles, permissions and what each built-in role may do.");
        Line("-- GENERATED from DbSeeder and RolePermissionMap (see DeploymentScriptTests). Do not edit by hand.");
        Line("-- Safe to run again: it only adds what is missing and puts the built-in roles back to their defaults.");
        Line("SET NOCOUNT ON;");
        Line("SET XACT_ABORT ON;");
        Line("BEGIN TRANSACTION;");
        Line();
        Line("-- Built-in roles");
        foreach (var role in DbSeeder.BuiltInRoles)
        {
            var (issues, allDepartments) = RolePermissionMap.Scopes[role.Name];
            Line($"IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Name] = {N(role.Name)})");
            Line($"    INSERT INTO [Roles] ([Id], [Name], [Description], [IssueVisibility], [SeesAllDepartments], [CreatedAt], [UpdatedAt])");
            Line($"    VALUES ('{role.Id}', {N(role.Name)}, {N(role.Description)}, {(int)issues}, {Bit(allDepartments)}, SYSUTCDATETIME(), SYSUTCDATETIME());");
            Line("ELSE");
            Line($"    UPDATE [Roles] SET [Description] = {N(role.Description)}, [IssueVisibility] = {(int)issues}, [SeesAllDepartments] = {Bit(allDepartments)}");
            Line($"    WHERE [Name] = {N(role.Name)};");
        }
        Line();
        Line("-- Permissions");
        foreach (var name in Permissions.All)
        {
            Line($"IF NOT EXISTS (SELECT 1 FROM [Permissions] WHERE [Name] = {N(name)})");
            Line($"    INSERT INTO [Permissions] ([Id], [Name], [Module], [CreatedAt], [UpdatedAt])");
            Line($"    VALUES ('{DbSeeder.PermissionIdFor(name)}', {N(name)}, {N(name.Split('.')[0])}, SYSUTCDATETIME(), SYSUTCDATETIME());");
        }
        Line();
        Line("-- What each built-in role may do (roles an administrator created are not touched)");
        foreach (var (roleName, wanted) in RolePermissionMap.Defaults.OrderBy(r => Array.IndexOf(RoleNames.All, r.Key)))
        {
            var names = wanted.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            Line($"DELETE rp FROM [RolePermissions] rp");
            Line($"    JOIN [Roles] r ON r.[Id] = rp.[RoleId]");
            Line($"    JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]");
            Line($"    WHERE r.[Name] = {N(roleName)} AND p.[Name] NOT IN ({string.Join(", ", names.Select(N))});");
            Line($"INSERT INTO [RolePermissions] ([RoleId], [PermissionId])");
            Line($"    SELECT r.[Id], p.[Id] FROM [Roles] r CROSS JOIN [Permissions] p");
            Line($"    WHERE r.[Name] = {N(roleName)} AND p.[Name] IN ({string.Join(", ", names.Select(N))})");
            Line($"      AND NOT EXISTS (SELECT 1 FROM [RolePermissions] x WHERE x.[RoleId] = r.[Id] AND x.[PermissionId] = p.[Id]);");
        }
        Line();
        Line("COMMIT TRANSACTION;");
        return sql.ToString();
    }
}

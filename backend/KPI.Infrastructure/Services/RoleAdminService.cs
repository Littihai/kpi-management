using KPI.Application.Admin;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class RoleAdminService : IRoleAdminService
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 50;
    public const int MaxDescriptionLength = 200;

    private readonly AppDbContext _db;

    public RoleAdminService(AppDbContext db) => _db = db;

    public static bool IsSystem(string name) => RoleNames.All.Contains(name);

    public IReadOnlyList<PermissionDto> Catalog() =>
        Permissions.All
            .Select(name =>
            {
                var entry = PermissionCatalog.Find(name);
                return new PermissionDto(name, name.Split('.')[0], entry?.Group ?? "อื่น ๆ", entry?.Label ?? name, entry?.Description ?? string.Empty);
            })
            .ToList();

    public async Task<IReadOnlyList<RoleDto>> ListAsync()
    {
        var roles = await _db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .ToListAsync();
        var counts = await _db.Users.AsNoTracking().GroupBy(u => u.RoleId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

        return roles
            .OrderBy(r => IsSystem(r.Name) ? Array.IndexOf(RoleNames.All, r.Name) : int.MaxValue)
            .ThenBy(r => r.Name)
            .Select(r => ToDto(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
    }

    public async Task<RoleDto> CreateAsync(Guid actorId, SaveRoleRequest request)
    {
        var (name, description, permissions) = Validate(request);
        if (await _db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower()))
            throw new RequestValidationException("มีบทบาทชื่อนี้อยู่แล้ว");

        var actor = await ActorAsync(actorId);
        EnsureCovers(actor.Role, permissions, request.IssueVisibility, request.SeesAllDepartments);

        var ids = await PermissionIdsAsync(permissions);
        var role = new Role
        {
            Name = name,
            Description = description,
            IssueVisibility = request.IssueVisibility,
            SeesAllDepartments = request.SeesAllDepartments,
        };
        foreach (var id in ids) role.RolePermissions.Add(new RolePermission { Role = role, PermissionId = id });
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return await OneAsync(role.Id);
    }

    public async Task<RoleDto> UpdateAsync(Guid actorId, Guid id, SaveRoleRequest request)
    {
        var role = await LoadAsync(id);
        EnsureCustom(role);
        var (name, description, permissions) = Validate(request);
        if (await _db.Roles.AnyAsync(r => r.Id != id && r.Name.ToLower() == name.ToLower()))
            throw new RequestValidationException("มีบทบาทชื่อนี้อยู่แล้ว");

        // You may only edit a role you could have made yourself, and only to something you could have made.
        var actor = await ActorAsync(actorId);
        EnsureCovers(actor.Role, role.RolePermissions.Select(rp => rp.Permission.Name).ToList(), role.IssueVisibility, role.SeesAllDepartments);
        EnsureCovers(actor.Role, permissions, request.IssueVisibility, request.SeesAllDepartments);

        var wanted = (await PermissionIdsAsync(permissions)).ToHashSet();
        _db.RolePermissions.RemoveRange(role.RolePermissions.Where(rp => !wanted.Contains(rp.PermissionId)));
        var have = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        foreach (var pid in wanted.Where(p => !have.Contains(p)))
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = pid });

        role.Name = name;
        role.Description = description;
        role.IssueVisibility = request.IssueVisibility;
        role.SeesAllDepartments = request.SeesAllDepartments;
        role.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await OneAsync(role.Id);
    }

    public async Task DeleteAsync(Guid actorId, Guid id)
    {
        var role = await LoadAsync(id);
        EnsureCustom(role);
        var actor = await ActorAsync(actorId);
        EnsureCovers(actor.Role, role.RolePermissions.Select(rp => rp.Permission.Name).ToList(), role.IssueVisibility, role.SeesAllDepartments);

        var users = await _db.Users.CountAsync(u => u.RoleId == id);
        if (users > 0)
            throw new InvalidStateException($"ลบไม่ได้ ยังมีผู้ใช้ {users} คนใช้บทบาทนี้ ย้ายผู้ใช้ไปบทบาทอื่นก่อน");

        _db.RolePermissions.RemoveRange(role.RolePermissions);
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
    }

    public async Task EnsureMayAssignAsync(Guid actorId, Guid roleId)
    {
        var actor = await ActorAsync(actorId);
        var role = await LoadAsync(roleId);
        EnsureCovers(actor.Role, role.RolePermissions.Select(rp => rp.Permission.Name).ToList(), role.IssueVisibility, role.SeesAllDepartments,
            "คุณกำหนดบทบาทที่มีสิทธิ์มากกว่าบทบาทของตัวเองไม่ได้");
    }

    // ───────────── helpers ─────────────

    private static (string Name, string Description, List<string> Permissions) Validate(SaveRoleRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length < MinNameLength || name.Length > MaxNameLength)
            throw new RequestValidationException($"ชื่อบทบาทต้องยาว {MinNameLength}-{MaxNameLength} ตัวอักษร");
        var description = (request.Description ?? string.Empty).Trim();
        if (description.Length > MaxDescriptionLength)
            throw new RequestValidationException($"คำอธิบายยาวได้ไม่เกิน {MaxDescriptionLength} ตัวอักษร");
        if (!Enum.IsDefined(request.IssueVisibility))
            throw new RequestValidationException("ขอบเขตการเห็นเรื่องไม่ถูกต้อง");

        var permissions = (request.Permissions ?? []).Select(p => p?.Trim() ?? string.Empty).Distinct().ToList();
        if (permissions.Count == 0)
            throw new RequestValidationException("เลือกสิทธิ์อย่างน้อยหนึ่งรายการ");
        var unknown = permissions.FirstOrDefault(p => !Permissions.All.Contains(p));
        if (unknown is not null)
            throw new RequestValidationException($"ไม่รู้จักสิทธิ์ '{unknown}'");
        return (name, description, permissions);
    }

    private static void EnsureCustom(Role role)
    {
        if (IsSystem(role.Name))
            throw new ForbiddenException("บทบาทของระบบแก้ไขหรือลบไม่ได้ สิทธิ์ของบทบาทเหล่านี้ถูกกำหนดไว้ตายตัว");
    }

    /// <summary>The actor must hold every permission and see at least as much as what is being granted.</summary>
    private static void EnsureCovers(Role actorRole, IReadOnlyCollection<string> permissions, IssueVisibility visibility,
        bool allDepartments, string? message = null)
    {
        var held = actorRole.RolePermissions.Select(rp => rp.Permission.Name).ToHashSet();
        if (permissions.Any(p => !held.Contains(p))
            || visibility > actorRole.IssueVisibility
            || (allDepartments && !actorRole.SeesAllDepartments))
            throw new ForbiddenException(message ?? "ให้สิทธิ์ที่เกินกว่าที่คุณมีไม่ได้");
    }

    private async Task<User> ActorAsync(Guid actorId) =>
        await _db.Users.Include(u => u.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == actorId && u.IsActive)
        ?? throw new ForbiddenException("บัญชีของคุณไม่พร้อมใช้งาน");

    private async Task<Role> LoadAsync(Guid id) =>
        await _db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission).FirstOrDefaultAsync(r => r.Id == id)
        ?? throw new NotFoundException("ไม่พบบทบาทนี้");

    private async Task<List<Guid>> PermissionIdsAsync(IEnumerable<string> names)
    {
        var wanted = names.ToList();
        var ids = await _db.Permissions.Where(p => wanted.Contains(p.Name)).Select(p => p.Id).ToListAsync();
        if (ids.Count != wanted.Count) throw new RequestValidationException("ไม่พบสิทธิ์บางรายการ");
        return ids;
    }

    private async Task<RoleDto> OneAsync(Guid id)
    {
        var role = await _db.Roles.AsNoTracking().Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission).FirstAsync(r => r.Id == id);
        return ToDto(role, await _db.Users.CountAsync(u => u.RoleId == id));
    }

    private static RoleDto ToDto(Role r, int users) =>
        new(r.Id, r.Name, r.Description, IsSystem(r.Name), users,
            r.RolePermissions.Select(rp => rp.Permission.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            r.IssueVisibility, r.SeesAllDepartments);
}

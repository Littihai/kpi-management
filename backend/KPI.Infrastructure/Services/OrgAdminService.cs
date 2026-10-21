using KPI.Application.Admin;
using KPI.Application.Auth;
using KPI.Application.Common;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class OrgAdminService : IOrgAdminService
{
    public const int MaxNameLength = 100;
    public const int MaxCodeLength = 20;

    private readonly AppDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly IAuthLog _authLog;
    private readonly TimeProvider _time;

    public OrgAdminService(AppDbContext db, IPasswordService passwords, IAuthLog authLog, TimeProvider time)
    {
        _db = db;
        _passwords = passwords;
        _authLog = authLog;
        _time = time;
    }

    // ───────────── Departments ─────────────

    public async Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync()
    {
        var rows = await _db.Departments.AsNoTracking()
            .OrderByDescending(d => d.IsActive).ThenBy(d => d.Name)
            .Select(d => new
            {
                d.Id, d.Code, d.Name, d.IsActive, d.ManagerId,
                ManagerFirst = d.Manager != null ? d.Manager.FirstName : null,
                ManagerLast = d.Manager != null ? d.Manager.LastName : null,
                Users = d.Users.Count(u => u.IsActive),
            })
            .ToListAsync();

        return rows.Select(d => new DepartmentDto(d.Id, d.Code, d.Name, d.IsActive, d.ManagerId,
            d.ManagerFirst is null ? null : $"{d.ManagerFirst} {d.ManagerLast}".Trim(), d.Users)).ToList();
    }

    public async Task<DepartmentDto> CreateDepartmentAsync(SaveDepartmentRequest request)
    {
        var (code, name) = await ValidateDepartmentAsync(request, existingId: null);
        var department = new Department { Code = code, Name = name, ManagerId = request.ManagerId, IsActive = request.IsActive };
        _db.Departments.Add(department);
        await _db.SaveChangesAsync();
        return await GetDepartmentAsync(department.Id);
    }

    public async Task<DepartmentDto> UpdateDepartmentAsync(Guid id, SaveDepartmentRequest request)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new NotFoundException("ไม่พบแผนกนี้");
        var (code, name) = await ValidateDepartmentAsync(request, id);

        department.Code = code;
        department.Name = name;
        department.ManagerId = request.ManagerId;
        department.IsActive = request.IsActive;
        department.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetDepartmentAsync(id);
    }

    private async Task<DepartmentDto> GetDepartmentAsync(Guid id) =>
        (await ListDepartmentsAsync()).First(d => d.Id == id);

    private async Task<(string Code, string Name)> ValidateDepartmentAsync(SaveDepartmentRequest request, Guid? existingId)
    {
        var name = (request.Name ?? "").Trim();
        var code = (request.Code ?? "").Trim().ToUpperInvariant();

        if (name.Length is < 2 or > MaxNameLength)
            throw new RequestValidationException($"ชื่อแผนกต้องยาว 2–{MaxNameLength} ตัวอักษร");
        if (code.Length > MaxCodeLength)
            throw new RequestValidationException($"รหัสแผนกต้องไม่เกิน {MaxCodeLength} ตัวอักษร");

        var lowerName = name.ToLower();
        if (await _db.Departments.AnyAsync(d => d.Id != existingId && d.Name.ToLower() == lowerName))
            throw new RequestValidationException("มีแผนกชื่อนี้อยู่แล้ว");
        if (code.Length > 0 && await _db.Departments.AnyAsync(d => d.Id != existingId && d.Code.ToUpper() == code))
            throw new RequestValidationException("มีแผนกที่ใช้รหัสนี้อยู่แล้ว");

        if (request.ManagerId is { } managerId
            && !await _db.Users.AnyAsync(u => u.Id == managerId && u.IsActive))
            throw new RequestValidationException("ไม่พบผู้ใช้ที่เลือกเป็นหัวหน้าแผนก หรือบัญชีถูกปิดใช้งาน");

        return (code, name);
    }

    // ───────────── Users ─────────────

    public async Task<PagedResult<UserRowDto>> SearchUsersAsync(Guid callerId, UserQuery query)
    {
        var caller = await _db.Users.Include(u => u.Role).AsNoTracking().FirstAsync(u => u.Id == callerId);

        var users = _db.Users.AsNoTracking().AsQueryable();
        if (!caller.Role.SeesAllDepartments)
        {
            var departmentId = caller.DepartmentId;
            users = users.Where(u => departmentId != null && u.DepartmentId == departmentId);
        }

        if (query.DepartmentId is { } filterDepartment) users = users.Where(u => u.DepartmentId == filterDepartment);
        if (!string.IsNullOrWhiteSpace(query.Role)) users = users.Where(u => u.Role.Name == query.Role);
        if (query.Active is { } active) users = users.Where(u => u.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            users = users.Where(u => u.FirstName.ToLower().Contains(term)
                || u.LastName.ToLower().Contains(term)
                || u.Email.ToLower().Contains(term));
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var page = PagedResult<UserRowDto>.ClampPage(query.Page);
        var size = PagedResult<UserRowDto>.ClampSize(query.PageSize);
        var total = await users.CountAsync();
        var items = await users
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(u => new UserRowDto(u.Id, u.FirstName, u.LastName, u.Email, u.IsActive, u.Role.Name,
                u.DepartmentId, u.Department != null ? u.Department.Name : null,
                u.LockedUntil != null && u.LockedUntil > now ? u.LockedUntil : null))
            .ToListAsync();

        return new PagedResult<UserRowDto>(items, total, page, size);
    }

    public async Task<UserRowDto> UpdateUserAsync(Guid actorId, Guid id, UpdateUserRequest request)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new NotFoundException("ไม่พบผู้ใช้นี้");
        await EnsureMayTouchAsync(actorId, user);

        var first = (request.FirstName ?? "").Trim();
        var last = (request.LastName ?? "").Trim();
        if (first.Length is 0 or > MaxNameLength || last.Length > MaxNameLength)
            throw new RequestValidationException($"กรุณากรอกชื่อ (ไม่เกิน {MaxNameLength} ตัวอักษร)");

        if (request.Email is not null)
        {
            var email = AuthService.NormalizeEmail(request.Email);
            if (email.Length == 0 || !email.Contains('@') || email.Contains(' '))
                throw new RequestValidationException("อีเมลไม่ถูกต้อง");
            if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase)
                && await _db.Users.AnyAsync(u => u.Id != id && u.Email.ToLower() == email))
                throw new RequestValidationException("อีเมลนี้ถูกใช้แล้ว");
            user.Email = email;
        }

        if (request.DepartmentId is { } departmentId
            && !await _db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive))
            throw new RequestValidationException("ไม่พบแผนกที่เลือก หรือแผนกถูกปิดใช้งาน");

        user.FirstName = first;
        user.LastName = last;
        user.DepartmentId = request.DepartmentId;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await RowAsync(id);
    }

    public async Task<UserRowDto> ReactivateUserAsync(Guid actorId, Guid id)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new NotFoundException("ไม่พบผู้ใช้นี้");
        await EnsureMayTouchAsync(actorId, user);
        if (!user.IsActive)
        {
            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return await RowAsync(id);
    }

    public async Task ResetPasswordAsync(Guid actorId, Guid id, ResetPasswordRequest request)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new NotFoundException("ไม่พบผู้ใช้นี้");
        await EnsureMayTouchAsync(actorId, user);
        await _passwords.SetAsync(user, request.NewPassword, PasswordSetKind.AdminSet);
    }

    public async Task<UserRowDto> UnlockUserAsync(Guid actorId, Guid id)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new NotFoundException("ไม่พบผู้ใช้นี้");
        await EnsureMayTouchAsync(actorId, user);
        if (user.LockedUntil != null || user.FailedLoginCount > 0)
        {
            user.LockedUntil = null;
            user.FailedLoginCount = 0;
            user.UpdatedAt = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync();
            await _authLog.WriteAsync(AuthEvents.AccountUnlocked, true, user.Id, user.Email, detail: $"by {actorId}");
        }
        return await RowAsync(id);
    }

    /// <summary>Nobody below SuperAdmin may edit, reactivate or reset a SuperAdmin: that would be a way to take the account over.</summary>
    private async Task EnsureMayTouchAsync(Guid actorId, User target)
    {
        if (target.Role?.Name != RoleNames.SuperAdmin) return;
        var actorRole = await _db.Users.Where(u => u.Id == actorId).Select(u => u.Role.Name).FirstOrDefaultAsync();
        if (actorRole != RoleNames.SuperAdmin)
            throw new ForbiddenException("เฉพาะผู้ดูแลระบบสูงสุดเท่านั้นที่จัดการบัญชีผู้ดูแลระบบสูงสุดได้");
    }

    private Task<UserRowDto> RowAsync(Guid id)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return _db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new UserRowDto(u.Id, u.FirstName, u.LastName, u.Email, u.IsActive, u.Role.Name,
                u.DepartmentId, u.Department != null ? u.Department.Name : null,
                u.LockedUntil != null && u.LockedUntil > now ? u.LockedUntil : null))
            .FirstAsync();
    }
}

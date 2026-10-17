using KPI.Application.Kpis;
using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Enums;
using KPI.Domain.Exceptions;
using KPI.Domain.Interfaces;
using KPI.Domain.Kpis;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class KpiService : IKpiService
{
    private readonly IKpiRepository _kpiRepo;
    private readonly IUserRepository _userRepo;
    private readonly AppDbContext _context;
    private readonly KpiEventPublisher _events;
    private readonly TimeProvider _time;

    /// <summary>Thailand is UTC+7 all year. Monthly progress is bucketed by the month people see on the wall.</summary>
    private static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);

    public KpiService(
        IKpiRepository kpiRepo,
        IUserRepository userRepo,
        AppDbContext context,
        KpiEventPublisher events,
        TimeProvider time)
    {
        _kpiRepo = kpiRepo;
        _userRepo = userRepo;
        _context = context;
        _events = events;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ───────────────────────── Queries ─────────────────────────

    public async Task<List<KpiDto>> GetAllAsync(Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Kpi.View);

        List<Kpi> kpis;
        if (RoleNames.SeesAllDepartments(actor.Role.Name))
            kpis = await _kpiRepo.GetAllAsync();
        else if (actor.DepartmentId is { } departmentId)
            kpis = await _kpiRepo.GetByDepartmentAsync(departmentId);
        else
            kpis = [];

        return kpis.Select(k => ToDto(k, actor)).ToList();
    }

    public async Task<KpiDto> GetByIdAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Kpi.View);
        return ToDto(await GetVisibleKpiAsync(id, actor), actor);
    }

    // ───────────────────────── Commands ─────────────────────────

    public async Task<KpiDto> CreateAsync(CreateKpiRequest request, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Kpi.Create);

        Validate(request.Name, request.Year, request.Target, request.StartDate, request.DueDate);
        await EnsureDepartmentAllowedAsync(actor, request.DepartmentId);

        var kpi = new Kpi
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Year = request.Year,
            Target = request.Target,
            StartDate = AsUtc(request.StartDate),
            DueDate = AsUtc(request.DueDate),
            DepartmentId = request.DepartmentId,
            OwnerId = actor.Id,
            Status = KpiStatus.Draft,
        };

        _context.Kpis.Add(kpi);
        StageAudit(actor, "Create", kpi, null, KpiStatus.Draft.ToString());
        await _context.SaveChangesAsync();

        var created = await _kpiRepo.GetByIdAsync(kpi.Id) ?? kpi;
        await _events.PublishAsync(created, "created", actor.FullName,
            email: KpiEmailKind.Created, actorId: actor.Id);
        return ToDto(created, actor);
    }

    public async Task<KpiDto> UpdateAsync(Guid id, UpdateKpiRequest request, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var kpi = await GetVisibleKpiAsync(id, actor);
        Authorize(actor, kpi, KpiAction.Edit);

        Validate(request.Name, request.Year, request.Target, request.StartDate, request.DueDate);
        if (request.DepartmentId != kpi.DepartmentId)
            await EnsureDepartmentAllowedAsync(actor, request.DepartmentId);

        kpi.Name = request.Name.Trim();
        kpi.Description = request.Description?.Trim() ?? string.Empty;
        kpi.Year = request.Year;
        kpi.Target = request.Target;
        kpi.StartDate = AsUtc(request.StartDate);
        kpi.DueDate = AsUtc(request.DueDate);
        kpi.DepartmentId = request.DepartmentId;

        StageAudit(actor, "Update", kpi, null, kpi.Name);
        await _kpiRepo.UpdateAsync(kpi);

        var updated = await _kpiRepo.GetByIdAsync(kpi.Id) ?? kpi;
        await _events.PublishAsync(updated, "updated", actor.FullName);
        return ToDto(updated, actor);
    }

    public async Task DeleteAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var kpi = await GetVisibleKpiAsync(id, actor);
        Authorize(actor, kpi, KpiAction.Delete);

        StageAudit(actor, "Delete", kpi, kpi.Status.ToString(), null);
        await _kpiRepo.DeleteAsync(kpi);
        await _events.PublishAsync(kpi, "deleted", actor.FullName);
    }

    public async Task<KpiDto> SubmitForApprovalAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var kpi = await GetVisibleKpiAsync(id, actor);
        Authorize(actor, kpi, KpiAction.Submit);

        Validate(kpi.Name, kpi.Year, kpi.Target, kpi.StartDate, kpi.DueDate);

        var old = kpi.Status;
        kpi.Status = KpiStatus.PendingApproval;
        kpi.SubmittedAt = Now;
        kpi.SubmittedById = actor.Id;
        kpi.RejectReason = null;

        StageAudit(actor, "Submit", kpi, old.ToString(), kpi.Status.ToString());
        await _kpiRepo.UpdateAsync(kpi);

        await _events.PublishAsync(kpi, "submitted", actor.FullName,
            new RealtimeNotification("Approval needed",
                $"{actor.FullName} submitted \"{kpi.Name}\" for approval.", "info", kpi.Id),
            new RealtimeAudience([], Approvers: true),
            KpiEmailKind.Submitted, actor.Id);
        return ToDto(kpi, actor);
    }

    public async Task<KpiDto> ApproveAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var kpi = await GetVisibleKpiAsync(id, actor);
        Authorize(actor, kpi, KpiAction.Approve);

        var old = kpi.Status;
        kpi.Status = KpiStatus.Active;
        kpi.ApprovedById = actor.Id;
        kpi.ApprovedAt = Now;
        kpi.RejectReason = null;

        StageAudit(actor, "Approve", kpi, old.ToString(), kpi.Status.ToString());
        await _kpiRepo.UpdateAsync(kpi);

        await _events.PublishAsync(kpi, "approved", actor.FullName,
            new RealtimeNotification("KPI approved",
                $"\"{kpi.Name}\" was approved and is now Active.", "success", kpi.Id),
            new RealtimeAudience([kpi.OwnerId]),
            KpiEmailKind.Approved, actor.Id);
        return ToDto(kpi, actor);
    }

    public async Task<KpiDto> RejectAsync(Guid id, string reason, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var kpi = await GetVisibleKpiAsync(id, actor);
        Authorize(actor, kpi, KpiAction.Reject);

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
            throw new RequestValidationException("A reason is required to reject a KPI.");
        if (reason.Length > 1000)
            throw new RequestValidationException("The reason must be 1000 characters or fewer.");

        var old = kpi.Status;
        kpi.Status = KpiStatus.Draft;
        kpi.RejectReason = reason;
        kpi.SubmittedAt = null;
        kpi.SubmittedById = null;

        StageAudit(actor, "Reject", kpi, old.ToString(), $"{kpi.Status}: {reason}");
        await _kpiRepo.UpdateAsync(kpi);

        await _events.PublishAsync(kpi, "rejected", actor.FullName,
            new RealtimeNotification("KPI returned for changes",
                $"\"{kpi.Name}\" was rejected: {reason}", "warning", kpi.Id),
            new RealtimeAudience([kpi.OwnerId]),
            KpiEmailKind.Rejected, actor.Id);
        return ToDto(kpi, actor);
    }

    public async Task<KpiDto> CloseAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var kpi = await GetVisibleKpiAsync(id, actor);
        Authorize(actor, kpi, KpiAction.Close);

        var old = kpi.Status;
        kpi.Status = KpiStatus.Closed;
        kpi.ClosedAt = Now;
        kpi.ClosedById = actor.Id;

        StageAudit(actor, "Close", kpi, old.ToString(), kpi.Status.ToString());
        await _kpiRepo.UpdateAsync(kpi);

        await _events.PublishAsync(kpi, "closed", actor.FullName);
        return ToDto(kpi, actor);
    }

    public async Task<KpiDto> UpdateProgressAsync(UpdateKpiProgressRequest request, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var kpi = await GetVisibleKpiAsync(request.KpiId, actor);
        Authorize(actor, kpi, KpiAction.UpdateProgress);

        var notes = request.Notes?.Trim() ?? string.Empty;
        if (request.Progress < 0 || request.Progress > kpi.Target)
            throw new RequestValidationException($"Progress must be between 0 and the target ({kpi.Target}).");
        if (notes.Length > 1000)
            throw new RequestValidationException("Notes must be 1000 characters or fewer.");

        var now = _time.GetUtcNow();
        var thaiNow = now.ToOffset(ThaiOffset);
        var oldStatus = kpi.Status;
        var oldProgress = kpi.Progress;
        kpi.Progress = request.Progress;
        kpi.Status = KpiStateMachine.EvaluateHealth(
            kpi.Progress, kpi.Target, kpi.StartDate, kpi.DueDate, now.UtcDateTime);

        _context.KpiProgressLogs.Add(new KpiProgressLog
        {
            KpiId = kpi.Id,
            Progress = request.Progress,
            Notes = notes,
            Month = thaiNow.Month,
            Year = thaiNow.Year,
            UpdatedById = actor.Id,
        });
        StageAudit(actor, "UpdateProgress", kpi,
            $"{oldStatus} {oldProgress}%", $"{kpi.Status} {kpi.Progress}%");

        // One SaveChanges: KPI, progress log and audit row commit (or fail) together.
        await _kpiRepo.UpdateAsync(kpi);

        var (notification, audience) = DescribeStatusChange(kpi, oldStatus);
        await _events.PublishAsync(kpi, "progress", actor.FullName, notification, audience,
            EmailForStatusChange(oldStatus, kpi.Status), actor.Id);
        return ToDto(kpi, actor);
    }

    // ───────────────────────── Rules ─────────────────────────

    /// <summary>Whether <paramref name="actor"/> may run <paramref name="action"/> on the KPI, and if not, why.</summary>
    private static (bool Allowed, bool IsStateProblem, string? Reason) Check(User actor, Kpi kpi, KpiAction action)
    {
        var permission = action switch
        {
            KpiAction.Edit => Permissions.Kpi.Edit,
            KpiAction.Delete => Permissions.Kpi.Delete,
            KpiAction.Submit => Permissions.Kpi.Submit,
            KpiAction.Approve => Permissions.Kpi.Approve,
            KpiAction.Reject => Permissions.Kpi.Reject,
            KpiAction.UpdateProgress => Permissions.Kpi.Progress,
            KpiAction.Close => Permissions.Kpi.Close,
            _ => string.Empty,
        };

        if (!HasPermission(actor, permission))
            return (false, false, "You do not have permission to do this.");

        if (!InScope(actor, kpi))
            return (false, false, "This KPI belongs to another department.");

        if (action == KpiAction.UpdateProgress && actor.Role.Name == RoleNames.Staff && kpi.OwnerId != actor.Id)
            return (false, false, "You can only update progress on your own KPIs.");

        if (action == KpiAction.Approve && kpi.OwnerId == actor.Id)
            return (false, false, "You cannot approve your own KPI.");

        if (!KpiStateMachine.CanApply(kpi.Status, action))
            return (false, true, $"Action '{action}' is not allowed while the KPI is {kpi.Status}.");

        return (true, false, null);
    }

    private static void Authorize(User actor, Kpi kpi, KpiAction action)
    {
        var (allowed, isStateProblem, reason) = Check(actor, kpi, action);
        if (allowed) return;
        if (isStateProblem) throw new InvalidStateException(reason!);
        throw new ForbiddenException(reason!);
    }

    private static bool HasPermission(User actor, string permission) =>
        actor.Role.RolePermissions.Any(rp => rp.Permission.Name == permission);

    private static void RequirePermission(User actor, string permission)
    {
        if (!HasPermission(actor, permission))
            throw new ForbiddenException("You do not have permission to do this.");
    }

    private static bool InScope(User actor, Kpi kpi) =>
        RoleNames.SeesAllDepartments(actor.Role.Name)
        || (actor.DepartmentId != null && actor.DepartmentId == kpi.DepartmentId);

    private async Task EnsureDepartmentAllowedAsync(User actor, Guid departmentId)
    {
        if (!RoleNames.SeesAllDepartments(actor.Role.Name) && actor.DepartmentId != departmentId)
            throw new ForbiddenException("You can only manage KPIs in your own department.");

        var exists = await _context.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive);
        if (!exists)
            throw new RequestValidationException("The selected department does not exist.");
    }

    private static void Validate(string name, int year, decimal target, DateTime start, DateTime due)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new RequestValidationException("Name is required.");
        if (name.Trim().Length > 200)
            throw new RequestValidationException("Name must be 200 characters or fewer.");
        if (year is < 2000 or > 2100)
            throw new RequestValidationException("Year is out of range.");
        if (target <= 0 || target > 100)
            throw new RequestValidationException("Target must be greater than 0 and at most 100.");
        if (due.Date <= start.Date)
            throw new RequestValidationException("Due date must be after the start date.");
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task<User> GetActorAsync(Guid userId)
    {
        var user = await _userRepo.GetByIdWithPermissionsAsync(userId);
        if (user is null || !user.IsActive)
            throw new ForbiddenException("Your account is not active.");
        return user;
    }

    /// <summary>Loads a KPI the actor may see; anything else is reported as not found.</summary>
    private async Task<Kpi> GetVisibleKpiAsync(Guid id, User actor)
    {
        var kpi = await _kpiRepo.GetByIdAsync(id);
        if (kpi is null || !InScope(actor, kpi))
            throw new NotFoundException("KPI not found.");
        return kpi;
    }

    private void StageAudit(User actor, string action, Kpi kpi, string? oldValues, string? newValues) =>
        _context.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = "Kpi",
            EntityId = kpi.Id.ToString(),
            OldValues = oldValues,
            NewValues = newValues,
            UserId = actor.Id,
            UserEmail = actor.Email,
        });

    private static (RealtimeNotification?, RealtimeAudience?) DescribeStatusChange(Kpi kpi, KpiStatus oldStatus)
    {
        if (kpi.Status == oldStatus) return (null, null);

        return kpi.Status switch
        {
            KpiStatus.AtRisk or KpiStatus.Delayed => (
                new RealtimeNotification($"KPI {kpi.Status}",
                    $"\"{kpi.Name}\" is now {kpi.Status}.", "warning", kpi.Id),
                new RealtimeAudience([kpi.OwnerId], ManagersOfDepartment: kpi.DepartmentId)),
            KpiStatus.Completed => (
                new RealtimeNotification("KPI completed",
                    $"\"{kpi.Name}\" reached its target.", "success", kpi.Id),
                new RealtimeAudience([kpi.OwnerId], Approvers: true, ManagersOfDepartment: kpi.DepartmentId)),
            _ => (null, null),
        };
    }

    /// <summary>Only a move into AtRisk, Delayed or Completed is worth an e-mail.</summary>
    public static KpiEmailKind? EmailForStatusChange(KpiStatus oldStatus, KpiStatus newStatus) =>
        oldStatus == newStatus ? null : newStatus switch
        {
            KpiStatus.AtRisk => KpiEmailKind.AtRisk,
            KpiStatus.Delayed => KpiEmailKind.Delayed,
            KpiStatus.Completed => KpiEmailKind.Completed,
            _ => null,
        };

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static KpiDto ToDto(Kpi kpi, User actor)
    {
        var allowed = new List<string>();
        void Add(KpiAction action, string name)
        {
            if (Check(actor, kpi, action).Allowed) allowed.Add(name);
        }
        Add(KpiAction.Edit, KpiActionNames.Edit);
        Add(KpiAction.Delete, KpiActionNames.Delete);
        Add(KpiAction.Submit, KpiActionNames.Submit);
        Add(KpiAction.Approve, KpiActionNames.Approve);
        Add(KpiAction.Reject, KpiActionNames.Reject);
        Add(KpiAction.UpdateProgress, KpiActionNames.UpdateProgress);
        Add(KpiAction.Close, KpiActionNames.Close);

        return new KpiDto(
            Id: kpi.Id,
            Name: kpi.Name,
            Description: kpi.Description,
            Year: kpi.Year,
            Target: kpi.Target,
            Progress: kpi.Progress,
            Status: kpi.Status,
            StatusLabel: kpi.Status.ToString(),
            TrafficLight: KpiStateMachine.TrafficLight(kpi.Status),
            StartDate: kpi.StartDate,
            DueDate: kpi.DueDate,
            DepartmentId: kpi.DepartmentId,
            DepartmentName: kpi.Department?.Name ?? "",
            OwnerId: kpi.OwnerId,
            OwnerName: kpi.Owner?.FullName ?? "",
            RejectReason: kpi.RejectReason,
            SubmittedAt: kpi.SubmittedAt,
            ApprovedAt: kpi.ApprovedAt,
            ClosedAt: kpi.ClosedAt,
            AllowedActions: allowed);
    }
}

using System.Text.RegularExpressions;
using KPI.Application.Common;
using KPI.Application.Issues;
using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Interfaces;
using KPI.Domain.Issues;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class IssueService : IIssueService
{
    public const int MaxAttachmentsPerIssue = 5;
    public const int MaxAttachmentBytes = 5 * 1024 * 1024;

    private static readonly Regex NumberPattern = new(@"(\d{4})\D+(\d{1,6})", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IUserRepository _users;
    private readonly IssueEventPublisher _events;
    private readonly IssueSlaPolicy _sla;
    private readonly TimeProvider _time;

    public IssueService(
        AppDbContext db,
        IUserRepository users,
        IssueEventPublisher events,
        IssueSlaPolicy sla,
        TimeProvider time)
    {
        _db = db;
        _users = users;
        _events = events;
        _sla = sla;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ───────────────────────── Creating ─────────────────────────

    public async Task<IssueDetailDto> CreateAsync(
        CreateIssueRequest request, IReadOnlyList<IssueUpload> files, Guid userId, string? ip)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Issue.Create);

        ValidateReport(request.Title, request.Description, request.Category, request.Module, request.Impact);
        var uploads = ValidateUploads(files, 0);

        var now = Now;
        var issue = NewIssue(request.Title, request.Description, request.Category, request.Module, request.Impact, now);
        issue.ReporterUserId = actor.Id;
        issue.ReporterName = actor.FullName;
        issue.ReporterEmail = actor.Email;
        issue.DepartmentId = actor.DepartmentId;
        issue.ReporterIp = ip.Truncate(64);
        issue.PageUrl = request.PageUrl.Truncate(500);
        issue.UserAgent = request.UserAgent.Truncate(400);
        issue.ClientInfo = request.ClientInfo.Truncate(1000);
        issue.ErrorRequestId = request.ErrorRequestId.Truncate(100);

        await SaveNewAsync(issue, uploads, actor);

        await _events.PublishAsync(issue, "created", actor.FullName, actor.Id,
            Toast(issue, "แจ้งปัญหาใหม่", issue.Title), ToIt,
            IssueEmailKind.NewForIt);
        await _events.PublishAsync(issue, "received", null, actor.Id, email: IssueEmailKind.Received);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<PublicIssueReceipt> CreatePublicAsync(
        PublicIssueRequest request, IReadOnlyList<IssueUpload> files, string? ip)
    {
        var name = request.ReporterName?.Trim() ?? string.Empty;
        var email = request.ReporterEmail?.Trim();
        var phone = request.ReporterPhone?.Trim();

        if (name.Length is < 2 or > 100) throw new RequestValidationException("กรุณากรอกชื่อ (2–100 ตัวอักษร)");
        if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(phone))
            throw new RequestValidationException("กรุณากรอกอีเมลหรือเบอร์โทรอย่างน้อยหนึ่งช่องทาง เพื่อให้ติดต่อกลับได้");
        if (!string.IsNullOrEmpty(email) && (email.Length > 254 || !email.Contains('@') || email.Contains(' ')))
            throw new RequestValidationException("รูปแบบอีเมลไม่ถูกต้อง");
        if (phone is { Length: > 40 }) throw new RequestValidationException("เบอร์โทรยาวเกินไป");

        ValidateReport(request.Title, request.Description, request.Category, request.Module, request.Impact);
        var uploads = ValidateUploads(files, 0);

        Department? department = null;
        if (request.DepartmentId is { } departmentId)
        {
            department = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId && d.IsActive)
                ?? throw new RequestValidationException("ไม่พบแผนกที่เลือก");
        }

        var now = Now;
        var issue = NewIssue(request.Title, request.Description, request.Category, request.Module, request.Impact, now);
        issue.IsPublicSubmission = true;
        issue.ReporterName = name;
        issue.ReporterEmail = string.IsNullOrEmpty(email) ? null : email;
        issue.ReporterPhone = string.IsNullOrEmpty(phone) ? null : phone;
        issue.DepartmentId = department?.Id;
        issue.ReporterIp = ip.Truncate(64);
        issue.UserAgent = request.UserAgent.Truncate(400);
        issue.ClientInfo = request.ClientInfo.Truncate(1000);

        await SaveNewAsync(issue, uploads, actor: null);

        await _events.PublishAsync(issue, "created", name, null,
            Toast(issue, "แจ้งปัญหาใหม่ (ผู้ใช้ที่ยังไม่ได้เข้าสู่ระบบ)", issue.Title), ToIt,
            IssueEmailKind.NewForIt);
        await _events.PublishAsync(issue, "received", null, null, email: IssueEmailKind.Received);

        return new PublicIssueReceipt(issue.DisplayNumber, issue.CreatedAt, issue.ResponseDueAt);
    }

    private Issue NewIssue(string title, string description, IssueCategory category, IssueModule module,
        IssueImpact impact, DateTime now)
    {
        var priority = IssueSlaPolicy.InitialPriority(impact);
        return new Issue
        {
            Title = title.Trim(),
            Description = description.Trim(),
            Category = category,
            Module = module,
            Impact = impact,
            Priority = priority,
            Status = IssueStatus.New,
            CreatedAt = now,
            UpdatedAt = now,
            NumberYear = now.Year,
            ResponseDueAt = _sla.ResponseDue(now),
            ResolveDueAt = _sla.ResolveDue(now, priority),
        };
    }

    /// <summary>Allocates the next number for the year; retries if two reports race for the same one.</summary>
    private async Task SaveNewAsync(Issue issue, List<IssueUpload> uploads, User? actor)
    {
        for (var attempt = 1; ; attempt++)
        {
            issue.Number = (await _db.Issues
                .Where(i => i.NumberYear == issue.NumberYear)
                .MaxAsync(i => (int?)i.Number) ?? 0) + 1;

            _db.Issues.Add(issue);
            foreach (var upload in uploads)
                issue.Attachments.Add(ToAttachment(upload, actor?.Id));
            StageAudit(actor, issue, "Create", null, IssueStatus.New.ToString(),
                actor is null ? issue.ReporterEmail ?? issue.ReporterPhone : null, actor is null ? issue.ReporterName : null);

            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < 4)
            {
                // Number already taken by a concurrent report: forget this attempt and pick the next one.
                // (The attachments are added again at the top of the loop, so only clear them here.)
                foreach (var entry in _db.ChangeTracker.Entries()
                             .Where(e => e.Entity is Issue or IssueAttachment or AuditLog).ToList())
                    entry.State = EntityState.Detached;
                issue.Attachments.Clear();
            }
        }
    }

    // ───────────────────────── Reading ─────────────────────────

    public async Task<PagedResult<IssueListItemDto>> ListAsync(IssueQuery query, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Issue.View);

        var page = PagedResult<IssueListItemDto>.ClampPage(query.Page);
        var size = PagedResult<IssueListItemDto>.ClampSize(query.PageSize);
        var now = Now;

        var rows = Scope(_db.Issues.AsNoTracking(), actor);

        if (query.Status is { } status) rows = rows.Where(i => i.Status == status);
        if (query.OpenOnly == true) rows = rows.Where(IssueFilters.Open);
        if (query.OpenOnly == false) rows = rows.Where(IssueFilters.Finished);
        if (query.Priority is { } priority) rows = rows.Where(i => i.Priority == priority);
        if (query.Module is { } module) rows = rows.Where(i => i.Module == module);
        if (query.DepartmentId is { } departmentId) rows = rows.Where(i => i.DepartmentId == departmentId);
        if (query.AssigneeId is { } assigneeId) rows = rows.Where(i => i.AssigneeId == assigneeId);
        if (query.Mine == true) rows = rows.Where(i => i.ReporterUserId == actor.Id || i.AssigneeId == actor.Id);
        if (query.From is { } from) rows = rows.Where(i => i.CreatedAt >= from);
        if (query.To is { } to) rows = rows.Where(i => i.CreatedAt < to);
        if (query.Overdue == true) rows = rows.Where(IssueFilters.Overdue(now));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            var match = NumberPattern.Match(term);
            if (match.Success)
            {
                var year = int.Parse(match.Groups[1].Value) - 543;
                var number = int.Parse(match.Groups[2].Value);
                rows = rows.Where(i => (i.NumberYear == year && i.Number == number)
                                       || i.Title.ToLower().Contains(term));
            }
            else
            {
                rows = rows.Where(i => i.Title.ToLower().Contains(term)
                                       || i.Description.ToLower().Contains(term)
                                       || i.ReporterName.ToLower().Contains(term));
            }
        }

        var ordered = query.Sort switch
        {
            "oldest" => rows.OrderBy(i => i.CreatedAt),
            "due" => rows.OrderBy(i => i.ResolveDueAt),
            "priority" => rows.OrderByDescending(i => i.Priority).ThenBy(i => i.CreatedAt),
            "updated" => rows.OrderByDescending(i => i.UpdatedAt),
            _ => rows.OrderByDescending(i => i.CreatedAt),
        };

        var total = await ordered.CountAsync();
        var pageRows = await ordered
            .Skip((page - 1) * size)
            .Take(size)
            .Select(i => new ListRow(
                i.Id, i.NumberYear, i.Number, i.Title, i.Category, i.Module, i.Impact, i.Priority, i.Status,
                i.ReporterName,
                i.DepartmentId, i.Department != null ? i.Department.Name : null,
                i.AssigneeId,
                i.Assignee != null ? i.Assignee.FirstName : null,
                i.Assignee != null ? i.Assignee.LastName : null,
                i.CreatedAt, i.UpdatedAt, i.ResponseDueAt, i.ResolveDueAt, i.FirstResponseAt,
                i.Comments.Count, i.Attachments.Count, i.IsPublicSubmission))
            .ToListAsync();

        return new PagedResult<IssueListItemDto>(pageRows.Select(r => r.ToDto(now)).ToList(), total, page, size);
    }

    /// <summary>What the list query reads; the number, name, tone and overdue flags are worked out in memory afterwards.</summary>
    private sealed record ListRow(
        Guid Id, int NumberYear, int Number, string Title, IssueCategory Category, IssueModule Module,
        IssueImpact Impact, IssuePriority Priority, IssueStatus Status, string ReporterName,
        Guid? DepartmentId, string? DepartmentName, Guid? AssigneeId, string? AssigneeFirstName, string? AssigneeLastName,
        DateTime CreatedAt, DateTime UpdatedAt, DateTime ResponseDueAt, DateTime ResolveDueAt, DateTime? FirstResponseAt,
        int CommentCount, int AttachmentCount, bool IsPublicSubmission)
    {
        public IssueListItemDto ToDto(DateTime now) => new(
            Id, Issue.FormatNumber(NumberYear, Number), Title, Category, Module, Impact, Priority, Status,
            IssueStateMachine.Tone(Status), ReporterName, DepartmentId, DepartmentName,
            AssigneeId, AssigneeFirstName is null ? null : $"{AssigneeFirstName} {AssigneeLastName}".Trim(),
            CreatedAt, UpdatedAt, ResponseDueAt, ResolveDueAt,
            IssueStateMachine.IsResponseOverdue(Status, FirstResponseAt, ResponseDueAt, now),
            IssueStateMachine.IsResolveOverdue(Status, ResolveDueAt, now),
            CommentCount, AttachmentCount, IsPublicSubmission);
    }

    public async Task<IssueSummaryDto> SummaryAsync(Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Issue.View);
        var now = Now;
        var weekAgo = now.AddDays(-7);

        var rows = Scope(_db.Issues.AsNoTracking(), actor);

        var byStatus = await rows.GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        int Count(params IssueStatus[] statuses) => byStatus.Where(s => statuses.Contains(s.Status)).Sum(s => s.Count);

        var overdue = await rows.CountAsync(IssueFilters.Overdue(now));
        var resolved = await rows.CountAsync(i => i.ResolvedAt != null && i.ResolvedAt >= weekAgo);
        var actorId = actor.Id;
        var mine = await rows.Where(IssueFilters.Open)
            .CountAsync(i => i.ReporterUserId == actorId || i.AssigneeId == actorId);

        var open = byStatus.Where(s => IssueStateMachine.IsOpen(s.Status)).Sum(s => s.Count);

        return new IssueSummaryDto(
            open,
            Count(IssueStatus.New),
            Count(IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.Reopened),
            Count(IssueStatus.WaitingForReporter),
            overdue, resolved, mine);
    }

    public async Task<IssueDetailDto> GetAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Issue.View);
        return await DetailAsync(id, actor);
    }

    // ───────────────────────── Commands ─────────────────────────

    public async Task<IssueDetailDto> CommentAsync(Guid id, string body, bool internalNote, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);

        body = RequireText(body, 1, 4000, "กรุณาพิมพ์ข้อความ", "ข้อความยาวเกินไป (สูงสุด 4,000 ตัวอักษร)");

        var staff = IsStaff(actor);
        if (internalNote && !staff) throw new ForbiddenException("เฉพาะทีม IT เท่านั้นที่เขียนบันทึกภายในได้");

        var now = Now;
        AddComment(issue, actor, body, now, staff, internalNote);

        var isReporter = issue.ReporterUserId == actor.Id;
        string audit = internalNote ? "InternalNote" : staff ? "StaffReply" : "ReporterReply";
        string? from = null, to = null;

        if (staff && !internalNote) MarkFirstResponse(issue, now);

        // The reporter answering a question puts the work back on IT's desk.
        var resumed = isReporter && issue.Status == IssueStatus.WaitingForReporter;
        if (resumed)
        {
            ResumeFromWaiting(issue, now);
            from = IssueStatus.WaitingForReporter.ToString();
            to = IssueStatus.InProgress.ToString();
            issue.Status = IssueStatus.InProgress;
        }

        StageAudit(actor, issue, audit, from, to ?? body.Truncate(200));
        await Touch(issue, now);

        if (!internalNote)
        {
            if (staff)
                await _events.PublishAsync(issue, "comment", actor.FullName, actor.Id,
                    Toast(issue, "IT ตอบกลับแล้ว", body.Truncate(80)), ToReporter(issue),
                    IssueEmailKind.StaffReplied, body);
            else
                await _events.PublishAsync(issue, "comment", actor.FullName, actor.Id,
                    Toast(issue, "ผู้แจ้งตอบกลับ", body.Truncate(80)), ToAssigneeOrIt(issue),
                    IssueEmailKind.ReporterReplied, body);
        }
        else
        {
            await _events.PublishAsync(issue, "note", actor.FullName, actor.Id);
        }

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> AssignAsync(Guid id, Guid assigneeId, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        Authorize(actor, issue, IssueAction.Assign);

        var assignee = await _users.GetByIdWithPermissionsAsync(assigneeId);
        if (assignee is null || !assignee.IsActive || !HasPermission(assignee, Permissions.Issue.Manage))
            throw new RequestValidationException("ผู้รับผิดชอบต้องเป็นสมาชิกทีม IT ที่ยังใช้งานอยู่");

        var now = Now;
        var oldStatus = issue.Status;
        var oldAssignee = issue.Assignee is null ? null : issue.Assignee.FullName;
        issue.AssigneeId = assignee.Id;
        issue.Assignee = assignee;
        issue.Status = IssueStateMachine.Next(issue.Status, IssueAction.Assign);
        MarkFirstResponse(issue, now);

        StageAudit(actor, issue, "Assign", oldAssignee ?? oldStatus.ToString(), assignee.FullName);
        await Touch(issue, now);

        var toAssignee = assignee.Id == actor.Id ? null : new RealtimeAudience([assignee.Id]);
        await _events.PublishAsync(issue, "assigned", actor.FullName, actor.Id,
            toAssignee is null ? null : Toast(issue, "มีงานมอบหมายให้คุณ", issue.Title),
            toAssignee,
            toAssignee is null ? null : IssueEmailKind.Assigned);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> StartAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        Authorize(actor, issue, IssueAction.Start);

        var now = Now;
        var old = issue.Status;
        if (issue.AssigneeId is null)
        {
            issue.AssigneeId = actor.Id;
            issue.Assignee = actor;
        }
        issue.Status = IssueStateMachine.Next(issue.Status, IssueAction.Start);
        MarkFirstResponse(issue, now);

        StageAudit(actor, issue, "Start", old.ToString(), issue.Status.ToString());
        await Touch(issue, now);
        await _events.PublishAsync(issue, "started", actor.FullName, actor.Id,
            Toast(issue, "กำลังแก้ไขปัญหา", $"{actor.FullName} เริ่มดำเนินการแล้ว"), ToReporter(issue));

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> RequestInfoAsync(Guid id, string message, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        Authorize(actor, issue, IssueAction.RequestInfo);

        message = RequireText(message, 1, 4000, "กรุณาระบุว่าต้องการข้อมูลอะไรเพิ่ม", "ข้อความยาวเกินไป");

        var now = Now;
        var old = issue.Status;
        issue.Status = IssueStatus.WaitingForReporter;
        issue.WaitingSince = now; // the resolve clock stops until the reporter answers
        MarkFirstResponse(issue, now);
        AddComment(issue, actor, message, now, staff: true);

        StageAudit(actor, issue, "RequestInfo", old.ToString(), issue.Status.ToString());
        await Touch(issue, now);
        await _events.PublishAsync(issue, "needsInfo", actor.FullName, actor.Id,
            Toast(issue, "IT ขอข้อมูลเพิ่ม", message.Truncate(80), "warning"), ToReporter(issue),
            IssueEmailKind.NeedsInfo, message);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> ResolveAsync(Guid id, ResolveIssueRequest request, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        Authorize(actor, issue, IssueAction.Resolve);

        var resolution = RequireText(request.Resolution, 5, 4000,
            "กรุณาอธิบายวิธีแก้ไข (อย่างน้อย 5 ตัวอักษร)", "วิธีแก้ไขยาวเกินไป (สูงสุด 4,000 ตัวอักษร)");
        if (request.RootCause == IssueRootCause.None || !Enum.IsDefined(request.RootCause))
            throw new RequestValidationException("กรุณาเลือกสาเหตุของปัญหา");
        if (request.Prevention is { Length: > 2000 }) throw new RequestValidationException("วิธีป้องกันยาวเกินไป");
        if (request.FixReference is { Length: > 500 }) throw new RequestValidationException("ลิงก์อ้างอิงยาวเกินไป");
        if (request.TimeSpentMinutes is < 0 or > 100_000) throw new RequestValidationException("เวลาที่ใช้ไม่ถูกต้อง");

        var now = Now;
        var old = issue.Status;
        issue.Status = IssueStatus.Resolved;
        issue.ResolvedAt = now;
        issue.WaitingSince = null;
        issue.Resolution = resolution;
        issue.RootCause = request.RootCause;
        issue.Prevention = request.Prevention?.Trim();
        issue.FixReference = request.FixReference?.Trim();
        issue.TimeSpentMinutes = request.TimeSpentMinutes;
        if (issue.AssigneeId is null) { issue.AssigneeId = actor.Id; issue.Assignee = actor; }
        MarkFirstResponse(issue, now);

        StageAudit(actor, issue, "Resolve", old.ToString(), $"{issue.Status}: {resolution.Truncate(200)}");
        await Touch(issue, now);
        await _events.PublishAsync(issue, "resolved", actor.FullName, actor.Id,
            Toast(issue, "แก้ไขปัญหาแล้ว", "กรุณายืนยันว่าใช้งานได้ปกติ", "success"), ToReporter(issue),
            IssueEmailKind.Resolved, resolution);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> ConfirmAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        Authorize(actor, issue, IssueAction.Confirm);

        var now = Now;
        var old = issue.Status;
        issue.Status = IssueStatus.Closed;
        issue.ClosedAt = now;

        StageAudit(actor, issue, "Confirm", old.ToString(), issue.Status.ToString());
        await Touch(issue, now);
        await _events.PublishAsync(issue, "closed", actor.FullName, actor.Id);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> ReopenAsync(Guid id, string reason, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        Authorize(actor, issue, IssueAction.Reopen);

        reason = RequireText(reason, 1, 4000, "กรุณาบอกว่าทำไมปัญหายังไม่หาย", "ข้อความยาวเกินไป");

        var now = Now;
        var old = issue.Status;
        issue.Status = IssueStatus.Reopened;
        issue.ReopenCount++;
        issue.ResolvedAt = null;
        issue.ClosedAt = null;
        issue.RejectReason = null;
        issue.ResolveDueAt = now + _sla.ResolveFor(issue.Priority); // a fresh target for the second attempt
        AddComment(issue, actor, reason, now, IsStaff(actor));

        StageAudit(actor, issue, "Reopen", old.ToString(), $"{issue.Status}: {reason.Truncate(200)}");
        await Touch(issue, now);
        await _events.PublishAsync(issue, "reopened", actor.FullName, actor.Id,
            Toast(issue, "เปิดเรื่องใหม่", reason.Truncate(80), "warning"), ToAssigneeOrIt(issue),
            IssueEmailKind.Reopened, reason);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> RejectAsync(Guid id, string reason, Guid? duplicateOfId, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        Authorize(actor, issue, IssueAction.Reject);

        reason = RequireText(reason, 1, 1000, "กรุณาระบุเหตุผล", "เหตุผลยาวเกินไป (สูงสุด 1,000 ตัวอักษร)");

        if (duplicateOfId is { } duplicate)
        {
            if (duplicate == issue.Id) throw new RequestValidationException("เรื่องซ้ำต้องเป็นคนละเรื่องกัน");
            if (!await _db.Issues.AnyAsync(i => i.Id == duplicate))
                throw new RequestValidationException("ไม่พบเรื่องที่ซ้ำ");
        }

        var now = Now;
        var old = issue.Status;
        issue.Status = IssueStatus.Rejected;
        issue.RejectReason = reason;
        issue.DuplicateOfId = duplicateOfId;
        issue.ClosedAt = now;
        issue.WaitingSince = null;
        MarkFirstResponse(issue, now);

        StageAudit(actor, issue, "Reject", old.ToString(), $"{issue.Status}: {reason.Truncate(200)}");
        await Touch(issue, now);
        await _events.PublishAsync(issue, "rejected", actor.FullName, actor.Id,
            Toast(issue, "ไม่ดำเนินการต่อ", reason.Truncate(80), "warning"), ToReporter(issue),
            IssueEmailKind.Rejected, reason);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueDetailDto> SetPriorityAsync(Guid id, IssuePriority priority, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        if (!IsStaff(actor)) throw new ForbiddenException("เฉพาะทีม IT เท่านั้นที่เปลี่ยนความสำคัญได้");
        if (!IssueStateMachine.IsOpen(issue.Status)) throw new InvalidStateException("เรื่องที่จบแล้วเปลี่ยนความสำคัญไม่ได้");
        if (!Enum.IsDefined(priority)) throw new RequestValidationException("ความสำคัญไม่ถูกต้อง");

        var now = Now;
        var old = issue.Priority;
        if (old == priority) return await DetailAsync(issue.Id, actor);

        // The target counts from when the issue was reported (plus any time spent waiting on the reporter).
        issue.Priority = priority;
        issue.ResolveDueAt = _sla.ResolveDue(issue.CreatedAt, priority);

        StageAudit(actor, issue, "SetPriority", old.ToString(), priority.ToString());
        await Touch(issue, now);
        await _events.PublishAsync(issue, "priority", actor.FullName, actor.Id);

        return await DetailAsync(issue.Id, actor);
    }

    // ───────────────────────── Attachments ─────────────────────────

    public async Task<IssueDetailDto> AddAttachmentsAsync(Guid id, IReadOnlyList<IssueUpload> files, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        var issue = await LoadAsync(id, actor);
        if (issue.Status == IssueStatus.Closed) throw new InvalidStateException("เรื่องที่ปิดแล้วแนบไฟล์เพิ่มไม่ได้");

        var existing = await _db.IssueAttachments.CountAsync(a => a.IssueId == id);
        var uploads = ValidateUploads(files, existing);
        if (uploads.Count == 0) throw new RequestValidationException("ไม่พบไฟล์ที่แนบ");

        foreach (var upload in uploads)
            _db.IssueAttachments.Add(ToAttachment(upload, actor.Id, issue.Id));

        StageAudit(actor, issue, "Attach", null, $"{uploads.Count} file(s)");
        await Touch(issue, Now);
        await _events.PublishAsync(issue, "attachment", actor.FullName, actor.Id);

        return await DetailAsync(issue.Id, actor);
    }

    public async Task<IssueAttachmentContent> GetAttachmentAsync(Guid id, Guid attachmentId, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Issue.View);

        var issue = await Scope(_db.Issues.AsNoTracking(), actor).FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new NotFoundException("ไม่พบเรื่องที่แจ้ง");
        var file = await _db.IssueAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.IssueId == issue.Id)
            ?? throw new NotFoundException("ไม่พบไฟล์แนบ");

        return new IssueAttachmentContent(file.FileName, file.ContentType, file.Data);
    }

    // ───────────────────────── People and upkeep ─────────────────────────

    public async Task<List<AssigneeDto>> AssigneesAsync(Guid userId)
    {
        var actor = await GetActorAsync(userId);
        if (!IsStaff(actor)) throw new ForbiddenException("เฉพาะทีม IT เท่านั้น");

        var staff = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role.RolePermissions.Any(rp => rp.Permission.Name == Permissions.Issue.Manage))
            .Select(u => new { u.Id, u.FirstName, u.LastName, Role = u.Role.Name })
            .ToListAsync();

        var staffIds = staff.Select(s => s.Id).ToList();
        var open = await _db.Issues.AsNoTracking()
            .Where(IssueFilters.Open)
            .Where(i => i.AssigneeId != null && staffIds.Contains(i.AssigneeId.Value))
            .GroupBy(i => i.AssigneeId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count);

        return staff
            .Select(s => new AssigneeDto(s.Id, $"{s.FirstName} {s.LastName}".Trim(), s.Role, open.GetValueOrDefault(s.Id)))
            .OrderBy(a => a.OpenIssues).ThenBy(a => a.Name)
            .ToList();
    }

    public async Task<int> AutoCloseResolvedAsync(DateTime now)
    {
        var cutoff = now - IssueStateMachine.AutoCloseAfter;
        var stale = await _db.Issues
            .Where(i => i.Status == IssueStatus.Resolved && i.ResolvedAt != null && i.ResolvedAt <= cutoff)
            .ToListAsync();

        foreach (var issue in stale)
        {
            issue.Status = IssueStatus.Closed;
            issue.ClosedAt = now;
            issue.UpdatedAt = now;
            issue.Version++;
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "AutoClose", EntityName = "Issue", EntityId = issue.Id.ToString(),
                OldValues = IssueStatus.Resolved.ToString(), NewValues = IssueStatus.Closed.ToString(),
                UserEmail = "system", UserName = "ระบบ", CreatedAt = now,
            });
        }

        if (stale.Count > 0) await _db.SaveChangesAsync();
        foreach (var issue in stale)
            await _events.PublishAsync(issue, "closed", "ระบบ", null);
        return stale.Count;
    }

    // ───────────────────────── Rules ─────────────────────────

    /// <summary>Whether the actor may run the action now, ignoring who they are relative to the issue.</summary>
    private static bool Permits(User actor, Issue issue, IssueAction action, DateTime now)
    {
        if (!IssueStateMachine.CanApply(issue.Status, action)) return false;

        var staff = IsStaff(actor);
        var reporter = issue.ReporterUserId == actor.Id;

        return action switch
        {
            IssueAction.Assign or IssueAction.Start or IssueAction.RequestInfo
                or IssueAction.Resolve or IssueAction.Reject => staff,
            IssueAction.Confirm => staff || reporter,
            IssueAction.Reopen => staff || (reporter && issue.Status switch
            {
                IssueStatus.Resolved => true,
                IssueStatus.Closed => issue.ClosedAt is { } closed && now - closed <= IssueStateMachine.ReporterReopenWindow,
                _ => false,
            }),
            _ => false,
        };
    }

    private void Authorize(User actor, Issue issue, IssueAction action)
    {
        var now = Now;
        if (Permits(actor, issue, action, now)) return;

        // Say whether it is the status or the person that is the problem.
        var allowedByStatus = IssueStateMachine.CanApply(issue.Status, action);
        if (!allowedByStatus)
            throw new InvalidStateException($"ทำรายการนี้ไม่ได้ในสถานะปัจจุบัน ({issue.Status})");
        throw new ForbiddenException("คุณไม่มีสิทธิ์ทำรายการนี้");
    }

    private static List<string> AllowedActions(User actor, Issue issue, DateTime now)
    {
        var allowed = new List<string>();
        void Add(IssueAction action, string name)
        {
            if (Permits(actor, issue, action, now)) allowed.Add(name);
        }
        Add(IssueAction.Assign, IssueActionNames.Assign);
        Add(IssueAction.Start, IssueActionNames.Start);
        Add(IssueAction.RequestInfo, IssueActionNames.RequestInfo);
        Add(IssueAction.Resolve, IssueActionNames.Resolve);
        Add(IssueAction.Confirm, IssueActionNames.Confirm);
        Add(IssueAction.Reopen, IssueActionNames.Reopen);
        Add(IssueAction.Reject, IssueActionNames.Reject);

        var staff = IsStaff(actor);
        if (staff && IssueStateMachine.IsOpen(issue.Status)) allowed.Add(IssueActionNames.SetPriority);
        allowed.Add(IssueActionNames.Comment);
        if (staff) allowed.Add(IssueActionNames.InternalNote);
        if (issue.Status != IssueStatus.Closed) allowed.Add(IssueActionNames.Attach);
        return allowed;
    }

    private static bool IsStaff(User actor) => HasPermission(actor, Permissions.Issue.Manage);

    private static bool HasPermission(User user, string permission) =>
        user.Role.RolePermissions.Any(rp => rp.Permission.Name == permission);

    private static void RequirePermission(User actor, string permission)
    {
        if (!HasPermission(actor, permission))
            throw new ForbiddenException("คุณไม่มีสิทธิ์ทำรายการนี้");
    }

    /// <summary>Everyone sees their own; managers also their department's; IT, directors and admins see all.</summary>
    private static IQueryable<Issue> Scope(IQueryable<Issue> issues, User actor)
    {
        var role = actor.Role.Name;
        if (RoleNames.SeesAllIssues(role)) return issues;

        var userId = actor.Id;
        if (role == RoleNames.Manager && actor.DepartmentId is { } departmentId)
            return issues.Where(i => i.ReporterUserId == userId || i.DepartmentId == departmentId);

        return issues.Where(i => i.ReporterUserId == userId);
    }

    private static void ValidateReport(string title, string description, IssueCategory category,
        IssueModule module, IssueImpact impact)
    {
        title = title?.Trim() ?? string.Empty;
        description = description?.Trim() ?? string.Empty;

        if (title.Length is < 3 or > 200) throw new RequestValidationException("หัวข้อต้องยาว 3–200 ตัวอักษร");
        if (description.Length is < 10 or > 4000)
            throw new RequestValidationException("กรุณาอธิบายปัญหาอย่างน้อย 10 ตัวอักษร (สูงสุด 4,000)");
        if (!Enum.IsDefined(category) || !Enum.IsDefined(module) || !Enum.IsDefined(impact))
            throw new RequestValidationException("ข้อมูลที่เลือกไม่ถูกต้อง");
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task<User> GetActorAsync(Guid userId)
    {
        var user = await _users.GetByIdWithPermissionsAsync(userId);
        if (user is null || !user.IsActive) throw new ForbiddenException("บัญชีของคุณไม่สามารถใช้งานได้");
        return user;
    }

    /// <summary>Loads a tracked issue the actor may see; anything else is reported as not found.</summary>
    private async Task<Issue> LoadAsync(Guid id, User actor)
    {
        RequirePermission(actor, Permissions.Issue.View);
        return await Scope(_db.Issues, actor)
                   .Include(i => i.Assignee)
                   .FirstOrDefaultAsync(i => i.Id == id)
               ?? throw new NotFoundException("ไม่พบเรื่องที่แจ้ง");
    }

    private async Task<IssueDetailDto> DetailAsync(Guid id, User actor)
    {
        var now = Now;
        var issue = await Scope(_db.Issues.AsNoTracking(), actor)
                        .Include(i => i.Assignee)
                        .Include(i => i.Department)
                        .Include(i => i.Comments)
                        .FirstOrDefaultAsync(i => i.Id == id)
                    ?? throw new NotFoundException("ไม่พบเรื่องที่แจ้ง");

        var staff = IsStaff(actor);

        var comments = issue.Comments
            .Where(c => staff || !c.IsInternal)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new IssueCommentDto(c.Id, c.AuthorName, c.IsStaff, c.IsInternal, c.Body, Utc(c.CreatedAt)))
            .ToList();

        var attachments = await _db.IssueAttachments.AsNoTracking()
            .Where(a => a.IssueId == id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new IssueAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt))
            .ToListAsync();

        var entityId = id.ToString();
        var events = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Issue" && a.EntityId == entityId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        var timeline = events
            .Where(e => staff || e.Action != "InternalNote")
            .Select(e => new IssueEventDto(e.Action, e.OldValues, e.NewValues,
                e.UserName ?? e.UserEmail, e.UserRole, Utc(e.CreatedAt)))
            .ToList();

        return new IssueDetailDto(
            issue.Id, issue.DisplayNumber, issue.Title, issue.Description,
            issue.Category, issue.Module, issue.Impact, issue.Priority, issue.Status,
            IssueStateMachine.Tone(issue.Status),
            issue.ReporterUserId, issue.ReporterName,
            issue.ReporterEmail, issue.ReporterPhone, issue.IsPublicSubmission,
            issue.DepartmentId, issue.Department?.Name,
            issue.AssigneeId, issue.Assignee is null ? null : issue.Assignee.FullName,
            Utc(issue.CreatedAt), Utc(issue.UpdatedAt),
            Utc(issue.ResponseDueAt), UtcOrNull(issue.FirstResponseAt),
            Utc(issue.ResolveDueAt),
            IssueStateMachine.IsResponseOverdue(issue.Status, issue.FirstResponseAt, issue.ResponseDueAt, now),
            IssueStateMachine.IsResolveOverdue(issue.Status, issue.ResolveDueAt, now),
            UtcOrNull(issue.WaitingSince), UtcOrNull(issue.ResolvedAt), UtcOrNull(issue.ClosedAt),
            issue.ReopenCount,
            issue.RootCause, issue.Resolution, issue.Prevention, issue.FixReference, issue.TimeSpentMinutes,
            issue.RejectReason, issue.DuplicateOfId,
            issue.PageUrl,
            staff ? issue.UserAgent : null,
            issue.ClientInfo,
            issue.ErrorRequestId,
            staff ? issue.ReporterIp : null,
            comments, attachments, timeline,
            AllowedActions(actor, issue, now));
    }

    private static void MarkFirstResponse(Issue issue, DateTime now) => issue.FirstResponseAt ??= now;

    /// <summary>Trims the text and refuses it when it is shorter than <paramref name="min"/> or longer than <paramref name="max"/>.</summary>
    private static string RequireText(string? value, int min, int max, string tooShort, string tooLong)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length < min) throw new RequestValidationException(tooShort);
        if (text.Length > max) throw new RequestValidationException(tooLong);
        return text;
    }

    private void AddComment(Issue issue, User actor, string body, DateTime now, bool staff, bool internalNote = false) =>
        _db.IssueComments.Add(new IssueComment
        {
            IssueId = issue.Id,
            AuthorId = actor.Id,
            AuthorName = actor.FullName,
            IsStaff = staff,
            IsInternal = internalNote,
            Body = body,
            CreatedAt = now,
        });

    // Who hears about a change, and how the toast reads.
    private static readonly RealtimeAudience ToIt = new([], ItSupport: true);

    private static RealtimeAudience ToReporter(Issue issue) =>
        new(issue.ReporterUserId is { } reporter ? [reporter] : []);

    private static RealtimeAudience ToAssigneeOrIt(Issue issue) =>
        issue.AssigneeId is { } assignee ? new RealtimeAudience([assignee]) : ToIt;

    private static RealtimeNotification Toast(Issue issue, string title, string? detail, string level = "info") =>
        new(title, $"{issue.DisplayNumber} · {detail}", level, issue.Id);

    /// <summary>The reporter answered: push the resolve deadline back by the time IT spent waiting.</summary>
    private static void ResumeFromWaiting(Issue issue, DateTime now)
    {
        if (issue.WaitingSince is { } since && now > since)
            issue.ResolveDueAt += now - since;
        issue.WaitingSince = null;
    }

    private async Task Touch(Issue issue, DateTime now)
    {
        issue.UpdatedAt = now;
        issue.Version++;
        await _db.SaveChangesAsync();
    }

    private void StageAudit(User? actor, Issue issue, string action, string? oldValues, string? newValues,
        string? fallbackEmail = null, string? fallbackName = null) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = "Issue",
            EntityId = issue.Id.ToString(),
            OldValues = oldValues,
            NewValues = newValues,
            UserId = actor?.Id,
            UserEmail = actor?.Email ?? fallbackEmail,
            UserName = actor is null ? fallbackName : actor.FullName,
            UserRole = actor?.Role?.Name,
            DepartmentName = actor?.Department?.Name,
            CreatedAt = Now,
        });

    private static List<IssueUpload> ValidateUploads(IReadOnlyList<IssueUpload> files, int alreadyAttached)
    {
        var uploads = files.Where(f => f.Data.Length > 0).ToList();
        if (alreadyAttached + uploads.Count > MaxAttachmentsPerIssue)
            throw new RequestValidationException($"แนบรูปได้ไม่เกิน {MaxAttachmentsPerIssue} ไฟล์ต่อเรื่อง");

        var checkedUploads = new List<IssueUpload>();
        foreach (var file in uploads)
        {
            if (file.Data.Length > MaxAttachmentBytes)
                throw new RequestValidationException($"ไฟล์ \"{file.FileName.ToSafeFileName()}\" ใหญ่เกิน 5 MB");

            var type = DetectImageType(file.Data)
                ?? throw new RequestValidationException($"ไฟล์ \"{file.FileName.ToSafeFileName()}\" ต้องเป็นรูปภาพ (PNG, JPG, GIF หรือ WebP)");

            checkedUploads.Add(new IssueUpload(file.FileName.ToSafeFileName(), type, file.Data));
        }
        return checkedUploads;
    }

    /// <summary>Identifies an image by its first bytes, so a renamed .exe cannot pass as a picture.</summary>
    public static string? DetectImageType(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
        if (d.Length >= 6 && d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return "image/gif";
        if (d.Length >= 12 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46
            && d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50) return "image/webp";
        return null;
    }

    private static IssueAttachment ToAttachment(IssueUpload upload, Guid? uploadedBy, Guid? issueId = null) => new()
    {
        IssueId = issueId ?? Guid.Empty,
        FileName = upload.FileName,
        ContentType = upload.ContentType,
        SizeBytes = upload.Data.Length,
        Data = upload.Data,
        UploadedById = uploadedBy,
    };

    /// <summary>Timestamps are stored as UTC without a zone; mark them so browsers convert correctly.</summary>
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? UtcOrNull(DateTime? value) => value is null ? null : Utc(value.Value);
}

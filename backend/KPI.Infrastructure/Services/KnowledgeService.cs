using System.Text.RegularExpressions;
using KPI.Application.Common;
using KPI.Application.Knowledge;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Interfaces;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class KnowledgeService : IKnowledgeService
{
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 500;
    public const int MaxBodyLength = 20000;
    public const int MaxCategoryLength = 100;
    public const string DefaultCategory = "ทั่วไป";

    private const string AuditEntity = "KnowledgeArticle";
    private const int MaxSuggestWords = 5;
    private const int MinSuggestWordLength = 3;
    private const int CandidatesPerWord = 10;

    private static readonly Regex WordSplit = new(@"[\s\p{P}\p{S}]+", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IUserRepository _users;
    private readonly TimeProvider _time;

    public KnowledgeService(AppDbContext db, IUserRepository users, TimeProvider time)
    {
        _db = db;
        _users = users;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ───────────────────────── Reading ─────────────────────────

    public async Task<PagedResult<ArticleListItemDto>> ListAsync(KnowledgeQuery query, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.View);

        var rows = Visible(actor);
        // a reader has only published articles, so the filter means something to a manager alone
        if (query.Published is { } published && CanManage(actor)) rows = rows.Where(a => a.IsPublished == published);
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            rows = rows.Where(a => a.Category == category);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = query.InBody
                ? rows.Where(a => a.Title.ToLower().Contains(term) || a.Summary.ToLower().Contains(term)
                                  || a.Category.ToLower().Contains(term) || a.Body.ToLower().Contains(term))
                : rows.Where(a => a.Title.ToLower().Contains(term) || a.Summary.ToLower().Contains(term)
                                  || a.Category.ToLower().Contains(term));
        }

        var page = PagedResult<ArticleListItemDto>.ClampPage(query.Page);
        var size = PagedResult<ArticleListItemDto>.ClampSize(query.PageSize);
        var total = await rows.CountAsync();
        var items = await rows
            .OrderByDescending(a => a.UpdatedAt).ThenBy(a => a.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(a => new { a.Id, a.Title, a.Summary, a.Category, a.IsPublished, a.PublishedAt, a.UpdatedAt, a.ViewCount })
            .ToListAsync();

        return new PagedResult<ArticleListItemDto>(
            items.Select(a => new ArticleListItemDto(a.Id, a.Title, a.Summary, a.Category, a.IsPublished,
                Utc(a.PublishedAt), Utc(a.UpdatedAt), a.ViewCount)).ToList(),
            total, page, size);
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.View);
        return await Visible(actor).Select(a => a.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    public async Task<IReadOnlyList<SuggestedArticleDto>> SuggestAsync(string? title, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.View);

        var text = (title ?? string.Empty).Trim();
        if (text.Length < IKnowledgeService.SuggestMinLength) return [];

        var words = WordSplit.Split(text.ToLower())
            .Where(w => w.Length >= MinSuggestWordLength)
            .Distinct().Take(MaxSuggestWords).ToList();
        // A title without spaces (Thai is written that way) is one long word, which the loop below tries as it is.
        if (words.Count == 0) words.Add(text.ToLower());

        // Drafts never suggest, even to a manager: the box is for the person reporting a problem.
        var published = _db.KnowledgeArticles.AsNoTracking().Where(a => a.IsPublished);
        var found = new Dictionary<Guid, (SuggestedArticleDto Dto, int Score, int Views)>();
        foreach (var word in words)
        {
            var hits = await published
                .Where(a => a.Title.ToLower().Contains(word) || a.Summary.ToLower().Contains(word))
                .OrderByDescending(a => a.ViewCount)
                .Take(CandidatesPerWord)
                .Select(a => new { a.Id, a.Title, a.Summary, a.Category, a.ViewCount })
                .ToListAsync();

            foreach (var hit in hits)
            {
                // a word in the title says more than one in the summary
                var score = hit.Title.Contains(word, StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                var previous = found.TryGetValue(hit.Id, out var seen) ? seen.Score : 0;
                found[hit.Id] = (new SuggestedArticleDto(hit.Id, hit.Title, hit.Summary, hit.Category), previous + score, hit.ViewCount);
            }
        }

        return found.Values
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Views).ThenBy(x => x.Dto.Title)
            .Take(IKnowledgeService.MaxSuggestions)
            .Select(x => x.Dto)
            .ToList();
    }

    public async Task<ArticleDto> GetAsync(Guid id, Guid userId, bool countView = true)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.View);

        var article = await Visible(actor, tracked: true).FirstOrDefaultAsync(a => a.Id == id)
                      ?? throw new NotFoundException("ไม่พบบทความ");

        if (countView && article.IsPublished)
        {
            article.ViewCount++;
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone edited the article at the same moment. Losing one view is better than failing the page.
                _db.Entry(article).State = EntityState.Detached;
                article = await _db.KnowledgeArticles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
                          ?? throw new NotFoundException("ไม่พบบทความ");
            }
        }

        return ToDto(article);
    }

    // ───────────────────────── Writing ─────────────────────────

    public async Task<ArticleDto> CreateAsync(SaveArticleRequest request, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.Manage);
        var (title, summary, body, category) = Validate(request);

        var now = Now;
        var article = new KnowledgeArticle
        {
            Title = title,
            Summary = summary,
            Body = body,
            Category = category,
            AuthorId = actor.Id,
            AuthorName = actor.FullName,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.KnowledgeArticles.Add(article);
        StageAudit(actor, "Create", article, null, title);
        await _db.SaveChangesAsync();
        return ToDto(article);
    }

    public async Task<ArticleDto> UpdateAsync(Guid id, SaveArticleRequest request, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.Manage);
        var article = await LoadAsync(id);
        var (title, summary, body, category) = Validate(request);
        RequireCurrent(article, request.Version);

        var oldTitle = article.Title;
        article.Title = title;
        article.Summary = summary;
        article.Body = body;
        article.Category = category;
        Touch(article, actor);
        StageAudit(actor, "Update", article, oldTitle, title);
        await _db.SaveChangesAsync();
        return ToDto(article);
    }

    public async Task<ArticleDto> PublishAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.Manage);
        var article = await LoadAsync(id);
        if (article.IsPublished) throw new InvalidStateException("บทความนี้เผยแพร่แล้ว");

        article.IsPublished = true;
        article.PublishedAt = Now;
        Touch(article, actor);
        StageAudit(actor, "Publish", article, "Draft", article.Title);
        await _db.SaveChangesAsync();
        return ToDto(article);
    }

    public async Task<ArticleDto> UnpublishAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.Manage);
        var article = await LoadAsync(id);
        if (!article.IsPublished) throw new InvalidStateException("บทความนี้ยังไม่ได้เผยแพร่");

        article.IsPublished = false;
        article.PublishedAt = null;
        Touch(article, actor);
        StageAudit(actor, "Unpublish", article, "Published", article.Title);
        await _db.SaveChangesAsync();
        return ToDto(article);
    }

    public async Task DeleteAsync(Guid id, Guid userId)
    {
        var actor = await GetActorAsync(userId);
        RequirePermission(actor, Permissions.Knowledge.Manage);
        var article = await LoadAsync(id);

        _db.KnowledgeArticles.Remove(article);
        StageAudit(actor, "Delete", article, article.Title, null);
        await _db.SaveChangesAsync();
    }

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>What the caller may read: everything for a manager, published articles for everyone else.</summary>
    private IQueryable<KnowledgeArticle> Visible(User actor, bool tracked = false)
    {
        var rows = tracked ? _db.KnowledgeArticles.AsQueryable() : _db.KnowledgeArticles.AsNoTracking();
        return CanManage(actor) ? rows : rows.Where(a => a.IsPublished);
    }

    private async Task<KnowledgeArticle> LoadAsync(Guid id) =>
        await _db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id) ?? throw new NotFoundException("ไม่พบบทความ");

    private static void RequireCurrent(KnowledgeArticle article, int? version)
    {
        if (version is null) throw new RequestValidationException("ไม่พบเลขเวอร์ชันของบทความ กรุณาโหลดหน้านี้ใหม่");
        if (version != article.Version)
            throw new InvalidStateException("บทความนี้ถูกแก้ไขโดยคนอื่นไปแล้ว กรุณาโหลดใหม่ก่อนบันทึก");
    }

    private void Touch(KnowledgeArticle article, User actor)
    {
        article.Version++;
        article.UpdatedAt = Now;
        article.AuthorId = actor.Id;
        article.AuthorName = actor.FullName;
    }

    private static (string Title, string Summary, string Body, string Category) Validate(SaveArticleRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length < 3) throw new RequestValidationException("กรุณาระบุหัวข้อบทความอย่างน้อย 3 ตัวอักษร");
        if (title.Length > MaxTitleLength) throw new RequestValidationException($"หัวข้อยาวเกินไป (สูงสุด {MaxTitleLength} ตัวอักษร)");

        var summary = (request.Summary ?? string.Empty).Trim();
        if (summary.Length > MaxSummaryLength) throw new RequestValidationException($"คำอธิบายสั้น ๆ ยาวเกินไป (สูงสุด {MaxSummaryLength} ตัวอักษร)");

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0) throw new RequestValidationException("กรุณาเขียนเนื้อหาบทความ");
        if (body.Length > MaxBodyLength) throw new RequestValidationException($"เนื้อหายาวเกินไป (สูงสุด {MaxBodyLength:N0} ตัวอักษร)");

        var category = (request.Category ?? string.Empty).Trim();
        if (category.Length == 0) category = DefaultCategory;
        if (category.Length > MaxCategoryLength) throw new RequestValidationException($"ชื่อหมวดหมู่ยาวเกินไป (สูงสุด {MaxCategoryLength} ตัวอักษร)");

        return (title, summary, body, category);
    }

    private void StageAudit(User actor, string action, KnowledgeArticle article, string? oldValues, string? newValues) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = AuditEntity,
            EntityId = article.Id.ToString(),
            OldValues = oldValues,
            NewValues = newValues,
            UserId = actor.Id,
            UserEmail = actor.Email,
            UserName = actor.FullName,
            UserRole = actor.Role?.Name,
            DepartmentName = actor.Department?.Name,
            CreatedAt = Now,
        });

    private static ArticleDto ToDto(KnowledgeArticle a) => new(
        a.Id, a.Title, a.Summary, a.Body, a.Category, a.IsPublished, Utc(a.PublishedAt), a.AuthorName,
        Utc(a.CreatedAt), Utc(a.UpdatedAt), a.Version, a.ViewCount);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    private async Task<User> GetActorAsync(Guid userId)
    {
        var user = await _users.GetByIdWithPermissionsAsync(userId);
        if (user is null || !user.IsActive) throw new ForbiddenException("บัญชีของคุณไม่สามารถใช้งานได้");
        return user;
    }

    private static bool CanManage(User actor) => HasPermission(actor, Permissions.Knowledge.Manage);

    private static bool HasPermission(User user, string permission) =>
        user.Role.RolePermissions.Any(rp => rp.Permission.Name == permission);

    private static void RequirePermission(User actor, string permission)
    {
        if (!HasPermission(actor, permission))
            throw new ForbiddenException("คุณไม่มีสิทธิ์ทำรายการนี้");
    }
}

using KPI.Application.Common;

namespace KPI.Application.Knowledge;

/// <summary>
/// <paramref name="Published"/> narrows a manager's list to published articles (true) or drafts (false); a reader always gets published articles only.
/// The body is searched only when <paramref name="InBody"/> is set, because reading every body is slow on a large knowledge base.
/// </summary>
public record KnowledgeQuery(string? Search = null, string? Category = null, bool InBody = false, bool? Published = null, int Page = 1, int PageSize = 20);

public record ArticleListItemDto(
    Guid Id, string Title, string Summary, string Category, bool IsPublished,
    DateTime? PublishedAt, DateTime UpdatedAt, int ViewCount);

public record ArticleDto(
    Guid Id, string Title, string Summary, string Body, string Category, bool IsPublished,
    DateTime? PublishedAt, string AuthorName, DateTime CreatedAt, DateTime UpdatedAt, int Version, int ViewCount);

/// <summary><paramref name="Version"/> is the number the editor loaded; it is required when changing an article and ignored when creating one.</summary>
public record SaveArticleRequest(string? Title, string? Summary, string? Body, string? Category, int? Version = null);

public record SuggestedArticleDto(Guid Id, string Title, string Summary, string Category);

/// <summary>
/// Every method takes the acting user and enforces permission itself. A draft is invisible (not found) to anyone who
/// cannot manage the knowledge base.
/// </summary>
public interface IKnowledgeService
{
    /// <summary>A title shorter than this suggests nothing: two letters would match half of the articles.</summary>
    public const int SuggestMinLength = 3;
    public const int MaxSuggestions = 5;

    Task<PagedResult<ArticleListItemDto>> ListAsync(KnowledgeQuery query, Guid userId);
    Task<IReadOnlyList<string>> CategoriesAsync(Guid userId);

    /// <summary>Published articles that look related to a title being typed, best first (the box on the new-issue form).</summary>
    Task<IReadOnlyList<SuggestedArticleDto>> SuggestAsync(string? title, Guid userId);

    /// <summary>Opening a published article counts as a view when <paramref name="countView"/> is set (the editor passes false).</summary>
    Task<ArticleDto> GetAsync(Guid id, Guid userId, bool countView = true);

    Task<ArticleDto> CreateAsync(SaveArticleRequest request, Guid userId);
    Task<ArticleDto> UpdateAsync(Guid id, SaveArticleRequest request, Guid userId);
    Task<ArticleDto> PublishAsync(Guid id, Guid userId);
    Task<ArticleDto> UnpublishAsync(Guid id, Guid userId);
    Task DeleteAsync(Guid id, Guid userId);
}

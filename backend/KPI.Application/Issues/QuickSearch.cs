namespace KPI.Application.Issues;

public record UserHitDto(Guid Id, string Name, string Email, string? Department);

/// <summary>What the Ctrl+K box shows: a few issues, and people only for someone who may open the user list.</summary>
public record QuickSearchResult(IReadOnlyList<IssueHitDto> Issues, IReadOnlyList<UserHitDto> Users);

public interface IQuickSearchService
{
    /// <summary>Fewer than <see cref="MinLength"/> characters returns nothing: one letter would match half the table.</summary>
    public const int MinLength = 2;

    Task<QuickSearchResult> SearchAsync(string? term, Guid userId);
}

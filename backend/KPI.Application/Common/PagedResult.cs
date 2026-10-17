namespace KPI.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public static int ClampPage(int page) => Math.Max(1, page);
    public static int ClampSize(int size, int max = 100) => Math.Clamp(size, 1, max);
}

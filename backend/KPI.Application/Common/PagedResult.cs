namespace KPI.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    /// <summary>The largest page a client may ask for; the screens offer 10, 20, 30, 100 and 200.</summary>
    public const int MaxPageSize = 200;

    public static int ClampPage(int page) => Math.Max(1, page);
    public static int ClampSize(int size, int max = MaxPageSize) => Math.Clamp(size, 1, max);
}

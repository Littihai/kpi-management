namespace KPI.Application.Common;

public static class StringExtensions
{
    /// <summary>Cuts the text to at most <paramref name="max"/> characters; null stays null.</summary>
    public static string? Truncate(this string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>
    /// The bare file name from whatever a browser sent. Path.GetFileName only knows the server's own separator,
    /// so on Linux a Windows path ("C:\Users\me\a.png") would pass through untouched: cut at either kind of slash.
    /// </summary>
    public static string ToSafeFileName(this string? name, string fallback = "image")
    {
        var raw = name ?? string.Empty;
        var file = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        return (file.Length == 0 ? fallback : file).Truncate(255)!;
    }
}

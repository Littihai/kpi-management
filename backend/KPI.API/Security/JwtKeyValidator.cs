using System.Text;

namespace KPI.API.Security;

/// <summary>
/// Refuses to start with a signing key that is missing, too short, or one of the placeholder values that
/// ship in the repository. Anyone who can read the repository could otherwise forge a token for any user.
/// </summary>
public static class JwtKeyValidator
{
    public const int MinBytes = 32;

    private const int MinDistinctCharacters = 8;

    private static readonly string[] PlaceholderFragments =
    [
        "your-super-secret",
        "your-secret-key",
        "secret-key-minimum",
        "random secret",
        "changeme",
        "change-me",
        "placeholder",
    ];

    /// <summary>Returns why the key cannot be used, or null when it is acceptable.</summary>
    public static string? Problem(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "it is not set.";

        if (Encoding.UTF8.GetByteCount(key) < MinBytes)
            return $"it must be at least {MinBytes} characters long.";

        var trimmed = key.Trim();
        if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
            return "it is still the <placeholder> from appsettings.Example.json.";

        if (PlaceholderFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)))
            return "it is a placeholder value that is published in the repository.";

        if (key.Distinct().Count() < MinDistinctCharacters)
            return "it is too repetitive to be a random secret.";

        return null;
    }
}

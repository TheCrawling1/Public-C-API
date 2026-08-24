using System.Text.RegularExpressions;

namespace ApiRouter.Rules;

/// <summary>Tiny case-insensitive glob matcher supporting <c>*</c> and <c>?</c> wildcards.</summary>
public static class Glob
{
    public static bool IsMatch(string? pattern, string? value)
    {
        // An empty/absent pattern is treated as "match anything".
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
        {
            return true;
        }

        value ??= string.Empty;

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            return Regex.IsMatch(
                value,
                regex,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException)
        {
            // Treat a pathological pattern as a non-match rather than surfacing a 500.
            return false;
        }
    }
}

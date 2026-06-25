using System.Text.RegularExpressions;

namespace WheelHouse.Core.Prompts;

/// <summary>
/// Pure (DB-free) placeholder logic for prompt templates. Placeholders use the
/// <c>{{NAME}}</c> convention (letters, digits, underscores). Safe to call on every keystroke.
/// </summary>
public static partial class PromptRendering
{
    /// <summary>Returns the distinct placeholder names in first-seen order.</summary>
    public static IReadOnlyList<string> ExtractPlaceholders(string body)
    {
        if (string.IsNullOrEmpty(body)) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (Match m in PlaceholderRegex().Matches(body))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name)) ordered.Add(name);
        }
        return ordered;
    }

    /// <summary>
    /// Substitutes <c>{{PLACEHOLDER}}</c> tokens with supplied values. Tokens with no
    /// (non-empty) value are left intact so unfilled gaps stay visible.
    /// </summary>
    public static string Render(string body, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        return PlaceholderRegex().Replace(body, match =>
        {
            var name = match.Groups[1].Value;
            return values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value!
                : match.Value;
        });
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}

namespace WheelHouse.Core.Search;

/// <summary>Builds a short, single-line snippet of text centred on a search match.</summary>
public static class TranscriptSnippet
{
    public static string Build(string text, string query, int radius = 60)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrEmpty(query))
            return flat.Length <= radius * 2 ? flat : flat[..(radius * 2)] + " …";

        var idx = flat.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return flat.Length <= radius * 2 ? flat : flat[..(radius * 2)] + " …";

        var start = Math.Max(0, idx - radius);
        var end = Math.Min(flat.Length, idx + query.Length + radius);
        var core = flat[start..end].Trim();
        return (start > 0 ? "… " : "") + core + (end < flat.Length ? " …" : "");
    }
}

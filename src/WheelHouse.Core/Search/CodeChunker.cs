namespace WheelHouse.Core.Search;

/// <summary>
/// Splits (already compressed) source text into overlapping, line-aligned chunks for embedding.
/// Small files pass through as a single chunk; large files no longer lose their tail to a
/// hard truncation cap. Pure logic, no I/O.
/// </summary>
public static class CodeChunker
{
    public const int DefaultTargetSize = 1500;
    public const int DefaultOverlapSize = 200;

    /// <summary>
    /// Splits <paramref name="text"/> into chunks of roughly <paramref name="targetSize"/>
    /// characters, breaking on line boundaries and carrying ~<paramref name="overlapSize"/>
    /// characters of trailing context into the next chunk so nothing is lost at a boundary.
    /// </summary>
    public static IReadOnlyList<string> Split(
        string text, int targetSize = DefaultTargetSize, int overlapSize = DefaultOverlapSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        if (targetSize < 1) targetSize = DefaultTargetSize;
        // Overlap must stay well under the target or a chunk could never take new content.
        overlapSize = Math.Clamp(overlapSize, 0, targetSize / 2);

        if (text.Length <= targetSize) return new[] { text };

        // Hard-split pathological single lines (minified bundles etc.) so every "line" fits.
        var lines = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            if (line.Length <= targetSize) { lines.Add(line); continue; }
            for (var i = 0; i < line.Length; i += targetSize)
                lines.Add(line.Substring(i, Math.Min(targetSize, line.Length - i)));
        }

        var chunks = new List<string>();
        var current = new List<string>();
        var currentLen = 0;

        foreach (var line in lines)
        {
            if (currentLen > 0 && currentLen + line.Length + 1 > targetSize)
            {
                chunks.Add(string.Join('\n', current));

                var overlap = new List<string>();
                var overlapLen = 0;
                for (var j = current.Count - 1; j >= 0 && overlapLen + current[j].Length + 1 <= overlapSize; j--)
                {
                    overlap.Insert(0, current[j]);
                    overlapLen += current[j].Length + 1;
                }
                current = overlap;
                currentLen = overlapLen;
            }

            current.Add(line);
            currentLen += line.Length + 1;
        }

        if (current.Count > 0) chunks.Add(string.Join('\n', current));
        return chunks;
    }
}

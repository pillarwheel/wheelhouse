using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Vector;

/// <summary>
/// Lexical leg of hybrid code search: tokenized, case-insensitive substring matching over
/// indexed snippets and file paths via SQL <c>LIKE</c>. Deliberately mirrors
/// <c>TranscriptSearchService</c> (no FTS extension needed), and substring semantics find
/// partial identifiers — e.g. "FileHashes" inside "GetFileHashesAsync" — that a word
/// tokenizer would miss. Score is the fraction of query tokens a row matches.
/// </summary>
internal static class CodeIndexKeywordSearch
{
    private const int MaxTokens = 8;
    private const int PerTokenCap = 400;

    public static async Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        WheelHouseDbContext db, string query, int topN, string? repositoryPath,
        CancellationToken cancellationToken)
    {
        var tokens = Tokenize(query);
        if (tokens.Count == 0 || topN <= 0) return Array.Empty<CodeSearchResult>();

        var matched = new Dictionary<int, int>(); // entry id → number of tokens matched
        foreach (var token in tokens)
        {
            var pattern = "%" + Escape(token) + "%";
            var q = db.CodeIndex.AsQueryable();
            if (!string.IsNullOrWhiteSpace(repositoryPath))
                q = q.Where(c => c.RepositoryPath == repositoryPath);

            var ids = await q
                .Where(c => EF.Functions.Like(c.Snippet, pattern, "\\") ||
                            EF.Functions.Like(c.FilePath, pattern, "\\"))
                .Select(c => c.Id)
                .Take(PerTokenCap)
                .ToListAsync(cancellationToken);

            foreach (var id in ids)
                matched[id] = matched.TryGetValue(id, out var n) ? n + 1 : 1;
        }
        if (matched.Count == 0) return Array.Empty<CodeSearchResult>();

        var top = matched
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(topN)
            .ToList();

        var ids2 = top.Select(t => t.Key).ToList();
        var entries = await db.CodeIndex
            .Where(c => ids2.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var results = new List<CodeSearchResult>(top.Count);
        foreach (var (id, hits) in top)
            if (entries.TryGetValue(id, out var entry))
                results.Add(new CodeSearchResult(entry, (double)hits / tokens.Count));
        return results;
    }

    private static List<string> Tokenize(string? query)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(query)) return tokens;

        var current = new System.Text.StringBuilder();
        foreach (var c in query)
        {
            if (char.IsLetterOrDigit(c) || c == '_') { current.Append(c); continue; }
            if (current.Length >= 2) tokens.Add(current.ToString());
            current.Clear();
        }
        if (current.Length >= 2) tokens.Add(current.ToString());

        return tokens.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTokens).ToList();
    }

    /// <summary>Escapes LIKE wildcards so tokens are matched literally.</summary>
    private static string Escape(string q) =>
        q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

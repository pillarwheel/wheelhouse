using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Core.Search;

/// <summary>
/// Combines the semantic (vector) and lexical (keyword) legs of hybrid code search using
/// reciprocal-rank fusion: each result contributes 1/(k + rank) per list it appears in, so
/// hits found by both legs rise above hits found by only one. Pure logic, no I/O.
/// </summary>
public static class SearchFusion
{
    public const int DefaultK = 60;

    /// <param name="semanticWeight">Multiplier on the vector leg's rank contributions.</param>
    /// <param name="keywordWeight">Multiplier on the keyword leg's rank contributions. Equal weights reproduce classic (unweighted) RRF ordering.</param>
    public static IReadOnlyList<CodeSearchResult> ReciprocalRankFusion(
        IReadOnlyList<CodeSearchResult> semantic,
        IReadOnlyList<CodeSearchResult> keyword,
        int topN,
        int k = DefaultK,
        double semanticWeight = 1.0,
        double keywordWeight = 1.0)
    {
        var fused = new Dictionary<string, (CodeSearchResult First, double Score)>();

        void Accumulate(IReadOnlyList<CodeSearchResult> list, double weight)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var key = Key(list[rank].Entry);
                var contribution = weight / (k + rank + 1);
                fused[key] = fused.TryGetValue(key, out var cur)
                    ? (cur.First, cur.Score + contribution)
                    : (list[rank], contribution);
            }
        }

        Accumulate(semantic, semanticWeight);
        Accumulate(keyword, keywordWeight);

        return fused.Values
            .OrderByDescending(v => v.Score)
            .ThenBy(v => Key(v.First.Entry), StringComparer.Ordinal)
            .Take(topN)
            .Select(v => v.First with { Score = v.Score })
            .ToList();
    }

    private static string Key(CodeIndexEntry e) =>
        e.Id > 0 ? e.Id.ToString() : $"{e.FilePath}|{e.SymbolName}";
}

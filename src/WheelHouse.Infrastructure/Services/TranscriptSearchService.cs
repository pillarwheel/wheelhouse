using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Search;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Case-insensitive full-text search over <c>SessionEvents.Text</c> using SQL <c>LIKE</c>
/// (no FTS5 extension required). Matches are grouped by session and ranked by recency.
/// </summary>
public class TranscriptSearchService : ITranscriptSearch
{
    private const int RawCap = 1000;
    private readonly WheelHouseDbContext _db;

    public TranscriptSearchService(WheelHouseDbContext db) => _db = db;

    public async Task<IReadOnlyList<TranscriptSessionMatches>> SearchAsync(
        string query, int maxSessions = 20, int hitsPerSession = 3,
        CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length == 0) return Array.Empty<TranscriptSessionMatches>();

        var pattern = "%" + Escape(query) + "%";
        var raw = await _db.SessionEvents
            .Where(e => EF.Functions.Like(e.Text, pattern, "\\"))
            .OrderByDescending(e => e.Id)
            .Take(RawCap)
            .Select(e => new { e.Id, e.AgentSessionId, e.Kind, e.Text, e.CreatedAt })
            .ToListAsync(cancellationToken);

        if (raw.Count == 0) return Array.Empty<TranscriptSessionMatches>();

        var groups = raw.GroupBy(e => e.AgentSessionId).ToList();
        var ids = groups.Select(g => g.Key).ToList();

        var sessions = await _db.Sessions
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.RepositoryPath, s.Status })
            .ToListAsync(cancellationToken);
        var meta = sessions.ToDictionary(s => s.Id);

        return groups
            .Where(g => meta.ContainsKey(g.Key))
            .Select(g =>
            {
                var m = meta[g.Key];
                var hits = g.OrderBy(e => e.Id).Take(hitsPerSession)
                    .Select(e => new TranscriptHit(
                        e.Id, e.Kind, TranscriptSnippet.Build(e.Text, query), e.CreatedAt))
                    .ToList();
                return (Recent: g.Max(e => e.CreatedAt),
                        Match: new TranscriptSessionMatches(
                            g.Key, m.Name, m.RepositoryPath, m.Status, g.Count(), hits));
            })
            .OrderByDescending(x => x.Recent)
            .Take(maxSessions)
            .Select(x => x.Match)
            .ToList();
    }

    /// <summary>Escapes LIKE wildcards so the query is matched literally.</summary>
    private static string Escape(string q) =>
        q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

namespace WheelHouse.Core.Interfaces;

/// <summary>A single matching transcript line within a session.</summary>
public record TranscriptHit(int EventId, string Kind, string Snippet, DateTime CreatedAt);

/// <summary>A session containing transcript matches, with a few representative hits.</summary>
public record TranscriptSessionMatches(
    int SessionId,
    string SessionName,
    string RepositoryPath,
    SessionStatus Status,
    int MatchCount,
    IReadOnlyList<TranscriptHit> Hits);

/// <summary>Full-text search across persisted session transcripts.</summary>
public interface ITranscriptSearch
{
    Task<IReadOnlyList<TranscriptSessionMatches>> SearchAsync(
        string query,
        int maxSessions = 20,
        int hitsPerSession = 3,
        CancellationToken cancellationToken = default);
}

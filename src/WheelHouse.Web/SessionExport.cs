using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Export;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Web;

/// <summary>Builds a downloadable Markdown report for a session (shared by the session view and history list).</summary>
public static class SessionExport
{
    public static async Task<(string Filename, string Markdown)?> BuildAsync(
        WheelHouseDbContext db, int sessionId)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return null;

        var tasks = await db.Tasks.Where(t => t.AgentSessionId == sessionId)
            .OrderBy(t => t.Sequence).ToListAsync();
        var events = await db.SessionEvents.Where(e => e.AgentSessionId == sessionId)
            .OrderBy(e => e.Id).ToListAsync();

        var markdown = TranscriptMarkdown.Build(session, tasks, events);
        var safeName = new string(session.Name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return ($"wheelhouse-session-{sessionId}-{safeName}.md", markdown);
    }

    /// <summary>
    /// Writes the session's Markdown report into the repository at
    /// <c>.wheelhouse/sessions/&lt;id&gt;.md</c> (GitOps). Returns the path written, or null if the
    /// session no longer exists.
    /// </summary>
    public static async Task<string?> SaveToWorkspaceAsync(
        WheelHouseDbContext db, int sessionId, string repositoryPath)
    {
        var export = await BuildAsync(db, sessionId);
        if (export is not { } e) return null;

        var path = SessionArchive.FullPath(repositoryPath, sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, e.Markdown);
        return path;
    }
}

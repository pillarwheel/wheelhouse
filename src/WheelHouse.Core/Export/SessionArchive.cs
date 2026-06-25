namespace WheelHouse.Core.Export;

/// <summary>Conventions for archiving a session's Markdown report inside the target repository.</summary>
public static class SessionArchive
{
    /// <summary>Path relative to the repo root, e.g. <c>.wheelhouse/sessions/12.md</c>.</summary>
    public static string RelativePath(int sessionId)
        => Path.Combine(".wheelhouse", "sessions", $"{sessionId}.md");

    /// <summary>Absolute path inside <paramref name="repositoryPath"/>.</summary>
    public static string FullPath(string repositoryPath, int sessionId)
        => Path.Combine(repositoryPath, RelativePath(sessionId));
}

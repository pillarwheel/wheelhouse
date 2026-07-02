namespace WheelHouse.Core.Search;

/// <summary>
/// Single source of truth for which files belong in the RAG code index, shared by the
/// repository indexer and the filesystem watcher so they can never disagree.
/// </summary>
public static class IndexableFiles
{
    public static readonly string[] SupportedExtensions =
        { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".razor", ".md", ".json", ".yaml", ".yml" };

    public static readonly string[] IgnoredDirs =
        { "bin", "obj", "node_modules", ".git", ".vs", "dist", "build" };

    public static bool HasSupportedExtension(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsIgnoredDir(string directoryName) =>
        IgnoredDirs.Contains(directoryName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when a file under <paramref name="rootPath"/> should be indexed: it has a supported
    /// extension and no path segment between the root and the file is an ignored directory.
    /// </summary>
    public static bool IsIndexable(string rootPath, string fullPath)
    {
        if (!HasSupportedExtension(fullPath)) return false;

        var relative = Path.GetRelativePath(rootPath, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal)) return false;

        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < segments.Length - 1; i++)
            if (IsIgnoredDir(segments[i])) return false;
        return true;
    }
}

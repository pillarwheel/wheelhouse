using System.Text;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Core.Search;

/// <summary>
/// Builds the repository-context string handed to Gemini when generating a plan, enriched with
/// semantically-relevant code snippets so plans reference real files and symbols. Pure/testable.
/// </summary>
public static class RagContext
{
    public static string Build(
        string repositoryPath, IReadOnlyList<CodeSearchResult> results, int maxSnippetChars = 1200)
    {
        if (results.Count == 0) return $"Repository: {repositoryPath}";

        var sb = new StringBuilder();
        sb.Append("Repository: ").AppendLine(repositoryPath).AppendLine();
        sb.AppendLine("Relevant code from the repository (local semantic search):").AppendLine();

        foreach (var r in results)
        {
            var rel = SafeRelative(repositoryPath, r.Entry.FilePath);
            var snippet = r.Entry.Snippet.Length > maxSnippetChars
                ? r.Entry.Snippet[..maxSnippetChars] + " …"
                : r.Entry.Snippet;

            sb.Append("File: ").AppendLine(rel);
            sb.AppendLine("```").AppendLine(snippet.Trim()).AppendLine("```").AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string SafeRelative(string baseDir, string path)
    {
        try { return Path.GetRelativePath(baseDir, path); }
        catch { return path; }
    }
}

using System.Text;
using System.Text.RegularExpressions;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Regex-based "AST-lite" compression: strips comments, XML doc-comments, and redundant
/// whitespace from C#-like source so fewer tokens are sent to the LLM while keeping structure.
/// Deliberately conservative — never touches string literal contents.
/// </summary>
public partial class CodeCompressionService : ICodeCompressionService
{
    // C-style: // line comments + /* */ blocks. Includes web + common backend languages.
    private static readonly HashSet<string> CStyleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".ts", ".tsx", ".jsx", ".mjs", ".cjs",
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".java", ".go", ".rs", ".kt", ".swift", ".php", ".scala"
    };

    // Hash-comment languages.
    private static readonly HashSet<string> HashExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".py", ".rb", ".sh", ".bash"
    };

    public string Compress(string source) => CompressCStyle(source);

    public string CompressForFile(string source, string filePath)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;
        var ext = Path.GetExtension(filePath);
        if (CStyleExtensions.Contains(ext)) return CompressCStyle(source);
        if (HashExtensions.Contains(ext)) return CompressHash(source);
        return source; // unknown style (.razor, .json, .md, …) — leave as-is
    }

    private string CompressCStyle(string source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        var noBlockComments = BlockComment().Replace(source, " ");

        var sb = new StringBuilder(noBlockComments.Length);
        foreach (var rawLine in noBlockComments.Split('\n'))
        {
            var line = StripLineComment(rawLine).TrimEnd();
            if (line.Trim().Length == 0) continue;       // drop blank lines
            sb.Append(line).Append('\n');
        }

        // Collapse runs of 3+ spaces that aren't leading indentation.
        return MultiSpace().Replace(sb.ToString(), " ").TrimEnd();
    }

    /// <summary>
    /// Compresses hash-comment languages (Python, Ruby, shell): strips <c>#</c> comments
    /// (ignoring <c>#</c> inside string literals on the same line) and blank lines. Best-effort —
    /// does not track multi-line/triple-quoted strings.
    /// </summary>
    private string CompressHash(string source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        var sb = new StringBuilder(source.Length);
        foreach (var rawLine in source.Split('\n'))
        {
            var line = StripHashComment(rawLine).TrimEnd();
            if (line.Trim().Length == 0) continue;
            sb.Append(line).Append('\n');
        }
        return MultiSpace().Replace(sb.ToString(), " ").TrimEnd();
    }

    private static string StripHashComment(string line)
    {
        bool inSingle = false, inDouble = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\') { i++; continue; }
            if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '#' && !inSingle && !inDouble)
                return line.Substring(0, i);
        }
        return line;
    }

    /// <summary>
    /// Removes a <c>//</c> line comment while ignoring <c>//</c> that appears inside a
    /// string or char literal. Lines beginning with <c>///</c> (doc comments) are dropped whole.
    /// </summary>
    private static string StripLineComment(string line)
    {
        if (line.TrimStart().StartsWith("///")) return string.Empty;

        bool inString = false, inChar = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            if (c == '\\') { i++; continue; }            // skip escaped char
            if (c == '"' && !inChar) inString = !inString;
            else if (c == '\'' && !inString) inChar = !inChar;
            else if (!inString && !inChar && c == '/' && line[i + 1] == '/')
                return line.Substring(0, i);
        }
        return line;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"(?<=\S) {2,}")]
    private static partial Regex MultiSpace();
}

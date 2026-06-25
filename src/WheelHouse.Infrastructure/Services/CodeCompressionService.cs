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
    public string Compress(string source)
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

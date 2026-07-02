using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Agents;

/// <summary>
/// Pure (filesystem-free) construction of the argument vectors used to invoke Claude Code,
/// with or without the Headroom compression wrapper. Kept separate so it can be unit-tested.
/// </summary>
public static class ClaudeCommand
{
    /// <summary>
    /// Builds the <c>claude</c> arguments for a single print-mode, stream-json run. When
    /// <paramref name="mcpConfigPath"/> is given, the run also loads WheelHouse's MCP server
    /// (live code-index search) and pre-allows its tools (<paramref name="mcpAllowedTools"/>)
    /// so print mode can call them without prompting.
    /// </summary>
    public static List<string> BuildAgentArgs(
        AgentRunRequest request, string? mcpConfigPath = null, IReadOnlyList<string>? mcpAllowedTools = null)
    {
        var args = new List<string>
        {
            "-p", request.Prompt,
            "--output-format", "stream-json",
            "--verbose"
        };
        if (!string.IsNullOrWhiteSpace(request.PermissionMode))
        {
            args.Add("--permission-mode");
            args.Add(request.PermissionMode!);
        }

        var allowedTools = new List<string>(request.AllowedTools ?? Array.Empty<string>());
        if (!string.IsNullOrWhiteSpace(mcpConfigPath))
        {
            args.Add("--mcp-config");
            args.Add(mcpConfigPath!);
            foreach (var tool in mcpAllowedTools ?? Array.Empty<string>())
                if (!allowedTools.Contains(tool))
                    allowedTools.Add(tool);
        }
        if (allowedTools.Count > 0)
        {
            args.Add("--allowedTools");
            args.Add(string.Join(",", allowedTools));
        }
        if (request.DisallowedTools is { Count: > 0 })
        {
            args.Add("--disallowedTools");
            args.Add(string.Join(",", request.DisallowedTools));
        }
        if (!string.IsNullOrWhiteSpace(request.ResumeSessionId))
        {
            args.Add("--resume");
            args.Add(request.ResumeSessionId!);
        }
        return args;
    }

    /// <summary>
    /// Wraps the agent args as <c>wrap claude [flags] -- &lt;agentArgs&gt;</c> for the
    /// <c>headroom</c> executable. The <c>--</c> separator passes everything after it
    /// through to Claude unchanged.
    /// </summary>
    public static List<string> BuildHeadroomArgs(IEnumerable<string> wrapFlags, IReadOnlyList<string> agentArgs)
    {
        var args = new List<string> { "wrap", "claude" };
        args.AddRange(wrapFlags.Where(f => !string.IsNullOrWhiteSpace(f)));
        args.Add("--");
        args.AddRange(agentArgs);
        return args;
    }
}

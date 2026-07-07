using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Core.Mcp;

/// <summary>
/// Pure mapping from the workspace <see cref="McpPolicy"/> to concrete enforcement decisions.
/// The policy file is edited in Workspace Settings; this class is what makes it real at the
/// two enforcement points (the Claude launcher and the MCP server).
/// </summary>
public static class McpPolicyEnforcement
{
    /// <summary>Claude Code tool names denied by the policy's capability switches.</summary>
    public static IReadOnlyList<string> DisallowedToolsFor(McpPolicy policy)
    {
        var tools = new List<string>();
        if (!policy.AllowShell) tools.Add("Bash");
        if (!policy.AllowNetwork)
        {
            tools.Add("WebFetch");
            tools.Add("WebSearch");
        }
        return tools;
    }

    /// <summary>
    /// Returns the run request with the policy's denials merged into
    /// <see cref="AgentRunRequest.DisallowedTools"/> (existing entries preserved, no duplicates).
    /// </summary>
    public static AgentRunRequest Apply(AgentRunRequest request, McpPolicy policy)
    {
        var denied = DisallowedToolsFor(policy);
        if (denied.Count == 0) return request;

        var merged = new List<string>(request.DisallowedTools ?? Array.Empty<string>());
        foreach (var tool in denied)
            if (!merged.Contains(tool, StringComparer.OrdinalIgnoreCase))
                merged.Add(tool);
        return request with { DisallowedTools = merged };
    }
}

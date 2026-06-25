using WheelHouse.Core.Agents;

namespace WheelHouse.Core.Interfaces;

/// <summary>Options for a single Claude Code invocation.</summary>
/// <param name="Prompt">The instruction handed to Claude.</param>
/// <param name="WorkingDirectory">Repository root the process runs in.</param>
/// <param name="Environment">Extra environment variables for the subprocess.</param>
/// <param name="ResumeSessionId">When set, resumes an existing Claude session.</param>
/// <param name="PermissionMode">Claude permission mode: default / acceptEdits / bypassPermissions / plan.</param>
/// <param name="AllowedTools">Tool patterns auto-approved without prompting (e.g. <c>Bash(git status:*)</c>).</param>
/// <param name="DisallowedTools">Tool patterns always denied.</param>
public record AgentRunRequest(
    string Prompt,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? ResumeSessionId = null,
    string? PermissionMode = null,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<string>? DisallowedTools = null);

/// <summary>
/// Drives the Claude Code CLI subprocess and streams its output back as normalized events.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Runs Claude Code for the given request, yielding parsed stream events as they arrive.
    /// </summary>
    IAsyncEnumerable<AgentStreamEvent> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true if the <c>claude</c> executable can be located/executed.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Describes how Claude will be launched (paths, Headroom mode, compression).</summary>
    Task<AgentRuntimeInfo> GetRuntimeInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a verification command (e.g. dotnet test) and returns the exit code and output.</summary>
    Task<(int ExitCode, string Output)> RunVerificationAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}


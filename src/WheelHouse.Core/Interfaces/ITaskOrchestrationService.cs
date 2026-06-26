using WheelHouse.Core.Agents;

namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Abstracts the agent execution service used by a session flow (Claude Code by default).
/// Swappable per-session via <see cref="ISessionFlowResolver"/>.
/// </summary>
public interface ITaskOrchestrationService
{
    IAsyncEnumerable<AgentStreamEvent> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);

    Task<(int ExitCode, string Output)> RunVerificationAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

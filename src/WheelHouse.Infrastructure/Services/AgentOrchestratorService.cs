using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="ITaskOrchestrationService"/> by delegating to <see cref="IAgentOrchestrator"/>.
/// Registered under the key <c>"ClaudeCode"</c> for keyed DI resolution.
/// </summary>
public class AgentOrchestratorService : ITaskOrchestrationService
{
    private readonly IAgentOrchestrator _orchestrator;

    public AgentOrchestratorService(IAgentOrchestrator orchestrator) => _orchestrator = orchestrator;

    public IAsyncEnumerable<AgentStreamEvent> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
        => _orchestrator.RunAsync(request, cancellationToken);

    public Task<(int ExitCode, string Output)> RunVerificationAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default)
        => _orchestrator.RunVerificationAsync(command, workingDirectory, cancellationToken);
}

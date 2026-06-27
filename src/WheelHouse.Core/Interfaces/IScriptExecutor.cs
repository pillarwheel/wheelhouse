using WheelHouse.Core.Agents;
using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Runs a session's visual script graph (<see cref="Models.Script.ScriptGraph"/>),
/// walking control-flow edges between nodes and streaming progress back to the caller.
/// </summary>
public interface IScriptExecutor
{
    /// <summary>
    /// Optional human-in-the-loop gate. When set, a <c>wait-approval</c> node suspends execution and
    /// invokes this with the approval message; returning <c>true</c> routes to <c>Approved</c>,
    /// <c>false</c> to <c>Rejected</c>. When null, approval nodes auto-approve.
    /// </summary>
    Func<string, Task<bool>>? ApprovalHandler { get; set; }

    /// <summary>
    /// Executes the graph stored on the session's template.
    /// </summary>
    /// <param name="session">The session whose template carries the graph; supplies goal and repository path.</param>
    /// <param name="onLog">Invoked with human-readable log lines and their event kind.</param>
    /// <param name="onNodeActive">Invoked with the id of the node about to run (for UI highlighting).</param>
    Task RunGraphAsync(
        AgentSession session,
        Action<string, AgentEventKind> onLog,
        Action<string> onNodeActive,
        CancellationToken cancellationToken);
}

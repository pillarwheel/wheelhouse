namespace WheelHouse.Core.Models.Script;

/// <summary>
/// Live telemetry for one executed script node, streamed to the UI as each node finishes
/// so the graph can show per-node counters during the run, not only at completion.
/// </summary>
/// <param name="NodeId">The graph node that ran.</param>
/// <param name="ElapsedMs">Wall-clock time the node took.</param>
/// <param name="ApproxTokens">Tokens attributed to the node — real CLI counts when available, chars÷4 otherwise. Approximate under parallel branches.</param>
/// <param name="CostUsd">USD cost attributed to the node when the agent CLI reported one.</param>
/// <param name="Succeeded">False when the node threw an engine error.</param>
public record ScriptNodeTelemetry(
    string NodeId,
    long ElapsedMs,
    int ApproxTokens,
    double CostUsd,
    bool Succeeded);

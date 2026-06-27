namespace WheelHouse.Core.Models.Script;

/// <summary>
/// Telemetry captured for a single visual-script run (self_improvement_testing_plan §3).
/// Used to track flow health: node success rate, handoff schema errors, final compile state,
/// approximate token usage, and wall-clock duration.
/// </summary>
public record ScriptRunMetrics
{
    /// <summary>Total nodes the engine attempted to execute.</summary>
    public int NodesExecuted { get; init; }

    /// <summary>Nodes that completed without an uncaught engine error and without routing to a failure port.</summary>
    public int NodesSucceeded { get; init; }

    /// <summary>Wired data inputs that resolved to a missing/empty upstream value (schema/handoff errors).</summary>
    public int HandoffErrors { get; init; }

    /// <summary>Result of the last <c>verification</c> node, when one ran (compilation/test pass rate).</summary>
    public bool? FinalVerificationPassed { get; init; }

    /// <summary>Rough token estimate across LLM/agent nodes (chars ÷ 4) for token-efficiency tracking.</summary>
    public int ApproxTokens { get; init; }

    /// <summary>Wall-clock duration of the run in milliseconds.</summary>
    public long ElapsedMs { get; init; }

    /// <summary>Fraction of executed nodes that succeeded (0–1).</summary>
    public double NodeSuccessRate => NodesExecuted == 0 ? 1.0 : (double)NodesSucceeded / NodesExecuted;
}

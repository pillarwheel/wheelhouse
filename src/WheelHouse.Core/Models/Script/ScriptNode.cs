namespace WheelHouse.Core.Models.Script;

/// <summary>
/// A single node in a visual script graph. The <see cref="Type"/> determines which
/// ports the node exposes and how the <see cref="ScriptExecutor"/> runs it; the free-form
/// <see cref="Settings"/> bag carries type-specific configuration (prompt bodies, commands,
/// loop counts, …).
/// </summary>
public class ScriptNode
{
    /// <summary>Unique identifier within the graph.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Node kind, e.g. <c>start</c>, <c>gemini-prompt</c>, <c>claude-prompt</c>,
    /// <c>claude-execute</c>, <c>verification</c>, <c>loop-count</c>, <c>conditional</c>,
    /// <c>generate-tasks</c>.
    /// </summary>
    public string Type { get; set; } = "start";

    /// <summary>User-facing display label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Canvas X coordinate in pixels.</summary>
    public double X { get; set; }

    /// <summary>Canvas Y coordinate in pixels.</summary>
    public double Y { get; set; }

    /// <summary>Type-specific configuration (system prompt, template body, command line, max iterations, …).</summary>
    public Dictionary<string, string> Settings { get; set; } = new();
}

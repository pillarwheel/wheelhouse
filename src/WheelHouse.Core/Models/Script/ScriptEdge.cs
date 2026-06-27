namespace WheelHouse.Core.Models.Script;

/// <summary>
/// A directed connection between two node ports. Edges carry either control flow
/// (e.g. <c>Next</c> → <c>Execute</c>, <c>Success</c>/<c>Failure</c>) or data
/// (e.g. <c>Response</c> → <c>Context</c>, <c>Goal</c> → <c>Goal</c>).
/// </summary>
public class ScriptEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string SourceNodeId { get; set; } = string.Empty;

    /// <summary>Output port on the source node, e.g. <c>Next</c>, <c>Success</c>, <c>Failure</c>, <c>Response</c>.</summary>
    public string SourcePort { get; set; } = string.Empty;

    public string TargetNodeId { get; set; } = string.Empty;

    /// <summary>Input port on the target node, e.g. <c>Execute</c>, <c>Context</c>, <c>Goal</c>.</summary>
    public string TargetPort { get; set; } = string.Empty;
}

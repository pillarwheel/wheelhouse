using System.Text.Json;

namespace WheelHouse.Core.Models.Script;

/// <summary>
/// A visual script: a set of <see cref="ScriptNode"/>s wired together by <see cref="ScriptEdge"/>s.
/// Serialized to <see cref="SessionTemplate.GraphJson"/> and executed by the script engine.
/// </summary>
public class ScriptGraph
{
    public List<ScriptNode> Nodes { get; set; } = new();
    public List<ScriptEdge> Edges { get; set; } = new();

    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    /// <summary>Serializes the graph to a compact JSON string.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, _opts);

    /// <summary>Parses a graph from JSON, returning an empty graph for null/blank/invalid input.</summary>
    public static ScriptGraph FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ScriptGraph();
        try
        {
            return JsonSerializer.Deserialize<ScriptGraph>(json, _opts) ?? new ScriptGraph();
        }
        catch (JsonException)
        {
            return new ScriptGraph();
        }
    }

    /// <summary>Returns the single start node, or null when the graph has none.</summary>
    public ScriptNode? StartNode =>
        Nodes.FirstOrDefault(n => string.Equals(n.Type, "start", StringComparison.OrdinalIgnoreCase));
}

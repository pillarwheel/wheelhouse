namespace WheelHouse.Core.Models;

/// <summary>
/// A reusable template that defines the sequence of planning and execution steps
/// used when orchestrating an agent session.
/// </summary>
public class SessionTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<FlowStepConfiguration> Steps { get; set; } = new();

    /// <summary>
    /// Serialized JSON of the visual script graph (<see cref="Script.ScriptGraph"/>).
    /// When non-empty, WheelHouse executes this node graph instead of the linear <see cref="Steps"/>.
    /// </summary>
    public string? GraphJson { get; set; }
}

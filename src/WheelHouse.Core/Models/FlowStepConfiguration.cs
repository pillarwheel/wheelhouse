namespace WheelHouse.Core.Models;

public enum FlowStepType
{
    Planning,
    Task,
    Execute,
    Verify
}

/// <summary>
/// A single configurable step within a <see cref="SessionTemplate"/>.
/// </summary>
public class FlowStepConfiguration
{
    public FlowStepType StepType { get; set; }

    /// <summary>Logical name of the service to invoke, e.g. "Gemini", "ClaudeCode".</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Service-specific settings serialized as JSON.</summary>
    public Dictionary<string, string> ConfigurationParameters { get; set; } = new();
}

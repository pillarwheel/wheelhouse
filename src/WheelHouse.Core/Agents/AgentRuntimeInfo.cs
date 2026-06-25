namespace WheelHouse.Core.Agents;

/// <summary>Snapshot of how the agent orchestrator will launch Claude (for diagnostics/UI).</summary>
/// <param name="ClaudeAvailable">Whether the claude executable was located.</param>
/// <param name="ClaudePath">Resolved path to claude, if found.</param>
/// <param name="HeadroomAvailable">Whether the headroom executable was located.</param>
/// <param name="HeadroomPath">Resolved path to headroom, if found.</param>
/// <param name="HeadroomMode">Configured mode: auto / on / off.</param>
/// <param name="CompressionActive">True when runs will actually be routed through Headroom.</param>
public record AgentRuntimeInfo(
    bool ClaudeAvailable,
    string? ClaudePath,
    bool HeadroomAvailable,
    string? HeadroomPath,
    string HeadroomMode,
    bool CompressionActive);

namespace WheelHouse.Core.Models;

/// <summary>
/// Represents the security-first, default-deny MCP and tool permission policy configurations.
/// </summary>
public class McpPolicy
{
    public bool DefaultDeny { get; set; } = true;
    public bool AllowNetwork { get; set; } = false;
    public bool AllowShell { get; set; } = false;
    public int ToolTimeoutMs { get; set; } = 30000;
    public int MaxToolCallsPerTurn { get; set; } = 8;
    public bool AuditLog { get; set; } = true;
}

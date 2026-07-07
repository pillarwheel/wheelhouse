namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Configures the cost-cascade orchestrator. The cascade is the default execution path;
/// this is the off switch for users who never want cheap-tier (Gemini) file writes.
/// </summary>
public class CascadeOptions
{
    /// <summary>"auto"/"on" try the cheap tier first (default); "off" always routes straight to Claude Code.</summary>
    public string Mode { get; set; } =
        Environment.GetEnvironmentVariable("WHEELHOUSE_CASCADE") ?? "auto";

    public bool Enabled => !string.Equals(Mode, "off", StringComparison.OrdinalIgnoreCase);
}

namespace WheelHouse.Infrastructure.Agents;

/// <summary>
/// Configures the optional Headroom context-compression wrapper
/// (https://github.com/headroomlabs-ai/headroom). When enabled and the <c>headroom</c>
/// executable is present, Claude is launched via <c>headroom wrap claude -- &lt;args&gt;</c>,
/// which compresses context before it reaches the model to cut token usage.
/// </summary>
public class HeadroomOptions
{
    /// <summary>"auto" (use when installed), "on" (require it), or "off" (never).</summary>
    public string Mode { get; set; } =
        Environment.GetEnvironmentVariable("WHEELHOUSE_HEADROOM") ?? "auto";

    /// <summary>Optional explicit path to the headroom executable (overrides PATH lookup).</summary>
    public string? ExecutablePath { get; set; } =
        Environment.GetEnvironmentVariable("WHEELHOUSE_HEADROOM_PATH");

    /// <summary>
    /// Extra flags passed to <c>headroom wrap claude</c> before the <c>--</c> separator,
    /// e.g. <c>--memory</c>, <c>--code-graph</c>.
    /// </summary>
    public List<string> WrapFlags { get; set; } = new();

    /// <summary>
    /// When true (default), the Claude subprocess inherits no <c>ANTHROPIC_API_KEY</c> while wrapped,
    /// so it uses its subscription/OAuth login. This is required because Claude sends credentials to
    /// Headroom's proxy as a Bearer token, and Anthropic rejects an API key in Bearer form (401).
    /// </summary>
    public bool UseSubscriptionAuth { get; set; } = true;
}

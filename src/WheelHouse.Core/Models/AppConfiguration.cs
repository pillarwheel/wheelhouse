namespace WheelHouse.Core.Models;

/// <summary>
/// Single-row application configuration (branding and runtime defaults) editable from the
/// in-app Settings page. Infrastructure/secret config (API keys, model names, backends) lives in
/// environment variables / <c>.env</c> instead.
/// </summary>
public class AppConfiguration
{
    /// <summary>Always 1 — there is exactly one configuration row.</summary>
    public int Id { get; set; } = 1;

    /// <summary>Organization name shown in the sidebar and used in reports.</summary>
    public string CompanyName { get; set; } = "Your Studio";

    /// <summary>Product name shown in the sidebar / titles.</summary>
    public string ProductName { get; set; } = "WheelHouse";

    /// <summary>Short tagline shown under the navigation.</summary>
    public string Tagline { get; set; } = "Coding research, development & implementation";

    /// <summary>Permission mode applied to newly added workspaces.</summary>
    public string DefaultPermissionMode { get; set; } = "acceptEdits";
}

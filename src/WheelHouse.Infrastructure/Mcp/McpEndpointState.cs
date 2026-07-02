using System.Text.Json;

namespace WheelHouse.Infrastructure.Mcp;

/// <summary>
/// Runtime state of the in-app MCP endpoint. The web host calls <see cref="Activate"/> once
/// it knows its bound address; from then on Claude runs are launched with
/// <c>--mcp-config</c> pointing at the generated config file so they can query the index.
/// </summary>
public class McpEndpointState
{
    /// <summary>"auto"/"on" expose the index to Claude via MCP (default); "off" disables it.</summary>
    public string Mode { get; set; } =
        Environment.GetEnvironmentVariable("WHEELHOUSE_MCP") ?? "auto";

    public bool Enabled => !string.Equals(Mode, "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Full URL of the MCP endpoint, e.g. "http://localhost:5000/mcp". Null until activated.</summary>
    public string? Url { get; private set; }

    /// <summary>Path of the generated mcp-config JSON passed to <c>claude --mcp-config</c>.</summary>
    public string? ConfigPath { get; private set; }

    /// <summary>Tool names Claude is allowed to call without prompting.</summary>
    public IReadOnlyList<string> AllowedToolNames { get; } = new[]
    {
        $"mcp__{WheelHouseMcpServer.ServerName}__search_code",
        $"mcp__{WheelHouseMcpServer.ServerName}__get_knowledge"
    };

    /// <summary>Records the endpoint URL and writes the mcp-config file Claude will load.</summary>
    public void Activate(string mcpUrl)
    {
        if (!Enabled) return;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheelHouse");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "mcp-config.json");
        var json = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                [WheelHouseMcpServer.ServerName] = new { type = "http", url = mcpUrl }
            }
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        Url = mcpUrl;
        ConfigPath = path;
    }
}

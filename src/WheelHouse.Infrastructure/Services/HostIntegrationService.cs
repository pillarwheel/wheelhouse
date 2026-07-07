using System.Text.Json;
using System.Text.Json.Nodes;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IHostIntegrationService"/> to export MCP configs and CI/CD pipelines.
/// </summary>
public class HostIntegrationService : IHostIntegrationService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true
    };

    public async Task ExportVsCodeSettingsAsync(string repositoryPath, string mcpUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return;
        }

        var dir = Path.Combine(repositoryPath, ".vscode");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "settings.json");
        JsonNode root;

        if (File.Exists(path))
        {
            try
            {
                var content = await File.ReadAllTextAsync(path, cancellationToken);
                root = JsonNode.Parse(content) ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        // Target key: github.copilot.chat.mcp.servers
        var key = "github.copilot.chat.mcp.servers";
        var servers = root[key] as JsonArray;
        if (servers == null)
        {
            servers = new JsonArray();
            root[key] = servers;
        }

        // Clean out any existing "wheelhouse" entry
        for (int i = servers.Count - 1; i >= 0; i--)
        {
            if (servers[i]?["name"]?.ToString() == "wheelhouse")
            {
                servers.RemoveAt(i);
            }
        }

        // Add the updated server entry
        servers.Add(new JsonObject
        {
            ["name"] = "wheelhouse",
            ["type"] = "http",
            ["url"] = mcpUrl
        });

        var json = root.ToJsonString(_jsonOpts);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task ExportClaudeDesktopConfigAsync(string repositoryPath, string mcpUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return;
        }

        var dir = Path.Combine(repositoryPath, ".wheelhouse");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "claude_desktop_config.json");
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["wheelhouse"] = new
                {
                    type = "http",
                    url = mcpUrl
                }
            }
        };

        var json = JsonSerializer.Serialize(config, _jsonOpts);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task ExportGitHubActionsWorkflowAsync(string repositoryPath, string testCommand, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return;
        }

        var dir = Path.Combine(repositoryPath, ".github", "workflows");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "wheelhouse-verify.yml");
        var command = string.IsNullOrWhiteSpace(testCommand) ? "dotnet test" : testCommand.Trim();

        var yaml = $"""
name: WheelHouse CI Verification
on: [push, pull_request]
jobs:
  verify:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Verify Workspace Rules
        run: {command}
""";

        await File.WriteAllTextAsync(path, yaml, cancellationToken);
    }
}

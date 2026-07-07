using System.Text.Json;
using System.Text.Json.Nodes;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class HostIntegrationServiceTests
{
    [Fact]
    public async Task ExportVsCodeSettings_Creates_Or_Merges_Settings_File()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = new HostIntegrationService();
            var vscodeDir = Path.Combine(tempDir, ".vscode");
            var settingsPath = Path.Combine(vscodeDir, "settings.json");

            // 1. Export to non-existent file
            var mcpUrl = "http://localhost:5000/mcp";
            await service.ExportVsCodeSettingsAsync(tempDir, mcpUrl);

            Assert.True(File.Exists(settingsPath));
            var content1 = await File.ReadAllTextAsync(settingsPath);
            var node1 = JsonNode.Parse(content1);
            var servers1 = node1?["github.copilot.chat.mcp.servers"] as JsonArray;
            Assert.NotNull(servers1);
            Assert.Single(servers1);
            Assert.Equal("wheelhouse", servers1[0]?["name"]?.ToString());
            Assert.Equal(mcpUrl, servers1[0]?["url"]?.ToString());

            // 2. Export to existing file (should merge and preserve other keys)
            node1!["editor.fontSize"] = 14;
            await File.WriteAllTextAsync(settingsPath, node1.ToJsonString());

            var newMcpUrl = "http://localhost:9999/mcp";
            await service.ExportVsCodeSettingsAsync(tempDir, newMcpUrl);

            var content2 = await File.ReadAllTextAsync(settingsPath);
            var node2 = JsonNode.Parse(content2);
            Assert.Equal(14, node2?["editor.fontSize"]?.GetValue<int>());
            var servers2 = node2?["github.copilot.chat.mcp.servers"] as JsonArray;
            Assert.NotNull(servers2);
            Assert.Single(servers2);
            Assert.Equal(newMcpUrl, servers2[0]?["url"]?.ToString());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* cleanup */ }
        }
    }

    [Fact]
    public async Task ExportClaudeDesktopConfig_Creates_Correct_Json()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = new HostIntegrationService();
            var mcpUrl = "http://localhost:5000/mcp";

            // Act
            await service.ExportClaudeDesktopConfigAsync(tempDir, mcpUrl);

            // Assert
            var configPath = Path.Combine(tempDir, ".wheelhouse", "claude_desktop_config.json");
            Assert.True(File.Exists(configPath));
            var content = await File.ReadAllTextAsync(configPath);
            var node = JsonNode.Parse(content);
            Assert.Equal(mcpUrl, node?["mcpServers"]?["wheelhouse"]?["url"]?.ToString());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* cleanup */ }
        }
    }

    [Fact]
    public async Task ExportGitHubActionsWorkflow_Writes_Yaml_File()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = new HostIntegrationService();

            // Act
            await service.ExportGitHubActionsWorkflowAsync(tempDir, "dotnet test --filter Category=Unit");

            // Assert
            var workflowPath = Path.Combine(tempDir, ".github", "workflows", "wheelhouse-verify.yml");
            Assert.True(File.Exists(workflowPath));
            var content = await File.ReadAllTextAsync(workflowPath);
            Assert.Contains("run: dotnet test --filter Category=Unit", content);
            Assert.Contains("uses: actions/checkout@v4", content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* cleanup */ }
        }
    }
}

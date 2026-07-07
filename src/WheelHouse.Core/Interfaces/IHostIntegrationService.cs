namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Service to export MCP configurations and verification pipelines to VSCode, Claude Desktop, and GitHub Actions.
/// </summary>
public interface IHostIntegrationService
{
    Task ExportVsCodeSettingsAsync(string repositoryPath, string mcpUrl, CancellationToken cancellationToken = default);
    Task ExportClaudeDesktopConfigAsync(string repositoryPath, string mcpUrl, CancellationToken cancellationToken = default);
    Task ExportGitHubActionsWorkflowAsync(string repositoryPath, string testCommand, CancellationToken cancellationToken = default);
}

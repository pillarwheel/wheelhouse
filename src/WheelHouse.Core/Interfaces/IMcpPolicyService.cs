using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

public record SecurityWarning(string Severity, string RulePattern, string Message, string Recommendation);

/// <summary>
/// Service managing workspace tool-security policies (mcp-policy.json) and static command rules audits.
/// </summary>
public interface IMcpPolicyService
{
    /// <summary>
    /// Loads the workspace security policy from the repository. Creates a default file if missing.
    /// </summary>
    Task<McpPolicy> LoadPolicyAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the workspace security policy to the repository.
    /// </summary>
    Task SavePolicyAsync(string repositoryPath, McpPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Statically scans a list of command auto-approval rules and reports security vulnerabilities.
    /// </summary>
    List<SecurityWarning> AuditRules(IEnumerable<CommandRule> rules);
}

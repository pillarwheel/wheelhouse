using System.Text.Json;
using WheelHouse.Core;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IMcpPolicyService"/> to manage workspace mcp-policy.json and
/// statically scan workspace command auto-approval rules.
/// </summary>
public class McpPolicyService : IMcpPolicyService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<McpPolicy> LoadPolicyAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return new McpPolicy();
        }

        var dir = Path.Combine(repositoryPath, ".wheelhouse");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "mcp-policy.json");
        if (!File.Exists(path))
        {
            var defaultPolicy = new McpPolicy();
            var json = JsonSerializer.Serialize(defaultPolicy, _jsonOpts);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            return defaultPolicy;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<McpPolicy>(json, _jsonOpts) ?? new McpPolicy();
        }
        catch
        {
            return new McpPolicy();
        }
    }

    public async Task SavePolicyAsync(string repositoryPath, McpPolicy policy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return;
        }

        var dir = Path.Combine(repositoryPath, ".wheelhouse");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "mcp-policy.json");
        var json = JsonSerializer.Serialize(policy, _jsonOpts);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public List<SecurityWarning> AuditRules(IEnumerable<CommandRule> rules)
    {
        var warnings = new List<SecurityWarning>();

        foreach (var rule in rules)
        {
            if (rule.Action != RuleAction.AutoApprove) continue;

            var pattern = rule.Pattern.Trim().ToLowerInvariant();

            // Check 1: Wildcard / Global Allowances
            if (pattern == "*" || pattern == ".*" || string.IsNullOrWhiteSpace(pattern))
            {
                warnings.Add(new SecurityWarning(
                    "Critical",
                    rule.Pattern,
                    "Global wildcard auto-approval allows the agent to execute any shell command without verification.",
                    "Change this rule to 'Prompt' or specify a precise prefix (e.g. 'git status')."
                ));
            }
            else if (pattern == "bash" || pattern == "cmd" || pattern == "powershell" || pattern == "pwsh" || pattern == "sh")
            {
                warnings.Add(new SecurityWarning(
                    "Critical",
                    rule.Pattern,
                    "Auto-approving raw shell executors enables the agent to run arbitrary commands.",
                    "Remove this rule and specify only safe, specific CLI tools."
                ));
            }

            // Check 2: Destructive commands
            if (ContainsWord(pattern, "rm") || ContainsWord(pattern, "del") || ContainsWord(pattern, "erase") || 
                ContainsWord(pattern, "drop") || ContainsWord(pattern, "delete") || ContainsWord(pattern, "rmdir"))
            {
                warnings.Add(new SecurityWarning(
                    "High",
                    rule.Pattern,
                    "Rule auto-approves command patterns that delete files or databases.",
                    "Change this rule to 'Prompt' to ensure destructive commands are verified by a human."
                ));
            }

            // Check 3: Package installers & remote code downloaders
            if (pattern.StartsWith("curl") || pattern.StartsWith("wget") || pattern.Contains("curl ") || pattern.Contains("wget "))
            {
                warnings.Add(new SecurityWarning(
                    "High",
                    rule.Pattern,
                    "Rule auto-approves network fetching tools (curl/wget), which could download external untrusted scripts.",
                    "Change this rule to 'Prompt' to secure network executions."
                ));
            }
            else if (pattern.StartsWith("npm install") || pattern.StartsWith("pip install") || pattern.StartsWith("dotnet add") || 
                     pattern.StartsWith("yarn add") || pattern.StartsWith("pnpm add") || pattern.StartsWith("apt") || 
                     pattern.StartsWith("yum") || pattern.StartsWith("choco"))
            {
                warnings.Add(new SecurityWarning(
                    "High",
                    rule.Pattern,
                    "Rule auto-approves package installers, exposing the environment to supply chain attacks or arbitrary dependencies.",
                    "Change this rule to 'Prompt' to gate package installations."
                ));
            }

            // Check 4: Unsafe Git Operations
            if (pattern.StartsWith("git push") || pattern.Contains(" push"))
            {
                warnings.Add(new SecurityWarning(
                    "Medium",
                    rule.Pattern,
                    "Rule auto-approves git push, allowing the agent to publish changes directly to remote repositories.",
                    "Change this rule to 'Prompt' to prevent unintended code publishing."
                ));
            }
            else if (pattern.Contains("reset --hard") || pattern.Contains("clean -f"))
            {
                warnings.Add(new SecurityWarning(
                    "Medium",
                    rule.Pattern,
                    "Rule auto-approves hard git resets or clean actions, which could discard local untracked work permanently.",
                    "Change this rule to 'Prompt' or 'AutoDeny' to protect local state."
                ));
            }
        }

        return warnings;
    }

    private static bool ContainsWord(string source, string word)
    {
        if (source.Contains(word))
        {
            var idx = source.IndexOf(word);
            // Verify it is bounded by spaces or start/end of string
            bool leftBound = idx == 0 || source[idx - 1] == ' ' || source[idx - 1] == '/';
            bool rightBound = (idx + word.Length) == source.Length || source[idx + word.Length] == ' ';
            return leftBound && rightBound;
        }
        return false;
    }
}

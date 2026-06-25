using WheelHouse.Core.Models;

namespace WheelHouse.Core.Permissions;

/// <summary>
/// Translates a workspace's <see cref="CommandRule"/>s into Claude Code tool-permission
/// patterns. Auto-approve rules become <c>--allowedTools</c> entries (so Claude can run those
/// commands without prompting); auto-deny rules become <c>--disallowedTools</c> entries.
/// </summary>
public static class CommandRulePermissions
{
    public static (IReadOnlyList<string> Allowed, IReadOnlyList<string> Disallowed) Build(
        IEnumerable<CommandRule> rules)
    {
        var allowed = new List<string>();
        var disallowed = new List<string>();

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            var pattern = ToBashPattern(rule);

            switch (rule.Action)
            {
                case RuleAction.AutoApprove: allowed.Add(pattern); break;
                case RuleAction.AutoDeny: disallowed.Add(pattern); break;
                // RuleAction.Prompt → fall through to Claude's normal permission flow.
            }
        }

        return (allowed, disallowed);
    }

    /// <summary>
    /// Maps a rule to a Claude Bash tool pattern. Exact rules match a specific command;
    /// everything else is treated as a command prefix (<c>Bash(prefix:*)</c>).
    /// </summary>
    private static string ToBashPattern(CommandRule rule)
    {
        var p = rule.Pattern.Trim();
        return rule.MatchType == RuleMatchType.Exact
            ? $"Bash({p})"
            : $"Bash({p}:*)";
    }
}

using System.Text.RegularExpressions;

namespace WheelHouse.Core.Models;

/// <summary>
/// A rule that decides whether a command requested by an agent is auto-approved,
/// auto-denied, or escalated to the user. Example: prefix <c>git status</c> → AutoApprove.
/// </summary>
public class CommandRule
{
    public int Id { get; set; }

    /// <summary>The pattern to test the command against (interpreted per <see cref="MatchType"/>).</summary>
    public string Pattern { get; set; } = string.Empty;

    public RuleMatchType MatchType { get; set; } = RuleMatchType.Prefix;

    public RuleAction Action { get; set; } = RuleAction.Prompt;

    /// <summary>Optional human description of why this rule exists.</summary>
    public string? Notes { get; set; }

    /// <summary>Null = global rule applying to every workspace.</summary>
    public int? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    /// <summary>Returns true when <paramref name="command"/> matches this rule.</summary>
    public bool Matches(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(Pattern))
            return false;

        var cmd = command.Trim();
        return MatchType switch
        {
            RuleMatchType.Prefix => cmd.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Exact => string.Equals(cmd, Pattern, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Contains => cmd.Contains(Pattern, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Regex => SafeRegexMatch(cmd, Pattern),
            _ => false
        };
    }

    private static bool SafeRegexMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
        }
        catch (Exception)
        {
            return false;
        }
    }
}

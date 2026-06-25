using WheelHouse.Core;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Permissions;
using WheelHouse.Infrastructure.Agents;
using Xunit;

namespace WheelHouse.Tests;

public class CommandRulePermissionsTests
{
    [Fact]
    public void Prefix_Approve_Becomes_AllowedTool_With_Wildcard()
    {
        var rules = new[]
        {
            new CommandRule { Pattern = "dotnet test", MatchType = RuleMatchType.Prefix, Action = RuleAction.AutoApprove }
        };
        var (allowed, disallowed) = CommandRulePermissions.Build(rules);
        Assert.Equal(new[] { "Bash(dotnet test:*)" }, allowed);
        Assert.Empty(disallowed);
    }

    [Fact]
    public void Exact_Approve_Has_No_Wildcard()
    {
        var rules = new[]
        {
            new CommandRule { Pattern = "git status", MatchType = RuleMatchType.Exact, Action = RuleAction.AutoApprove }
        };
        var (allowed, _) = CommandRulePermissions.Build(rules);
        Assert.Equal(new[] { "Bash(git status)" }, allowed);
    }

    [Fact]
    public void Deny_Goes_To_Disallowed_And_Prompt_Is_Ignored()
    {
        var rules = new[]
        {
            new CommandRule { Pattern = "rm -rf", MatchType = RuleMatchType.Prefix, Action = RuleAction.AutoDeny },
            new CommandRule { Pattern = "curl", MatchType = RuleMatchType.Prefix, Action = RuleAction.Prompt }
        };
        var (allowed, disallowed) = CommandRulePermissions.Build(rules);
        Assert.Empty(allowed);
        Assert.Equal(new[] { "Bash(rm -rf:*)" }, disallowed);
    }
}

public class ClaudeCommandPermissionTests
{
    [Fact]
    public void Emits_Permission_Mode_And_Tool_Flags()
    {
        var request = new AgentRunRequest(
            "do it", "C:/repo",
            PermissionMode: "acceptEdits",
            AllowedTools: new[] { "Bash(dotnet test:*)", "Edit" },
            DisallowedTools: new[] { "Bash(rm -rf:*)" });

        var args = ClaudeCommand.BuildAgentArgs(request);

        Assert.Contains("--permission-mode", args);
        Assert.Equal("acceptEdits", args[args.IndexOf("--permission-mode") + 1]);
        Assert.Equal("Bash(dotnet test:*),Edit", args[args.IndexOf("--allowedTools") + 1]);
        Assert.Equal("Bash(rm -rf:*)", args[args.IndexOf("--disallowedTools") + 1]);
    }

    [Fact]
    public void Omits_Flags_When_Not_Specified()
    {
        var args = ClaudeCommand.BuildAgentArgs(new AgentRunRequest("x", "C:/repo"));
        Assert.DoesNotContain("--permission-mode", args);
        Assert.DoesNotContain("--allowedTools", args);
        Assert.DoesNotContain("--disallowedTools", args);
    }
}

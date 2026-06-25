using WheelHouse.Core;
using WheelHouse.Core.Models;
using Xunit;

namespace WheelHouse.Tests;

public class CommandRuleTests
{
    [Theory]
    [InlineData("git status", "git status -s", true)]
    [InlineData("git status", "git push", false)]
    public void Prefix_Match(string pattern, string command, bool expected)
    {
        var rule = new CommandRule { Pattern = pattern, MatchType = RuleMatchType.Prefix };
        Assert.Equal(expected, rule.Matches(command));
    }

    [Fact]
    public void Exact_Match_Is_Case_Insensitive()
    {
        var rule = new CommandRule { Pattern = "dotnet build", MatchType = RuleMatchType.Exact };
        Assert.True(rule.Matches("DOTNET BUILD"));
        Assert.False(rule.Matches("dotnet build --no-restore"));
    }

    [Fact]
    public void Regex_Match_Works_And_Is_Safe()
    {
        var rule = new CommandRule { Pattern = @"^npm (install|ci)$", MatchType = RuleMatchType.Regex };
        Assert.True(rule.Matches("npm install"));
        Assert.False(rule.Matches("npm run build"));
    }

    [Fact]
    public void Invalid_Regex_Does_Not_Throw()
    {
        var rule = new CommandRule { Pattern = "([unclosed", MatchType = RuleMatchType.Regex };
        Assert.False(rule.Matches("anything"));
    }
}

using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Agents;
using Xunit;

namespace WheelHouse.Tests;

public class ClaudeCommandTests
{
    [Fact]
    public void BuildAgentArgs_Uses_Print_And_StreamJson()
    {
        var args = ClaudeCommand.BuildAgentArgs(new AgentRunRequest("do the thing", "C:/repo"));
        Assert.Equal(new[] { "-p", "do the thing", "--output-format", "stream-json", "--verbose" }, args);
    }

    [Fact]
    public void BuildAgentArgs_Appends_Resume_When_Present()
    {
        var args = ClaudeCommand.BuildAgentArgs(new AgentRunRequest("x", "C:/repo", ResumeSessionId: "sess-123"));
        Assert.Contains("--resume", args);
        Assert.Equal("sess-123", args[^1]);
    }

    [Fact]
    public void BuildHeadroomArgs_Wraps_Claude_With_Separator()
    {
        var agentArgs = ClaudeCommand.BuildAgentArgs(new AgentRunRequest("hello", "C:/repo"));
        var wrapped = ClaudeCommand.BuildHeadroomArgs(new[] { "--memory" }, agentArgs);

        Assert.Equal("wrap", wrapped[0]);
        Assert.Equal("claude", wrapped[1]);
        Assert.Equal("--memory", wrapped[2]);
        Assert.Equal("--", wrapped[3]);
        // everything after "--" is the verbatim claude invocation
        Assert.Equal(agentArgs, wrapped.Skip(4).ToList());
    }

    [Fact]
    public void BuildHeadroomArgs_Drops_Blank_Flags()
    {
        var wrapped = ClaudeCommand.BuildHeadroomArgs(new[] { "", "  " }, new[] { "-p", "x" });
        Assert.Equal(new[] { "wrap", "claude", "--", "-p", "x" }, wrapped);
    }
}

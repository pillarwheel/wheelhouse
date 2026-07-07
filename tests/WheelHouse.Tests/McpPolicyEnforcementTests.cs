using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Mcp;
using WheelHouse.Core.Models;
using Xunit;

namespace WheelHouse.Tests;

public class McpPolicyEnforcementTests
{
    [Fact]
    public void Default_Policy_Denies_Shell_And_Network_Tools()
    {
        var denied = McpPolicyEnforcement.DisallowedToolsFor(new McpPolicy());
        Assert.Equal(new[] { "Bash", "WebFetch", "WebSearch" }, denied);
    }

    [Fact]
    public void Permissive_Policy_Denies_Nothing()
    {
        var denied = McpPolicyEnforcement.DisallowedToolsFor(
            new McpPolicy { AllowShell = true, AllowNetwork = true });
        Assert.Empty(denied);
    }

    [Fact]
    public void Apply_Merges_Into_Existing_Denials_Without_Duplicates()
    {
        var request = new AgentRunRequest("x", "C:/repo",
            DisallowedTools: new[] { "bash", "SomethingElse" }); // "bash" ≈ "Bash"

        var applied = McpPolicyEnforcement.Apply(request, new McpPolicy());

        Assert.Equal(new[] { "bash", "SomethingElse", "WebFetch", "WebSearch" }, applied.DisallowedTools);
        Assert.Equal(request.Prompt, applied.Prompt); // rest of the request untouched
    }

    [Fact]
    public void Apply_Is_A_NoOp_For_A_Permissive_Policy()
    {
        var request = new AgentRunRequest("x", "C:/repo");
        var applied = McpPolicyEnforcement.Apply(
            request, new McpPolicy { AllowShell = true, AllowNetwork = true });
        Assert.Same(request, applied);
    }
}

public class McpCallGateTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    [Fact]
    public void Allows_Up_To_The_Budget_Then_Blocks()
    {
        var gate = new McpCallGate();
        var t0 = DateTimeOffset.UtcNow;

        Assert.True(gate.TryAcquire("repo", 2, Window, t0));
        Assert.True(gate.TryAcquire("repo", 2, Window, t0.AddSeconds(1)));
        Assert.False(gate.TryAcquire("repo", 2, Window, t0.AddSeconds(2)));
    }

    [Fact]
    public void Budget_Frees_Up_As_The_Window_Slides()
    {
        var gate = new McpCallGate();
        var t0 = DateTimeOffset.UtcNow;

        Assert.True(gate.TryAcquire("repo", 1, Window, t0));
        Assert.False(gate.TryAcquire("repo", 1, Window, t0.AddSeconds(30)));
        Assert.True(gate.TryAcquire("repo", 1, Window, t0.AddSeconds(61)));
    }

    [Fact]
    public void Zero_Or_Negative_Budget_Means_Unlimited()
    {
        var gate = new McpCallGate();
        var t0 = DateTimeOffset.UtcNow;
        for (var i = 0; i < 100; i++)
            Assert.True(gate.TryAcquire("repo", 0, Window, t0));
    }

    [Fact]
    public void Budgets_Are_Independent_Per_Key()
    {
        var gate = new McpCallGate();
        var t0 = DateTimeOffset.UtcNow;

        Assert.True(gate.TryAcquire("repo-a", 1, Window, t0));
        Assert.True(gate.TryAcquire("repo-b", 1, Window, t0));
        Assert.False(gate.TryAcquire("repo-a", 1, Window, t0));
    }
}

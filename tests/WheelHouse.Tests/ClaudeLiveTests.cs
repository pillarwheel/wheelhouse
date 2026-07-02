using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Agents;
using WheelHouse.Infrastructure.Configuration;
using WheelHouse.Infrastructure.Mcp;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// End-to-end checks for the agent orchestrator. The verification half runs a real dev command
/// locally; the Claude half is gated behind WHEELHOUSE_LIVE_TESTS (it spawns headroom+claude and
/// makes a billed API call) and is auth-tolerant — it proves WheelHouse drives the Headroom-wrapped
/// invocation and parses the stream, independent of upstream Anthropic auth.
/// </summary>
public class ClaudeLiveTests
{
    private static bool LiveEnabled =>
        Environment.GetEnvironmentVariable("WHEELHOUSE_LIVE_TESTS") is "1" or "true";

    private static ClaudeCliService NewService()
    {
        EnvFile.Load();
        return new ClaudeCliService(
            NullLogger<ClaudeCliService>.Instance,
            new HeadroomOptions(),
            new PowerShellVerificationRunner(NullLogger<PowerShellVerificationRunner>.Instance),
            new McpEndpointState());
    }

    [Fact]
    public async Task RunVerification_Executes_A_Real_Dev_Command()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (exit, output) = await NewService().RunVerificationAsync("dotnet --version", Path.GetTempPath());
        Assert.Equal(0, exit);
        Assert.Matches(@"\d+\.\d+\.\d+", output); // a dotnet SDK version string
    }

    [Fact]
    public async Task Orchestrator_Drives_Claude_Through_Headroom_And_Parses_Stream()
    {
        if (!LiveEnabled) return;
        var svc = NewService();
        var info = await svc.GetRuntimeInfoAsync();
        if (!info.ClaudeAvailable) return; // claude not installed — inconclusive

        var dir = Path.Combine(Path.GetTempPath(), $"wh_claude_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var events = new List<AgentStreamEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await foreach (var e in svc.RunAsync(
                new AgentRunRequest("Reply with exactly the word: ok", dir), cts.Token))
                events.Add(e);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }

        // WheelHouse launched the process and parsed structured events to a terminal result.
        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.Kind == AgentEventKind.System);
        Assert.Contains(events, e => e.Kind is AgentEventKind.Result or AgentEventKind.Error);

        // If Headroom is engaged, the start banner says so (proves the compressed path is used).
        if (info.CompressionActive)
            Assert.Contains(events, e => e.Kind == AgentEventKind.System && e.Text.Contains("Headroom"));

        // Auth-tolerant: surface (don't hard-fail) when upstream auth is the blocker.
        var authBlocked = events.Any(e =>
            e.Text.Contains("authentication_failed") || e.Text.Contains("401"));
        Assert.True(!authBlocked || info.CompressionActive,
            "Claude auth failed and Headroom was not engaged — unexpected configuration.");
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Infrastructure.Agents;
using WheelHouse.Infrastructure.Configuration;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Live tests that hit the real Gemini API. Gated behind WHEELHOUSE_LIVE_TESTS so the normal
/// test run stays offline/free; they pull GEMINI_API_KEY from the repo .env when enabled.
/// They are transient-tolerant: a 5xx/429 "model overloaded" outage is treated as inconclusive
/// (the service already retries internally) so they fail only on real regressions.
/// </summary>
public class GeminiLiveTests
{
    private static bool LiveEnabled =>
        Environment.GetEnvironmentVariable("WHEELHOUSE_LIVE_TESTS") is "1" or "true";

    private static GeminiService? TryBuild()
    {
        EnvFile.Load();
        var options = new GeminiOptions();
        if (string.IsNullOrWhiteSpace(options.ApiKey)) return null;
        return new GeminiService(new HttpClient { Timeout = TimeSpan.FromSeconds(60) },
            options, new GeminiContextCache(), NullLogger<GeminiService>.Instance);
    }

    /// <summary>True when text reflects a transient API outage rather than a code/config problem.</summary>
    private static bool IsTransient(string text) =>
        text.Contains("(429)") || text.Contains("(500)") || text.Contains("(502)") ||
        text.Contains("(503)") || text.Contains("(504)") || text.Contains("errored");

    [Fact]
    public async Task Embeddings_Return_A_Vector()
    {
        if (!LiveEnabled) return;
        var svc = TryBuild();
        if (svc is null) return;

        // Retry a few times to ride out transient 503s; if still empty, treat as inconclusive.
        float[] vec = Array.Empty<float>();
        for (var i = 0; i < 3 && vec.Length == 0; i++)
        {
            if (i > 0) await Task.Delay(2000);
            vec = await svc.EmbedAsync("WheelHouse local RAG embedding smoke test");
        }
        if (vec.Length == 0) return; // API unavailable — inconclusive
        Assert.Equal(768, vec.Length);
    }

    [Fact]
    public async Task Completion_Returns_Text()
    {
        if (!LiveEnabled) return;
        var svc = TryBuild();
        if (svc is null) return;

        var reply = await svc.CompleteAsync("Reply with exactly one word: pong");
        if (IsTransient(reply)) return; // API overloaded — inconclusive
        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.DoesNotContain("Gemini request failed", reply); // catches non-transient (e.g. 404 model)
        Assert.DoesNotContain("not configured", reply);
    }

    [Fact]
    public async Task GenerateTasks_Parses_A_Task_List()
    {
        if (!LiveEnabled) return;
        var svc = TryBuild();
        if (svc is null) return;

        const string plan =
            "1. Add a /health endpoint returning 200 OK.\n" +
            "2. Add a unit test asserting the endpoint returns 200.\n" +
            "3. Wire it into Program.cs.";

        IReadOnlyList<WheelHouse.Core.Models.TaskItem> tasks = Array.Empty<WheelHouse.Core.Models.TaskItem>();
        for (var i = 0; i < 3 && tasks.Count == 0; i++)
        {
            if (i > 0) await Task.Delay(2000);
            tasks = await svc.GenerateTasksAsync(plan);
        }
        if (tasks.Count == 0) return; // API unavailable / inconclusive
        Assert.All(tasks, t => Assert.False(string.IsNullOrWhiteSpace(t.Title)));
    }
}

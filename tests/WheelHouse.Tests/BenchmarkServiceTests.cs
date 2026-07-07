using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class BenchmarkServiceTests
{
    /// <summary>Simulation-path service: the real-mode dependencies are never touched.</summary>
    internal static BenchmarkService NewService() => new(
        new UnusedGemini(),
        new UnusedVerifier(),
        new ServiceCollection().BuildServiceProvider(),
        NullLogger<BenchmarkService>.Instance);

    private sealed class UnusedGemini : IGeminiService
    {
        public bool IsConfigured => false;
        public Task<string> CompleteAsync(string p, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GenerateResearchPlanAsync(string g, string r, CancellationToken ct = default) => Task.FromResult("");
        public Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(string p, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<TaskItem>)new List<TaskItem>());
        public Task<string> TroubleshootAsync(string c, string o, string r, CancellationToken ct = default) => Task.FromResult("");
        public Task<float[]> EmbedAsync(string t, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
    }

    private sealed class UnusedVerifier : IVerificationRunner
    {
        public Task<VerificationResult> RunAsync(string c, string wd, TimeSpan? t = null, CancellationToken ct = default)
            => Task.FromResult(new VerificationResult(0, "", false));
    }

    [Fact]
    public void GetBuiltInChallenges_Returns_All_Default_Challenges()
    {
        // Arrange
        var service = NewService();

        // Act
        var challenges = service.GetBuiltInChallenges();

        // Assert
        Assert.Equal(5, challenges.Count);
        Assert.Contains(challenges, c => c.Id == "FIB");
        Assert.Contains(challenges, c => c.Id == "YML");
        Assert.Contains(challenges, c => c.Id == "COR");
        Assert.Contains(challenges, c => c.Id == "BTR");
        Assert.Contains(challenges, c => c.Id == "JSN");
    }

    [Fact]
    public async Task RunBenchmark_Calculates_Cascade_Report_Accurately()
    {
        // Arrange
        var service = NewService();

        // Act
        var report = await service.RunBenchmarkAsync("Cascade", simulate: true);

        // Assert
        Assert.Equal("Cascade", report.ConfigName);
        Assert.Equal(5, report.Total);
        Assert.True(report.Solved >= 4); // Cascade resolves simple + fallback complex
        Assert.True(report.SolveRate >= 0.8);
        Assert.True(report.AvgDurationMs > 0.0);
        Assert.True(report.TotalCost > 0.0);
        Assert.Equal(5, report.Results.Count);
    }

    [Fact]
    public async Task RunBenchmark_GeminiOnly_Has_Lower_SolveRate_Than_Cascade()
    {
        // Arrange
        var service = NewService();

        // Act
        var geminiReport = await service.RunBenchmarkAsync("GeminiOnly", simulate: true);
        var cascadeReport = await service.RunBenchmarkAsync("Cascade", simulate: true);

        // Assert: Gemini only resolves FIB and YML (2 out of 5), while Cascade resolves more.
        Assert.Equal(2, geminiReport.Solved);
        Assert.Equal(0.4, geminiReport.SolveRate);
        Assert.True(cascadeReport.Solved > geminiReport.Solved);
    }
}

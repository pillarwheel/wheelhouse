using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

public record ChallengeResult(
    string ChallengeId,
    string Title,
    bool Passed,
    double DurationMs,
    double Cost,
    string Logs);

public record BenchmarkReport(
    DateTime RunAt,
    string ConfigName,
    int Total,
    int Solved,
    double SolveRate,
    double AvgDurationMs,
    double TotalCost,
    List<ChallengeResult> Results);

/// <summary>
/// Service to manage and execute coding benchmarks (DRACO mode) under varying model configurations.
/// </summary>
public interface IBenchmarkService
{
    IReadOnlyList<BenchmarkChallenge> GetBuiltInChallenges();

    /// <param name="configName">Cascade, ClaudeOnly, or GeminiOnly (GeminiOnly is simulation-only).</param>
    /// <param name="simulate">True returns modeled numbers instantly; false runs the real plan→execute→verify pipeline per challenge in a temp sandbox (slow, uses live agents).</param>
    /// <param name="genome">Optional harness genome applied to the planning stage — this is what lets Darwin evaluate mutations against real outcomes.</param>
    Task<BenchmarkReport> RunBenchmarkAsync(
        string configName,
        bool simulate = true,
        HarnessGenome? genome = null,
        CancellationToken cancellationToken = default);
}

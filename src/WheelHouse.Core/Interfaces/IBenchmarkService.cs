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
    Task<BenchmarkReport> RunBenchmarkAsync(string configName, bool simulate = true, CancellationToken cancellationToken = default);
}

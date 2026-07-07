namespace WheelHouse.Core.Models;

/// <summary>
/// Represents a structured coding challenge used to evaluate agent and planning solve rates in a
/// sandbox. <see cref="SeedFiles"/> are materialized into the sandbox before the agent runs;
/// <see cref="VerificationCommand"/> must exit 0 only when the task was genuinely solved.
/// </summary>
public class BenchmarkChallenge
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>The task statement handed to the planning/execution pipeline.</summary>
    public string Description { get; set; } = string.Empty;

    public string VerificationCommand { get; set; } = string.Empty;

    /// <summary>Relative path → content written into the sandbox before the run.</summary>
    public Dictionary<string, string> SeedFiles { get; set; } = new();
}

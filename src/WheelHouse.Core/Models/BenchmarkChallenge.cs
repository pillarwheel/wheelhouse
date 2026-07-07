namespace WheelHouse.Core.Models;

/// <summary>
/// Represents a structured coding challenge used to evaluate agent and planning solve rates in a sandbox.
/// </summary>
public class BenchmarkChallenge
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VerificationCommand { get; set; } = string.Empty;
}

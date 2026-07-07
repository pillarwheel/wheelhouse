using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

public record EvolutionGenerationResult(
    int Generation,
    HarnessGenome Genome,
    double Score,
    int TasksPassed,
    int TasksTotal,
    bool IsImprovement);

/// <summary>
/// Service managing evolutionary harness optimization (Darwin Mode).
/// </summary>
public interface IDarwinService
{
    /// <summary>
    /// Loads the active genome configuration. Creates a default file if missing.
    /// </summary>
    Task<HarnessGenome> LoadGenomeAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the genome configuration.
    /// </summary>
    Task SaveGenomeAsync(string repositoryPath, HarnessGenome genome, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a single evolutionary generation (mutates, evaluates in sandbox, and selects if better).
    /// </summary>
    Task<EvolutionGenerationResult> EvolveGenerationAsync(string repositoryPath, int generation, bool simulate = true, CancellationToken cancellationToken = default);
}

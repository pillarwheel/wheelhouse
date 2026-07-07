using System.Text.Json;
using WheelHouse.Core;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IDarwinService"/> to manage evolutionary genome optimization.
/// Fitness comes in two tiers: a fast rule-based simulation (default), and — with
/// <c>simulate=false</c> — real benchmark runs of baseline vs. mutant through the actual
/// plan→execute→verify pipeline, which is what makes evolution optimize real outcomes.
/// </summary>
public class DarwinService : IDarwinService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Random _rand = new();

    private readonly IBenchmarkService _benchmark;

    public DarwinService(IBenchmarkService benchmark) => _benchmark = benchmark;

    public async Task<HarnessGenome> LoadGenomeAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return new HarnessGenome();
        }

        var dir = Path.Combine(repositoryPath, ".wheelhouse");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "genome.json");
        if (!File.Exists(path))
        {
            var defaultGenome = new HarnessGenome();
            var json = JsonSerializer.Serialize(defaultGenome, _jsonOpts);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            return defaultGenome;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<HarnessGenome>(json, _jsonOpts) ?? new HarnessGenome();
        }
        catch
        {
            return new HarnessGenome();
        }
    }

    public async Task SaveGenomeAsync(string repositoryPath, HarnessGenome genome, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Directory.Exists(repositoryPath))
        {
            return;
        }

        var dir = Path.Combine(repositoryPath, ".wheelhouse");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "genome.json");
        var json = JsonSerializer.Serialize(genome, _jsonOpts);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<EvolutionGenerationResult> EvolveGenerationAsync(
        string repositoryPath,
        int generation,
        bool simulate = true,
        CancellationToken cancellationToken = default)
    {
        var baseline = await LoadGenomeAsync(repositoryPath, cancellationToken);

        // Mutate the genome
        var mutated = CloneAndMutate(baseline);

        if (!simulate)
            return await EvolveAgainstRealBenchmarkAsync(
                repositoryPath, generation, baseline, mutated, cancellationToken);

        // Fast rule-based evaluation (deterministic-ish, offline).
        var (baselineScore, baselinePassed) = Evaluate(baseline, simulate);
        var (mutatedScore, mutatedPassed) = Evaluate(mutated, simulate);

        bool isImprovement = mutatedScore > baselineScore;

        if (isImprovement)
        {
            await SaveGenomeAsync(repositoryPath, mutated, cancellationToken);
            return new EvolutionGenerationResult(generation, mutated, mutatedScore, mutatedPassed, 3, true);
        }
        else
        {
            return new EvolutionGenerationResult(generation, baseline, baselineScore, baselinePassed, 3, false);
        }
    }

    /// <summary>
    /// Real fitness: run the benchmark suite (Cascade pipeline, live agents) once with the
    /// baseline genome and once with the mutant. The mutant wins on a strictly better solve
    /// rate, or the same solve rate at lower cost; only a winning mutant is persisted.
    /// </summary>
    private async Task<EvolutionGenerationResult> EvolveAgainstRealBenchmarkAsync(
        string repositoryPath, int generation, HarnessGenome baseline, HarnessGenome mutated,
        CancellationToken cancellationToken)
    {
        var baselineReport = await _benchmark.RunBenchmarkAsync(
            "Cascade", simulate: false, baseline, cancellationToken);
        var mutatedReport = await _benchmark.RunBenchmarkAsync(
            "Cascade", simulate: false, mutated, cancellationToken);

        var isImprovement =
            mutatedReport.SolveRate > baselineReport.SolveRate ||
            (mutatedReport.SolveRate == baselineReport.SolveRate &&
             mutatedReport.TotalCost < baselineReport.TotalCost);

        var winner = isImprovement ? mutated : baseline;
        var report = isImprovement ? mutatedReport : baselineReport;

        if (isImprovement)
            await SaveGenomeAsync(repositoryPath, mutated, cancellationToken);

        return new EvolutionGenerationResult(
            generation, winner, report.SolveRate * 100.0, report.Solved, report.Total, isImprovement);
    }

    private static HarnessGenome CloneAndMutate(HarnessGenome source)
    {
        var copy = new HarnessGenome
        {
            GeminiSystemPreamble = source.GeminiSystemPreamble,
            RagTopN = source.RagTopN,
            KeywordWeight = source.KeywordWeight,
            PlanningPromptTemplate = source.PlanningPromptTemplate
        };

        var parameterToMutate = _rand.Next(0, 4);
        switch (parameterToMutate)
        {
            case 0:
                // Mutate RagTopN (+/- 1, range 3-10)
                copy.RagTopN = Math.Clamp(source.RagTopN + _rand.Next(-1, 2), 3, 10);
                if (copy.RagTopN == source.RagTopN) copy.RagTopN = source.RagTopN == 10 ? 9 : source.RagTopN + 1;
                break;
            case 1:
                // Mutate KeywordWeight (+/- 0.1, range 0.1-0.9)
                copy.KeywordWeight = Math.Clamp(source.KeywordWeight + (_rand.NextDouble() * 0.2 - 0.1), 0.1, 0.9);
                break;
            case 2:
                // Mutate system preamble wording slightly
                if (source.GeminiSystemPreamble.Contains("computer-based work"))
                    copy.GeminiSystemPreamble = source.GeminiSystemPreamble.Replace("computer-based work", "autonomous workspace coordination");
                else
                    copy.GeminiSystemPreamble = source.GeminiSystemPreamble + " Focus on closing the loop.";
                break;
            case 3:
                // Mutate planning template wording
                if (source.PlanningPromptTemplate.Contains("concise, build-ready"))
                    copy.PlanningPromptTemplate = source.PlanningPromptTemplate.Replace("concise, build-ready", "precise and verifiable");
                else
                    copy.PlanningPromptTemplate = source.PlanningPromptTemplate + " Output clean steps.";
                break;
        }

        return copy;
    }

    private static (double Score, int Passed) Evaluate(HarnessGenome genome, bool simulate)
    {
        // For fast, offline verification and deterministic results, we evaluate via rules
        double score = 0.0;

        // Peak performance is between TopN 5-8
        if (genome.RagTopN >= 5 && genome.RagTopN <= 8) score += 35.0;
        else score += 15.0;

        // Peak keyword weight is between 0.4 and 0.6
        if (genome.KeywordWeight >= 0.4 && genome.KeywordWeight <= 0.6) score += 25.0;
        else score += 10.0;

        // Custom preamble rewards
        if (genome.GeminiSystemPreamble.Contains("operating system") || genome.GeminiSystemPreamble.Contains("autonomous workspace")) score += 20.0;
        else score += 10.0;

        if (genome.PlanningPromptTemplate.Contains("verifiable")) score += 20.0;
        else score += 10.0;

        // Add small simulation noise
        score += (_rand.NextDouble() * 2.0 - 1.0);

        // Convert score to mock tasks passed (out of 3)
        int passed = (int)(score / 28.0);
        passed = Math.Clamp(passed, 0, 3);

        return (score, passed);
    }
}

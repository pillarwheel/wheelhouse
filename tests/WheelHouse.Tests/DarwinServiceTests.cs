using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class DarwinServiceTests
{
    /// <summary>The simulate paths must never touch the benchmark service.</summary>
    private sealed class ThrowingBenchmark : IBenchmarkService
    {
        public IReadOnlyList<BenchmarkChallenge> GetBuiltInChallenges() => Array.Empty<BenchmarkChallenge>();
        public Task<BenchmarkReport> RunBenchmarkAsync(
            string configName, bool simulate = true, HarnessGenome? genome = null, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated evolution must not run benchmarks");
    }

    private static DarwinService NewService() => new(new ThrowingBenchmark());

    [Fact]
    public async Task Loads_Default_Genome_If_Missing_And_Saves_Changes()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = NewService();

            // Act: Load missing genome
            var genome = await service.LoadGenomeAsync(tempDir);

            // Assert: Verify defaults
            Assert.Equal(5, genome.RagTopN);
            Assert.Equal(0.5, genome.KeywordWeight);
            Assert.Contains("principal architect", genome.GeminiSystemPreamble);

            var genomePath = Path.Combine(tempDir, ".wheelhouse", "genome.json");
            Assert.True(File.Exists(genomePath));

            // Act: Save updated genome
            genome.RagTopN = 8;
            genome.KeywordWeight = 0.7;
            await service.SaveGenomeAsync(tempDir, genome);

            // Act: Load again
            var reloaded = await service.LoadGenomeAsync(tempDir);

            // Assert: Verify changes saved
            Assert.Equal(8, reloaded.RagTopN);
            Assert.Equal(0.7, reloaded.KeywordWeight);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* cleanup */ }
        }
    }

    [Fact]
    public async Task EvolveGeneration_Mutates_And_Runs_Simulated_Evaluation()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = NewService();

            // Initial baseline load (saves defaults)
            var baseline = await service.LoadGenomeAsync(tempDir);

            // Act: Run evolution generation
            var result = await service.EvolveGenerationAsync(tempDir, 1, simulate: true);

            // Assert: Result contains a valid genome and score
            Assert.Equal(1, result.Generation);
            Assert.NotNull(result.Genome);
            Assert.True(result.Score >= 0.0);
            Assert.True(result.TasksTotal == 3);

            // Verify if improvement occurred, the genome saved is the new one
            var saved = await service.LoadGenomeAsync(tempDir);
            if (result.IsImprovement)
            {
                Assert.Equal(result.Genome.RagTopN, saved.RagTopN);
                Assert.Equal(result.Genome.KeywordWeight, saved.KeywordWeight);
                Assert.Equal(result.Genome.GeminiSystemPreamble, saved.GeminiSystemPreamble);
                Assert.Equal(result.Genome.PlanningPromptTemplate, saved.PlanningPromptTemplate);
            }
            else
            {
                Assert.Equal(baseline.RagTopN, saved.RagTopN);
                Assert.Equal(baseline.KeywordWeight, saved.KeywordWeight);
                Assert.Equal(baseline.GeminiSystemPreamble, saved.GeminiSystemPreamble);
                Assert.Equal(baseline.PlanningPromptTemplate, saved.PlanningPromptTemplate);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* cleanup */ }
        }
    }
}

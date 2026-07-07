using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Proves the real benchmark pipeline offline via fakes: sandboxes are seeded and cleaned up,
/// the right keyed orchestrator runs, the genome reaches the planning stage, cost aggregates
/// from usage events, and Darwin's real fitness selects and persists the winning genome.
/// </summary>
public class BenchmarkRealModeTests
{
    // ----- Fakes -----

    private sealed class FakeOrchestrator : ITaskOrchestrationService
    {
        public readonly List<string> WorkingDirs = new();
        public bool SolveChallenges = true;   // writes a marker the verifier checks
        public double CostPerRun = 0.01;

        public async IAsyncEnumerable<AgentStreamEvent> RunAsync(
            AgentRunRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            WorkingDirs.Add(request.WorkingDirectory);
            if (SolveChallenges)
                await File.WriteAllTextAsync(Path.Combine(request.WorkingDirectory, "agent-ran.txt"), "ok", ct);
            yield return new AgentStreamEvent(AgentEventKind.Result, "(done)",
                Usage: new AgentUsage(100, 50, 1000, CostPerRun));
        }

        public Task<(int ExitCode, string Output)> RunVerificationAsync(string c, string wd, CancellationToken ct = default)
            => Task.FromResult((0, ""));
    }

    /// <summary>Passes iff the fake agent left its marker in the sandbox.</summary>
    private sealed class MarkerVerifier : IVerificationRunner
    {
        public readonly List<string> SeenSeedFiles = new();
        public Task<VerificationResult> RunAsync(string command, string workingDirectory, TimeSpan? t = null, CancellationToken ct = default)
        {
            foreach (var f in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories))
                SeenSeedFiles.Add(Path.GetFileName(f));
            var passed = File.Exists(Path.Combine(workingDirectory, "agent-ran.txt"));
            return Task.FromResult(new VerificationResult(passed ? 0 : 1, passed ? "ok" : "missing", false));
        }
    }

    private sealed class CapturingGemini : IGeminiService
    {
        public readonly List<string?> Preambles = new();
        public bool IsConfigured => true;
        public Task<string> CompleteAsync(string p, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GenerateResearchPlanAsync(string g, string r, CancellationToken ct = default)
            => GenerateResearchPlanAsync(g, r, null, null, ct);
        public Task<string> GenerateResearchPlanAsync(
            string g, string r, string? systemPreamble, string? planningTemplate, CancellationToken ct = default)
        {
            Preambles.Add(systemPreamble);
            return Task.FromResult("THE PLAN");
        }
        public Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(string p, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<TaskItem>)new List<TaskItem>());
        public Task<string> TroubleshootAsync(string c, string o, string r, CancellationToken ct = default) => Task.FromResult("");
        public Task<float[]> EmbedAsync(string t, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
    }

    private static (BenchmarkService Service, FakeOrchestrator Cascade, FakeOrchestrator Claude,
        MarkerVerifier Verifier, CapturingGemini Gemini) NewHarness()
    {
        var cascade = new FakeOrchestrator();
        var claude = new FakeOrchestrator();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITaskOrchestrationService>("Cascade", cascade);
        services.AddKeyedSingleton<ITaskOrchestrationService>("ClaudeCode", claude);
        var verifier = new MarkerVerifier();
        var gemini = new CapturingGemini();
        var service = new BenchmarkService(
            gemini, verifier, services.BuildServiceProvider(), NullLogger<BenchmarkService>.Instance);
        return (service, cascade, claude, verifier, gemini);
    }

    // ----- Tests -----

    [Fact]
    public async Task Real_Run_Executes_Every_Challenge_In_A_Seeded_Sandbox_And_Cleans_Up()
    {
        var (service, cascade, _, verifier, _) = NewHarness();

        var report = await service.RunBenchmarkAsync("Cascade", simulate: false);

        Assert.Equal(5, report.Total);
        Assert.Equal(5, report.Solved);
        Assert.Equal(1.0, report.SolveRate);
        Assert.Equal(5 * 0.01, report.TotalCost, precision: 6); // real cost from AgentUsage events

        // Each challenge got its own sandbox, since removed.
        Assert.Equal(5, cascade.WorkingDirs.Distinct().Count());
        Assert.All(cascade.WorkingDirs, d => Assert.False(Directory.Exists(d)));

        // Seed files were materialized before verification (e.g. BTR's tree.txt, JSN's data.json).
        Assert.Contains("tree.txt", verifier.SeenSeedFiles);
        Assert.Contains("data.json", verifier.SeenSeedFiles);
    }

    [Fact]
    public async Task Failed_Verification_Counts_As_Unsolved()
    {
        var (service, cascade, _, _, _) = NewHarness();
        cascade.SolveChallenges = false; // agent runs but never produces the marker

        var report = await service.RunBenchmarkAsync("Cascade", simulate: false);

        Assert.Equal(0, report.Solved);
        Assert.All(report.Results, r => Assert.Contains("[verify] exit 1", r.Logs));
    }

    [Fact]
    public async Task ClaudeOnly_Uses_The_ClaudeCode_Orchestrator()
    {
        var (service, cascade, claude, _, _) = NewHarness();

        await service.RunBenchmarkAsync("ClaudeOnly", simulate: false);

        Assert.Empty(cascade.WorkingDirs);
        Assert.Equal(5, claude.WorkingDirs.Count);
    }

    [Fact]
    public async Task GeminiOnly_Is_Simulation_Only()
    {
        var (service, _, _, _, _) = NewHarness();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunBenchmarkAsync("GeminiOnly", simulate: false));
    }

    [Fact]
    public async Task Genome_Reaches_The_Planning_Stage()
    {
        var (service, _, _, _, gemini) = NewHarness();
        var genome = new HarnessGenome { GeminiSystemPreamble = "MUTATED PREAMBLE" };

        await service.RunBenchmarkAsync("Cascade", simulate: false, genome);

        Assert.Equal(5, gemini.Preambles.Count);
        Assert.All(gemini.Preambles, p => Assert.Equal("MUTATED PREAMBLE", p));
    }

    [Fact]
    public async Task Darwin_Real_Fitness_Persists_The_Mutant_Only_When_It_Wins()
    {
        var repo = Path.Combine(Path.GetTempPath(), $"whdarwin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(repo);
        try
        {
            // Benchmark fake: second run (the mutant) scores higher than the first (baseline).
            var winner = new ScriptedBenchmark(baselineSolved: 2, mutantSolved: 4);
            var darwin = new DarwinService(winner);
            var baseline = await darwin.LoadGenomeAsync(repo);

            var result = await darwin.EvolveGenerationAsync(repo, 1, simulate: false);

            Assert.True(result.IsImprovement);
            Assert.Equal(4, result.TasksPassed);
            Assert.Equal(5, result.TasksTotal);
            Assert.Equal(winner.MutantGenome!.GeminiSystemPreamble,
                (await darwin.LoadGenomeAsync(repo)).GeminiSystemPreamble); // mutant persisted

            // And the reverse: a losing mutant is not persisted.
            var loser = new ScriptedBenchmark(baselineSolved: 4, mutantSolved: 2);
            var repo2 = Path.Combine(Path.GetTempPath(), $"whdarwin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(repo2);
            try
            {
                var darwin2 = new DarwinService(loser);
                var baseline2 = await darwin2.LoadGenomeAsync(repo2);
                var result2 = await darwin2.EvolveGenerationAsync(repo2, 1, simulate: false);

                Assert.False(result2.IsImprovement);
                var saved = await darwin2.LoadGenomeAsync(repo2);
                Assert.Equal(baseline2.GeminiSystemPreamble, saved.GeminiSystemPreamble);
                Assert.Equal(baseline2.RagTopN, saved.RagTopN);
            }
            finally { try { Directory.Delete(repo2, true); } catch { /* ignore */ } }
        }
        finally { try { Directory.Delete(repo, true); } catch { /* ignore */ } }
    }

    /// <summary>First real call = baseline, second = mutant; records the mutant genome.</summary>
    private sealed class ScriptedBenchmark : IBenchmarkService
    {
        private readonly int _baselineSolved, _mutantSolved;
        private int _calls;
        public HarnessGenome? MutantGenome;

        public ScriptedBenchmark(int baselineSolved, int mutantSolved)
            => (_baselineSolved, _mutantSolved) = (baselineSolved, mutantSolved);

        public IReadOnlyList<BenchmarkChallenge> GetBuiltInChallenges() => Array.Empty<BenchmarkChallenge>();

        public Task<BenchmarkReport> RunBenchmarkAsync(
            string configName, bool simulate = true, HarnessGenome? genome = null, CancellationToken ct = default)
        {
            Assert.False(simulate);
            _calls++;
            var solved = _calls == 1 ? _baselineSolved : _mutantSolved;
            if (_calls == 2) MutantGenome = genome;
            return Task.FromResult(new BenchmarkReport(
                DateTime.UtcNow, configName, 5, solved, solved / 5.0, 1000, 0.05, new List<ChallengeResult>()));
        }
    }
}

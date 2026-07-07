using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IBenchmarkService"/>. Two modes:
/// <list type="bullet">
/// <item><b>simulate=true</b> — instant, modeled numbers for exploring the dashboard.</item>
/// <item><b>simulate=false</b> — the real pipeline per challenge: seed a temp sandbox, plan with
/// Gemini (honoring an optional <see cref="HarnessGenome"/>), execute with the requested
/// orchestrator (Cascade / ClaudeOnly), then run the challenge's verification command. Slow and
/// uses live agents; this is the fitness function Darwin evolves against.</item>
/// </list>
/// Challenges are deliberately file-based with inline PowerShell checks, so verification needs
/// no package restore and exits 0 only when the task is genuinely solved.
/// </summary>
public class BenchmarkService : IBenchmarkService
{
    private const int LogCap = 400;

    private static readonly List<BenchmarkChallenge> Challenges = new()
    {
        new()
        {
            Id = "FIB",
            Title = "Math: Fibonacci Generator",
            Description =
                "Create a file named fib.txt in the repository root containing the first 12 Fibonacci " +
                "numbers starting 1, 1 — one number per line, no blank lines, no other text.",
            VerificationCommand =
                "if (((Get-Content fib.txt) -join ',') -eq '1,1,2,3,5,8,13,21,34,55,89,144') { exit 0 } else { exit 1 }"
        },
        new()
        {
            Id = "YML",
            Title = "GitOps: YAML Config Validator",
            Description =
                "The file .wheelhouse/config.yaml is malformed. Fix it so every line is a valid " +
                "'key: value' pair and it sets permissionMode to acceptEdits, keeping the existing " +
                "workspaceName value.",
            VerificationCommand =
                "$c = Get-Content .wheelhouse/config.yaml -Raw; " +
                "if (($c -match 'permissionMode:\\s*acceptEdits') -and ($c -match 'workspaceName:\\s*bench') -and ($c -notmatch 'permissionMode\\s*=')) { exit 0 } else { exit 1 }",
            SeedFiles = { [".wheelhouse/config.yaml"] = "workspaceName bench\npermissionMode = plan\n" }
        },
        new()
        {
            Id = "COR",
            Title = "Security: CORS Middleware",
            Description =
                "Create src/CorsPolicy.cs containing a public class CorsPolicy with a public const " +
                "string AllowedOrigin set to \"https://localhost\" and a public const int MaxAgeSeconds set to 600.",
            VerificationCommand =
                "$c = Get-Content src/CorsPolicy.cs -Raw; " +
                "if (($c -match 'class\\s+CorsPolicy') -and ($c -match 'https://localhost') -and ($c -match 'MaxAgeSeconds\\s*=\\s*600')) { exit 0 } else { exit 1 }"
        },
        new()
        {
            Id = "BTR",
            Title = "Data: Binary Tree Inverter",
            Description =
                "The file tree.txt contains a complete binary tree in level order: 1,2,3,4,5,6,7. " +
                "Create inverted.txt containing the level-order traversal of the mirrored tree, " +
                "as a single comma-separated line.",
            VerificationCommand =
                "if ((Get-Content inverted.txt -Raw).Trim() -eq '1,3,2,7,6,5,4') { exit 0 } else { exit 1 }",
            SeedFiles = { ["tree.txt"] = "1,2,3,4,5,6,7" }
        },
        new()
        {
            Id = "JSN",
            Title = "JSON: Tokenizer & Parser",
            Description =
                "The file data.json contains a JSON object. Create keys.txt listing its top-level " +
                "keys in alphabetical order, one per line.",
            VerificationCommand =
                "if (((Get-Content keys.txt) -join ',') -eq 'name,tags,version') { exit 0 } else { exit 1 }",
            SeedFiles = { ["data.json"] = """{"version": 1, "name": "wheelhouse", "tags": ["bench"]}""" }
        }
    };

    private readonly IGeminiService _gemini;
    private readonly IVerificationRunner _verifier;
    private readonly IServiceProvider _services;
    private readonly ILogger<BenchmarkService> _logger;

    public BenchmarkService(
        IGeminiService gemini,
        IVerificationRunner verifier,
        IServiceProvider services,
        ILogger<BenchmarkService> logger)
    {
        _gemini = gemini;
        _verifier = verifier;
        _services = services;
        _logger = logger;
    }

    public IReadOnlyList<BenchmarkChallenge> GetBuiltInChallenges() => Challenges;

    public Task<BenchmarkReport> RunBenchmarkAsync(
        string configName, bool simulate = true, HarnessGenome? genome = null,
        CancellationToken cancellationToken = default)
        => simulate
            ? RunSimulatedAsync(configName, cancellationToken)
            : RunRealAsync(configName, genome, cancellationToken);

    // ----- Real execution -----

    private async Task<BenchmarkReport> RunRealAsync(
        string configName, HarnessGenome? genome, CancellationToken cancellationToken)
    {
        var orchestratorKey = configName.ToLowerInvariant() switch
        {
            "cascade" => "Cascade",
            "claudeonly" => "ClaudeCode",
            _ => throw new InvalidOperationException(
                $"Config '{configName}' is simulation-only. Real runs support Cascade and ClaudeOnly.")
        };
        var orchestrator = _services.GetRequiredKeyedService<ITaskOrchestrationService>(orchestratorKey);

        var results = new List<ChallengeResult>();
        foreach (var challenge in Challenges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunRealChallengeAsync(orchestrator, challenge, genome, cancellationToken));
        }
        return Aggregate(configName, results);
    }

    private async Task<ChallengeResult> RunRealChallengeAsync(
        ITaskOrchestrationService orchestrator, BenchmarkChallenge challenge, HarnessGenome? genome,
        CancellationToken cancellationToken)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "wheelhouse-bench", $"{challenge.Id}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);

        var sw = Stopwatch.StartNew();
        var logs = new StringBuilder();
        var cost = 0.0;
        var passed = false;
        try
        {
            foreach (var (relative, content) in challenge.SeedFiles)
            {
                var path = Path.Combine(sandbox, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, cancellationToken);
            }

            // Plan with the (possibly mutated) genome — the stage Darwin's fitness must exercise.
            var context = "Sandbox repository files:\n" +
                          string.Join("\n", challenge.SeedFiles.Keys.DefaultIfEmpty("(repository is empty)"));
            var plan = await _gemini.GenerateResearchPlanAsync(
                challenge.Description, context,
                genome?.GeminiSystemPreamble, genome?.PlanningPromptTemplate, cancellationToken);
            logs.AppendLine($"[plan] {Truncate(plan)}");

            var request = new AgentRunRequest(
                $"{challenge.Description}\n\n## Plan\n{plan}",
                sandbox,
                PermissionMode: "acceptEdits",
                VerificationCommand: challenge.VerificationCommand);

            await foreach (var evt in orchestrator.RunAsync(request, cancellationToken))
            {
                if (evt.Usage?.CostUsd is { } c) cost += c;
                if (evt.Kind == AgentEventKind.Error) logs.AppendLine($"[agent-error] {Truncate(evt.Text)}");
            }

            var verify = await _verifier.RunAsync(
                challenge.VerificationCommand, sandbox, cancellationToken: cancellationToken);
            passed = verify.Succeeded;
            logs.AppendLine($"[verify] exit {verify.ExitCode}: {Truncate(verify.Output)}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logs.AppendLine($"[error] {ex.Message}");
            _logger.LogWarning(ex, "Benchmark challenge {Id} errored.", challenge.Id);
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { /* ignore */ }
        }

        return new ChallengeResult(
            challenge.Id, challenge.Title, passed, sw.ElapsedMilliseconds, cost, logs.ToString().TrimEnd());
    }

    private static BenchmarkReport Aggregate(string configName, List<ChallengeResult> results)
    {
        var total = results.Count;
        var solved = results.Count(r => r.Passed);
        return new BenchmarkReport(
            DateTime.UtcNow, configName, total, solved,
            total > 0 ? (double)solved / total : 0.0,
            results.Count > 0 ? results.Average(r => r.DurationMs) : 0.0,
            results.Sum(r => r.Cost),
            results);
    }

    private static string Truncate(string s, int max = LogCap)
        => s.Length <= max ? s : s[..max] + "…";

    // ----- Simulation (instant, modeled numbers for the dashboard) -----

    private async Task<BenchmarkReport> RunSimulatedAsync(string configName, CancellationToken cancellationToken)
    {
        var results = new List<ChallengeResult>();
        var rand = new Random();

        foreach (var challenge in Challenges)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Simulate run latency
            await Task.Delay(150, cancellationToken);

            bool passed;
            double durationMs;
            double cost;
            string logs;

            switch (configName.ToLowerInvariant())
            {
                case "geminionly":
                    // Resolves simple tasks (FIB, YML), fails complex ones
                    if (challenge.Id == "FIB" || challenge.Id == "YML")
                    {
                        passed = true;
                        durationMs = rand.Next(1200, 2000);
                        cost = 0.002;
                        logs = "[Gemini] Completed implementation. Tests passed.";
                    }
                    else
                    {
                        passed = false;
                        durationMs = rand.Next(2000, 3500);
                        cost = 0.004;
                        logs = "[Gemini] Run failed: Index out of range. Tests failed.";
                    }
                    break;

                case "claudeonly":
                    // Resolves almost all tasks, but high cost and medium latency
                    passed = challenge.Id != "JSN" || rand.Next(0, 10) > 2; // 80% pass on hard JSON task
                    durationMs = rand.Next(3000, 5000);
                    cost = passed ? 0.045 : 0.065;
                    logs = passed ? "[Claude] Resolving task dependencies. Changes verified." : "[Claude] Compilation failed: CS0103 name does not exist.";
                    break;

                case "cascade":
                default:
                    // Resolves simple tasks cheaply via Gemini, complex ones via Claude fallback.
                    if (challenge.Id == "FIB" || challenge.Id == "YML")
                    {
                        passed = true;
                        durationMs = rand.Next(1200, 2000); // Fast Gemini solve
                        cost = 0.002;
                        logs = "[Cascade] Gemini trial succeeded. No Claude escalation required.";
                    }
                    else
                    {
                        // Falls back to Claude
                        passed = challenge.Id != "JSN" || rand.Next(0, 10) > 1; // 90% pass on JSON with Claude fallback
                        durationMs = rand.Next(4000, 6500); // Longer due to fallback restore
                        cost = 0.048; // Gemini cost + Claude cost
                        logs = "[Cascade] Gemini trial failed verification. Discarding changes and escalating to Claude. Claude completed successfully.";
                    }
                    break;
            }

            results.Add(new ChallengeResult(challenge.Id, challenge.Title, passed, durationMs, cost, logs));
        }

        return Aggregate(configName, results);
    }
}

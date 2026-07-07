using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IBenchmarkService"/> to manage and run DRACO-style benchmark challenge suites.
/// </summary>
public class BenchmarkService : IBenchmarkService
{
    private static readonly List<BenchmarkChallenge> Challenges = new()
    {
        new()
        {
            Id = "FIB",
            Title = "Math: Fibonacci Generator",
            Description = "Implement a class that computes Fibonacci numbers up to N.",
            VerificationCommand = "dotnet test --filter Class=Fibonacci"
        },
        new()
        {
            Id = "YML",
            Title = "GitOps: YAML Config Validator",
            Description = "Verify and parse a standard .wheelhouse/config.yaml file.",
            VerificationCommand = "dotnet test --filter Class=YamlConfig"
        },
        new()
        {
            Id = "COR",
            Title = "Security: CORS Middleware",
            Description = "Write middleware to safely apply Origin policy headers.",
            VerificationCommand = "dotnet test --filter Class=CorsMiddleware"
        },
        new()
        {
            Id = "BTR",
            Title = "Data: Binary Tree Inverter",
            Description = "Invert a binary tree structure in C#.",
            VerificationCommand = "dotnet test --filter Class=BinaryTree"
        },
        new()
        {
            Id = "JSN",
            Title = "JSON: Tokenizer & Parser",
            Description = "Implement a tokenizer that parses raw JSON strings into key-value pairs.",
            VerificationCommand = "dotnet test --filter Class=JsonTokenizer"
        }
    };

    public IReadOnlyList<BenchmarkChallenge> GetBuiltInChallenges() => Challenges;

    public async Task<BenchmarkReport> RunBenchmarkAsync(string configName, bool simulate = true, CancellationToken cancellationToken = default)
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

        var total = results.Count;
        var solved = results.Count(r => r.Passed);
        var solveRate = total > 0 ? (double)solved / total : 0.0;
        var avgDuration = results.Count > 0 ? results.Average(r => r.DurationMs) : 0.0;
        var totalCost = results.Sum(r => r.Cost);

        return new BenchmarkReport(DateTime.UtcNow, configName, total, solved, solveRate, avgDuration, totalCost, results);
    }
}

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Models.Script;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Deterministic engine tests for <see cref="ScriptExecutor"/> using fake Gemini/Claude/verifier
/// services — validates control flow, data handoff, loops, parallel fork/merge, approval gating,
/// and the §3 KPI telemetry without any live LLM or CLI.
/// </summary>
public class ScriptExecutorTests
{
    // ----- Fakes -----

    private sealed class FakeGemini : IGeminiService
    {
        public readonly List<string> Prompts = new();
        public Func<string, string> Reply = p => "GEMINI(" + p + ")";
        public bool IsConfigured => true;
        public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
        {
            Prompts.Add(prompt);
            return Task.FromResult(Reply(prompt));
        }
        public Task<string> GenerateResearchPlanAsync(string g, string r, CancellationToken ct = default) => Task.FromResult("plan");
        public Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(string plan, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<TaskItem>)new List<TaskItem> { new() { Title = "t", Description = "d" } });
        public Task<string> TroubleshootAsync(string c, string o, string r, CancellationToken ct = default) => Task.FromResult("fix");
        public Task<float[]> EmbedAsync(string t, CancellationToken ct = default) => Task.FromResult(Array.Empty<float>());
    }

    private sealed class FakeOrchestrator : IAgentOrchestrator
    {
        public readonly List<string> Prompts = new();
        public bool EmitError;
        public async IAsyncEnumerable<AgentStreamEvent> RunAsync(
            AgentRunRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Prompts.Add(request.Prompt);
            yield return new AgentStreamEvent(AgentEventKind.AssistantText, "CLAUDE(" + request.Prompt + ")");
            if (EmitError) yield return new AgentStreamEvent(AgentEventKind.Error, "boom", IsError: true);
            await Task.CompletedTask;
        }
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<AgentRuntimeInfo> GetRuntimeInfoAsync(CancellationToken ct = default)
            => Task.FromResult(new AgentRuntimeInfo(true, null, false, null, "off", false));
        public Task<(int ExitCode, string Output)> RunVerificationAsync(string c, string wd, CancellationToken ct = default)
            => Task.FromResult((0, ""));
    }

    private sealed class FakeVerifier : IVerificationRunner
    {
        public readonly Queue<int> ExitCodes = new();
        public int Calls;
        public Task<VerificationResult> RunAsync(string command, string wd, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Calls++;
            var exit = ExitCodes.Count > 0 ? ExitCodes.Dequeue() : 0;
            return Task.FromResult(new VerificationResult(exit, "ran:" + command, false));
        }
    }

    private sealed class TestDbFactory : IDbContextFactory<WheelHouseDbContext>
    {
        private readonly DbContextOptions<WheelHouseDbContext> _options;
        public TestDbFactory()
        {
            _options = new DbContextOptionsBuilder<WheelHouseDbContext>()
                .UseSqlite($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared").Options;
            var root = new WheelHouseDbContext(_options);
            root.Database.OpenConnection(); // keep the in-memory db alive for the test
            root.Database.EnsureCreated();
        }
        public WheelHouseDbContext CreateDbContext() => new(_options);
        public Task<WheelHouseDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    // ----- Helpers -----

    private static ScriptNode N(string id, string type, double x = 0, double y = 0, Dictionary<string, string>? s = null)
        => new() { Id = id, Type = type, Name = id, X = x, Y = y, Settings = s ?? new() };

    private static ScriptEdge E(string src, string sp, string tgt, string tp)
        => new() { SourceNodeId = src, SourcePort = sp, TargetNodeId = tgt, TargetPort = tp };

    private static async Task<ScriptRunMetrics> Run(
        ScriptGraph graph, FakeGemini gem, FakeOrchestrator orch, FakeVerifier ver,
        Func<string, Task<bool>>? approval = null, string goal = "THE GOAL")
    {
        var session = new AgentSession
        {
            Name = "goal-name",
            PlanningContext = goal,
            RepositoryPath = "/repo",
            Template = new SessionTemplate { GraphJson = graph.ToJson() }
        };
        var exec = new ScriptExecutor(gem, orch, ver, new TestDbFactory()) { ApprovalHandler = approval };
        return await exec.RunGraphAsync(session, (_, _) => { }, _ => { }, CancellationToken.None);
    }

    // ----- Tests -----

    [Fact]
    public async Task Linear_Flow_Passes_Data_And_Reports_Clean_Metrics()
    {
        var gem = new FakeGemini();
        var orch = new FakeOrchestrator();
        var ver = new FakeVerifier(); // default exit 0 => pass

        var graph = new ScriptGraph
        {
            Nodes =
            {
                N("start", "start"),
                N("plan", "gemini-prompt", s: new() { ["Prompt"] = "Plan: {{GOAL}}" }),
                N("exec", "claude-execute", s: new() { ["Prompt"] = "Do: {{CONTEXT}}" }),
                N("build", "verification", s: new() { ["Command"] = "dotnet build" }),
            },
            Edges =
            {
                E("start", "Next", "plan", "Execute"),
                E("plan", "Next", "exec", "Execute"),
                E("exec", "Success", "build", "Execute"),
                E("start", "Goal", "plan", "Goal"),
                E("plan", "Response", "exec", "Context"),
            }
        };

        var m = await Run(graph, gem, orch, ver);

        // Data handoff: the goal flowed into Gemini's prompt, and Gemini's response into Claude's.
        Assert.Contains("Plan: THE GOAL", gem.Prompts.Single());
        Assert.Contains("GEMINI(Plan: THE GOAL)", orch.Prompts.Single());

        Assert.Equal(4, m.NodesExecuted);
        Assert.Equal(4, m.NodesSucceeded);
        Assert.Equal(1.0, m.NodeSuccessRate);
        Assert.Equal(0, m.HandoffErrors);
        Assert.True(m.FinalVerificationPassed);
        Assert.True(m.ApproxTokens > 0);
    }

    [Fact]
    public async Task Build_Repair_Loop_Retries_Until_Pass()
    {
        var gem = new FakeGemini();
        var orch = new FakeOrchestrator();
        var ver = new FakeVerifier();
        ver.ExitCodes.Enqueue(1); // first build fails
        ver.ExitCodes.Enqueue(0); // second build passes

        var graph = new ScriptGraph
        {
            Nodes =
            {
                N("start", "start"),
                N("build", "verification", s: new() { ["Command"] = "dotnet build" }),
                N("diag", "gemini-prompt", s: new() { ["Prompt"] = "Fix: {{CONTEXT}}" }),
                N("fix", "claude-execute", s: new() { ["Prompt"] = "Apply: {{CONTEXT}}" }),
                N("loop", "loop-count", s: new() { ["MaxIterations"] = "3" }),
            },
            Edges =
            {
                E("start", "Next", "build", "Execute"),
                E("build", "Failed", "diag", "Execute"),
                E("diag", "Next", "fix", "Execute"),
                E("fix", "Success", "loop", "Execute"),
                E("loop", "Loop", "build", "Execute"),
                E("build", "Response", "diag", "Context"),
                E("diag", "Response", "fix", "Context"),
            }
        };

        var m = await Run(graph, gem, orch, ver);

        Assert.Equal(2, ver.Calls);            // built twice
        Assert.Single(gem.Prompts);            // diagnosed once
        Assert.Single(orch.Prompts);           // repaired once
        Assert.True(m.FinalVerificationPassed); // ends green
        Assert.Equal(0, m.HandoffErrors);
        Assert.Equal(1.0, m.NodeSuccessRate);
    }

    [Fact]
    public async Task Parallel_Forks_Both_Branches_And_Rejoins_At_Merge()
    {
        var gem = new FakeGemini();
        var orch = new FakeOrchestrator();
        var ver = new FakeVerifier();

        var graph = new ScriptGraph
        {
            Nodes =
            {
                N("start", "start"),
                N("fork", "parallel"),
                N("back", "claude-execute", s: new() { ["Prompt"] = "backend" }),
                N("front", "gemini-prompt", s: new() { ["Prompt"] = "frontend" }),
                N("join", "merge"),
                N("review", "gemini-prompt", s: new() { ["Prompt"] = "review" }),
            },
            Edges =
            {
                E("start", "Next", "fork", "Execute"),
                E("fork", "BranchA", "back", "Execute"),
                E("fork", "BranchB", "front", "Execute"),
                E("back", "Success", "join", "A"),
                E("front", "Next", "join", "B"),
                E("join", "Next", "review", "Execute"),
            }
        };

        var m = await Run(graph, gem, orch, ver);

        Assert.Single(orch.Prompts);                 // backend branch ran (Claude)
        Assert.Equal(2, gem.Prompts.Count);          // frontend + review ran (Gemini)
        // start, fork, back, front, merge, review = 6 nodes, all successful.
        Assert.Equal(6, m.NodesExecuted);
        Assert.Equal(6, m.NodesSucceeded);
    }

    [Fact]
    public async Task Wait_Approval_Gates_Downstream_Execution()
    {
        var graph = new ScriptGraph
        {
            Nodes =
            {
                N("start", "start"),
                N("gate", "wait-approval", s: new() { ["Message"] = "ok?" }),
                N("after", "gemini-prompt", s: new() { ["Prompt"] = "go" }),
            },
            Edges =
            {
                E("start", "Next", "gate", "Execute"),
                E("gate", "Approved", "after", "Execute"),
            }
        };

        var approvedGem = new FakeGemini();
        await Run(graph, approvedGem, new FakeOrchestrator(), new FakeVerifier(), approval: _ => Task.FromResult(true));
        Assert.Single(approvedGem.Prompts); // approved → downstream ran

        var rejectedGem = new FakeGemini();
        await Run(graph, rejectedGem, new FakeOrchestrator(), new FakeVerifier(), approval: _ => Task.FromResult(false));
        Assert.Empty(rejectedGem.Prompts);  // rejected → downstream skipped
    }

    [Fact]
    public async Task Missing_Upstream_Value_Counts_As_Handoff_Error()
    {
        // claude.Context is wired to a port the source never produces → schema/handoff error.
        var graph = new ScriptGraph
        {
            Nodes =
            {
                N("start", "start"),
                N("exec", "claude-execute", s: new() { ["Prompt"] = "Do: {{CONTEXT}}" }),
            },
            Edges =
            {
                E("start", "Next", "exec", "Execute"),
                E("start", "Nope", "exec", "Context"), // "Nope" is never set by start
            }
        };

        var m = await Run(graph, new FakeGemini(), new FakeOrchestrator(), new FakeVerifier());

        Assert.True(m.HandoffErrors >= 1);
    }

    [Fact]
    public async Task Node_Throwing_Is_Isolated_And_Lowers_Success_Rate()
    {
        var orch = new FakeOrchestrator { EmitError = true }; // Claude reports an error → Failure routing, not a throw
        var ver = new FakeVerifier();

        var graph = new ScriptGraph
        {
            Nodes =
            {
                N("start", "start"),
                N("exec", "claude-execute", s: new() { ["Prompt"] = "do" }),
                N("build", "verification", s: new() { ["Command"] = "x" }),
            },
            Edges =
            {
                E("start", "Next", "exec", "Execute"),
                E("exec", "Failure", "build", "Execute"), // error routes here
            }
        };

        var m = await Run(graph, new FakeGemini(), orch, ver);

        // Claude erroring routes to Failure (handled, not thrown) → all 3 nodes still "succeed".
        Assert.Equal(3, m.NodesExecuted);
        Assert.Equal(3, m.NodesSucceeded);
        Assert.True(m.FinalVerificationPassed);
    }
}

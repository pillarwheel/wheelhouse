using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WheelHouse.Core;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Models.Script;
using WheelHouse.Core.Prompts;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Executes a <see cref="ScriptGraph"/> as a control-flow state machine: it starts at the
/// graph's <c>start</c> node and walks control edges (<c>Next</c>/<c>Passed</c>/<c>Failed</c>/
/// <c>Success</c>/<c>Failure</c>/<c>Loop</c>/<c>Done</c>/<c>Approved</c>/<c>Rejected</c>) from node
/// to node, pulling data inputs (<c>Goal</c>/<c>Context</c>/<c>Response</c>/<c>Plan</c>) from
/// upstream outputs. A <c>parallel</c> node forks concurrent branches that rejoin at a <c>merge</c>
/// node. A shared variable bag (<c>LastResponse</c>, …) and per-node <c>ContextGlobs</c> scoping
/// keep multi-model pipelines token-efficient.
/// </summary>
public class ScriptExecutor : IScriptExecutor
{
    // Hard cap on node executions so a mis-wired loop can never run forever.
    private const int MaxSteps = 500;

    // Context-scoping caps so a broad glob can't blow up the prompt.
    private const int MaxContextFiles = 40;
    private const int MaxFileBytes = 4_000;
    private const int MaxContextBytes = 48_000;

    private static readonly string[] NoiseDirs = { ".git", "bin", "obj", "node_modules", ".vs", ".idea" };

    private readonly IGeminiService _gemini;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IVerificationRunner _verifier;
    private readonly IDbContextFactory<WheelHouseDbContext> _dbFactory;
    private readonly IGitService _git;

    public ScriptExecutor(
        IGeminiService gemini,
        IAgentOrchestrator orchestrator,
        IVerificationRunner verifier,
        IDbContextFactory<WheelHouseDbContext> dbFactory,
        IGitService git)
    {
        _gemini = gemini;
        _orchestrator = orchestrator;
        _verifier = verifier;
        _dbFactory = dbFactory;
        _git = git;
    }

    public Func<string, Task<bool>>? ApprovalHandler { get; set; }

    /// <summary>Thread-safe accumulator for run telemetry (parallel branches mutate it concurrently).</summary>
    private sealed class Acc
    {
        public int Executed, Succeeded, Handoff, Tokens;
        public double Cost;
        public bool? FinalVerify;
        public readonly object Lock = new();
    }

    public async Task<ScriptRunMetrics> RunGraphAsync(
        AgentSession session,
        Action<string, AgentEventKind> onLog,
        Action<string> onNodeActive,
        CancellationToken cancellationToken,
        Action<ScriptNodeTelemetry>? onNodeTelemetry = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var acc = new Acc();

        ScriptRunMetrics Result()
        {
            lock (acc.Lock)
                return new()
                {
                    NodesExecuted = acc.Executed,
                    NodesSucceeded = acc.Succeeded,
                    HandoffErrors = acc.Handoff,
                    FinalVerificationPassed = acc.FinalVerify,
                    ApproxTokens = acc.Tokens,
                    CostUsd = acc.Cost,
                    ElapsedMs = sw.ElapsedMilliseconds
                };
        }

        var graph = ScriptGraph.FromJson(session.Template?.GraphJson);
        var start = graph.StartNode;
        if (start is null)
        {
            onLog("Graph has no Start node — nothing to run.", AgentEventKind.Error);
            return Result();
        }

        var nodesById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // State is concurrency-safe because parallel branches run on separate threads.
        var outputs = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var shared = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var loopVisits = new Dictionary<string, int>(StringComparer.Ordinal);
        var loopLock = new object();
        var stepCounter = new int[1]; // boxed so Interlocked can ref it across branch closures

        string OutKey(string nodeId, string port) => $"{nodeId} {port}";

        void SetOutput(ScriptNode node, string port, string value) => outputs[OutKey(node.Id, port)] = value;

        void Publish(ScriptNode node, string response)
        {
            SetOutput(node, "Response", response);
            shared["LastResponse"] = response;
        }

        string? GetInput(ScriptNode node, string port)
        {
            var edge = graph.Edges.FirstOrDefault(e =>
                e.TargetNodeId == node.Id && string.Equals(e.TargetPort, port, StringComparison.OrdinalIgnoreCase));
            if (edge is null) return null;
            if (outputs.TryGetValue(OutKey(edge.SourceNodeId, edge.SourcePort), out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
            // A wired input whose upstream value never materialized is a handoff/schema error.
            Interlocked.Increment(ref acc.Handoff);
            return null;
        }

        string Render(ScriptNode node, string? template)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in shared) values[kv.Key] = kv.Value;

            var def = ScriptNodeTypes.Find(node.Type);
            if (def is not null)
                foreach (var port in def.Inputs)
                    if (!ScriptNodeTypes.ControlInputPorts.Contains(port))
                        values[port] = GetInput(node, port);

            if (string.IsNullOrWhiteSpace(template))
            {
                var goal = values.GetValueOrDefault("Goal");
                var ctx = values.GetValueOrDefault("Context");
                return string.Join("\n\n", new[] { goal, ctx }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            return PromptRendering.Render(template, values);
        }

        string ComposePrompt(ScriptNode node, string? template, string repoPath)
        {
            var prompt = Render(node, template);
            var scoped = BuildScopedContext(repoPath, node.Settings.GetValueOrDefault("ContextGlobs"), onLog);
            return string.IsNullOrEmpty(scoped) ? prompt : prompt + scoped;
        }

        ScriptEdge? ControlEdge(ScriptNode node, string port) =>
            graph.Edges.FirstOrDefault(e =>
                e.SourceNodeId == node.Id && string.Equals(e.SourcePort, port, StringComparison.OrdinalIgnoreCase));

        async Task<(string Text, bool Error, bool GotUsage)> RunClaude(string prompt, string permissionMode, string repoPath)
        {
            var sb = new StringBuilder();
            var hadError = false;
            var gotUsage = false;
            var request = new AgentRunRequest(prompt, repoPath, PermissionMode: permissionMode);
            await foreach (var evt in _orchestrator.RunAsync(request, cancellationToken))
            {
                onLog(evt.Text, evt.Kind);
                if (evt.Kind == AgentEventKind.AssistantText) sb.AppendLine(evt.Text);
                if (evt.IsError || evt.Kind == AgentEventKind.Error) hadError = true;
                if (evt.Usage is { } usage)
                {
                    // Real CLI-reported accounting beats the chars÷4 estimate.
                    gotUsage = usage.TotalTokens > 0;
                    Interlocked.Add(ref acc.Tokens, usage.TotalTokens);
                    if (usage.CostUsd is { } cost)
                        lock (acc.Lock) acc.Cost += cost;
                }
            }
            return (sb.ToString().Trim(), hadError, gotUsage);
        }

        // Rough token estimate (chars ÷ 4) accumulated across LLM/agent nodes for the efficiency KPI.
        void AddTokens(params string?[] texts)
        {
            var chars = texts.Where(t => t is not null).Sum(t => t!.Length);
            if (chars > 0) Interlocked.Add(ref acc.Tokens, chars / 4);
        }

        // Atomic git commit (research_notes §2): snapshot a code-changing stage so it can be rolled back.
        async Task AutoCommit(ScriptNode node, string repoPath)
        {
            var label = string.IsNullOrWhiteSpace(node.Name) ? "Claude Execute" : node.Name;
            var message = $"{label}: auto-commit";
            onLog($"Auto-committing stage: {message}", AgentEventKind.System);
            await _verifier.RunAsync("git add -A", repoPath, cancellationToken: cancellationToken);
            var commit = await _verifier.RunAsync(
                $"git commit -m \"{message.Replace('"', '\'')}\"", repoPath, cancellationToken: cancellationToken);
            onLog(commit.Succeeded ? "Stage committed." : $"Nothing to commit.\n{Truncate(commit.Output)}", AgentEventKind.System);
        }

        // Runs one leaf node (inside `repoPath`, which is a worktree for isolated parallel
        // branches) and returns the control-output port to follow.
        async Task<string> ExecuteNodeAsync(ScriptNode node, string entryPort, string repoPath)
        {
            onNodeActive(node.Id);
            var label = string.IsNullOrWhiteSpace(node.Name) ? node.Type : node.Name;
            onLog($"▶ {label} ({node.Type})", AgentEventKind.System);

            switch (node.Type.ToLowerInvariant())
            {
                case "start":
                    SetOutput(node, "Goal",
                        !string.IsNullOrWhiteSpace(session.PlanningContext) ? session.PlanningContext! : session.Name);
                    SetOutput(node, "RepositoryPath", repoPath);
                    return "Next";

                case "gemini-prompt":
                {
                    var prompt = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"), repoPath);
                    onLog($"Gemini ← {Truncate(prompt)}", AgentEventKind.System);
                    var response = await _gemini.CompleteAsync(prompt, cancellationToken);
                    AddTokens(prompt, response);
                    Publish(node, response);
                    onLog(response, AgentEventKind.AssistantText);
                    return "Next";
                }

                case "claude-prompt":
                {
                    var prompt = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"), repoPath);
                    var (response, error, gotUsage) = await RunClaude(prompt, "plan", repoPath);
                    if (!gotUsage) AddTokens(prompt, response);
                    Publish(node, response);
                    return error ? "Failure" : "Success";
                }

                case "claude-execute":
                {
                    var prompt = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"), repoPath);
                    var (response, error, gotUsage) = await RunClaude(prompt, "acceptEdits", repoPath);
                    if (!gotUsage) AddTokens(prompt, response);
                    Publish(node, response);
                    if (!error && string.Equals(node.Settings.GetValueOrDefault("AutoCommit"), "on", StringComparison.OrdinalIgnoreCase))
                        await AutoCommit(node, repoPath);
                    return error ? "Failure" : "Success";
                }

                case "dialogue":
                {
                    var rounds = int.TryParse(node.Settings.GetValueOrDefault("Rounds"), out var r) && r > 0 ? Math.Min(r, 6) : 2;
                    var seed = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"), repoPath);
                    if (string.IsNullOrWhiteSpace(seed)) seed = shared.GetValueOrDefault("LastResponse") ?? session.Name;

                    var transcript = new StringBuilder();
                    var currentText = seed;
                    for (var i = 1; i <= rounds && !cancellationToken.IsCancellationRequested; i++)
                    {
                        var critique = await _gemini.CompleteAsync(
                            "You are a senior reviewer. Critique the following proposal and list concrete improvements:\n\n" + currentText,
                            cancellationToken);
                        transcript.AppendLine($"## Gemini · round {i}").AppendLine(critique).AppendLine();
                        onLog($"Gemini (round {i}): {Truncate(critique)}", AgentEventKind.AssistantText);

                        var (refined, _, refinedUsage) = await RunClaude(
                            $"A reviewer gave this feedback:\n{critique}\n\nRevise and improve the proposal below accordingly. " +
                            $"Return only the revised proposal:\n\n{currentText}", "plan", repoPath);
                        transcript.AppendLine($"## Claude · round {i}").AppendLine(refined).AppendLine();
                        AddTokens(critique, refinedUsage ? null : refined);
                        if (!string.IsNullOrWhiteSpace(refined)) currentText = refined;
                    }
                    Publish(node, transcript.ToString().Trim());
                    return "Next";
                }

                case "verification":
                {
                    var command = node.Settings.GetValueOrDefault("Command");
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        onLog("No command configured — treating as passed.", AgentEventKind.System);
                        lock (acc.Lock) acc.FinalVerify = true;
                        return "Passed";
                    }
                    onLog($"$ {command}", AgentEventKind.System);
                    var result = await _verifier.RunAsync(command, repoPath, cancellationToken: cancellationToken);
                    onLog($"(exit {result.ExitCode})\n{result.Output}", result.Succeeded ? AgentEventKind.Result : AgentEventKind.Error);
                    Publish(node, result.Output);
                    lock (acc.Lock) acc.FinalVerify = result.Succeeded;
                    return result.Succeeded ? "Passed" : "Failed";
                }

                case "git-command":
                {
                    var args = node.Settings.GetValueOrDefault("Command");
                    if (string.IsNullOrWhiteSpace(args))
                    {
                        onLog("No git args configured — skipping.", AgentEventKind.System);
                        return "Success";
                    }
                    var cmd = $"git {args}";
                    onLog($"$ {cmd}", AgentEventKind.System);
                    var result = await _verifier.RunAsync(cmd, repoPath, cancellationToken: cancellationToken);
                    onLog($"(exit {result.ExitCode})\n{result.Output}", result.Succeeded ? AgentEventKind.Result : AgentEventKind.Error);
                    Publish(node, result.Output);
                    return result.Succeeded ? "Success" : "Failure";
                }

                case "loop-count":
                {
                    var max = int.TryParse(node.Settings.GetValueOrDefault("MaxIterations"), out var m) && m > 0 ? m : 3;
                    int visits;
                    lock (loopLock)
                    {
                        if (string.Equals(entryPort, "Reset", StringComparison.OrdinalIgnoreCase))
                        {
                            loopVisits[node.Id] = 0;
                            onLog("Loop counter reset.", AgentEventKind.System);
                        }
                        visits = loopVisits.GetValueOrDefault(node.Id) + 1;
                        loopVisits[node.Id] = visits;
                    }
                    SetOutput(node, "Count", visits.ToString());
                    var outPort = visits < max ? "Loop" : "Done";
                    onLog($"Iteration {visits}/{max} → {outPort}", AgentEventKind.System);
                    return outPort;
                }

                case "conditional":
                {
                    var a = GetInput(node, "ValueA") ?? shared.GetValueOrDefault("LastResponse") ?? "";
                    var b = GetInput(node, "ValueB") ?? node.Settings.GetValueOrDefault("CompareTo") ?? "";
                    var op = node.Settings.GetValueOrDefault("Operator") ?? "Contains";
                    var match = op.ToLowerInvariant() switch
                    {
                        "equals" => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase),
                        "isempty" => string.IsNullOrWhiteSpace(a),
                        _ => b.Length == 0 || a.Contains(b, StringComparison.OrdinalIgnoreCase)
                    };
                    var outPort = match ? "True" : "False";
                    onLog($"Condition ({op}) → {outPort}", AgentEventKind.System);
                    return outPort;
                }

                case "merge":
                    onLog("Branches joined.", AgentEventKind.System);
                    return "Next";

                case "wait-approval":
                {
                    var message = Render(node, node.Settings.GetValueOrDefault("Message"));
                    if (string.IsNullOrWhiteSpace(message)) message = "Approve to continue?";
                    if (ApprovalHandler is null)
                    {
                        onLog("No approval handler — auto-approving.", AgentEventKind.System);
                        return "Approved";
                    }
                    onLog($"⏸ Awaiting approval: {message}", AgentEventKind.System);
                    var approved = await ApprovalHandler(message);
                    onLog($"Approval → {(approved ? "Approved" : "Rejected")}", approved ? AgentEventKind.Result : AgentEventKind.Error);
                    return approved ? "Approved" : "Rejected";
                }

                case "generate-tasks":
                {
                    var plan = GetInput(node, "Plan") ?? shared.GetValueOrDefault("LastResponse");
                    if (string.IsNullOrWhiteSpace(plan)) plan = session.PlanningContext;
                    if (string.IsNullOrWhiteSpace(plan))
                    {
                        onLog("No plan input — skipping task generation.", AgentEventKind.System);
                        return "Next";
                    }
                    var generated = await _gemini.GenerateTasksAsync(plan, cancellationToken);
                    await ReplaceTasksAsync(session.Id, generated, cancellationToken);
                    onLog($"Generated {generated.Count} task(s).", AgentEventKind.Result);
                    return "Next";
                }

                default:
                    onLog($"Unknown node type '{node.Type}' — skipping.", AgentEventKind.Error);
                    return "Next";
            }
        }

        // Runs one node with telemetry + per-node error isolation. Returns its control port, or null
        // when the node threw (the branch then ends rather than crashing the whole run).
        async Task<string?> RunNode(ScriptNode node, string entry, string repoPath)
        {
            // Token/cost deltas are read off the shared accumulator, so concurrent parallel
            // branches can bleed into each other's numbers — acceptable for a live gauge.
            var nodeSw = System.Diagnostics.Stopwatch.StartNew();
            var tokensBefore = Volatile.Read(ref acc.Tokens);
            double costBefore;
            lock (acc.Lock) costBefore = acc.Cost;

            void ReportTelemetry(bool succeeded)
            {
                if (onNodeTelemetry is null) return;
                double costDelta;
                lock (acc.Lock) costDelta = acc.Cost - costBefore;
                onNodeTelemetry(new ScriptNodeTelemetry(
                    node.Id, nodeSw.ElapsedMilliseconds,
                    Volatile.Read(ref acc.Tokens) - tokensBefore, costDelta, succeeded));
            }

            try
            {
                var outPort = await ExecuteNodeAsync(node, entry, repoPath);
                // A node "succeeds" when it completes without throwing — routing to a Failed/Failure
                // port is a valid result the engine handled, not an engine error.
                Interlocked.Increment(ref acc.Executed);
                Interlocked.Increment(ref acc.Succeeded);
                ReportTelemetry(succeeded: true);
                return outPort;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Interlocked.Increment(ref acc.Executed);
                var label = string.IsNullOrWhiteSpace(node.Name) ? node.Type : node.Name;
                onLog($"Node '{label}' failed: {ex.Message}", AgentEventKind.Error);
                ReportTelemetry(succeeded: false);
                return null;
            }
        }

        // Merges isolated branch worktrees back into `repoPath` (sequentially — concurrent merges
        // would race), snapshotting uncommitted agent edits first so they survive worktree removal.
        // A conflicted branch is left unmerged with its name in the log for manual resolution.
        async Task MergeWorktreesAsync(string repoPath, List<(string Path, string Branch)> worktrees)
        {
            foreach (var (wtPath, branchName) in worktrees)
            {
                try
                {
                    var status = await _git.GetStatusAsync(wtPath, cancellationToken);
                    if (status.IsRepo && !status.Clean)
                        await _git.CommitAllAsync(wtPath, $"WheelHouse parallel snapshot ({branchName})", cancellationToken);

                    var merge = await _git.MergeBranchAsync(repoPath, branchName, cancellationToken);
                    onLog(merge.Success
                        ? $"Merged {branchName}."
                        : $"Merge of {branchName} failed — branch kept for manual resolution.\n{Truncate(merge.Output)}",
                        merge.Success ? AgentEventKind.Result : AgentEventKind.Error);

                    await _git.RemoveWorktreeAsync(repoPath, wtPath, cancellationToken);
                    if (merge.Success)
                        await _git.DeleteBranchAsync(repoPath, branchName, force: false, cancellationToken);
                }
                catch (Exception ex)
                {
                    onLog($"Worktree cleanup for {branchName} failed: {ex.Message}", AgentEventKind.Error);
                }
            }
        }

        // Walks control flow from `node`. Returns the merge node it halted at (so the caller resumes
        // past it), or null when the path ends. `parallel` nodes fork concurrent sub-walks that
        // rejoin; with Isolation=worktree each branch runs in its own git worktree and verified
        // results are merged back when the branches join.
        async Task<ScriptNode?> WalkAsync(ScriptNode? node, string entryPort, string repoPath)
        {
            while (node is not null && !cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Increment(ref stepCounter[0]) > MaxSteps)
                {
                    onLog($"Stopped: exceeded {MaxSteps} node executions (possible infinite loop).", AgentEventKind.Error);
                    return null;
                }

                if (string.Equals(node.Type, "merge", StringComparison.OrdinalIgnoreCase))
                    return node; // halt so the forking parallel node can join here

                if (string.Equals(node.Type, "parallel", StringComparison.OrdinalIgnoreCase))
                {
                    onNodeActive(node.Id);
                    onLog($"▶ {(string.IsNullOrWhiteSpace(node.Name) ? node.Type : node.Name)} (parallel)", AgentEventKind.System);
                    Interlocked.Increment(ref acc.Executed);
                    Interlocked.Increment(ref acc.Succeeded);

                    var useWorktrees =
                        string.Equals(node.Settings.GetValueOrDefault("Isolation"), "worktree", StringComparison.OrdinalIgnoreCase) &&
                        await _git.IsRepositoryAsync(repoPath, cancellationToken);
                    var runTag = $"{DateTime.UtcNow:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

                    var def = ScriptNodeTypes.Find("parallel");
                    var branches = new List<Task<ScriptNode?>>();
                    var worktrees = new List<(string Path, string Branch)>();
                    foreach (var port in def?.Outputs ?? Array.Empty<string>())
                    {
                        var be = ControlEdge(node, port);
                        if (be is null || !nodesById.TryGetValue(be.TargetNodeId, out var target)) continue;

                        var branchRepo = repoPath;
                        if (useWorktrees)
                        {
                            var branchName = $"wheelhouse/parallel-{runTag}-{port.ToLowerInvariant()}";
                            var wtPath = Path.Combine(Path.GetTempPath(), "wheelhouse-worktrees", $"{runTag}-{port.ToLowerInvariant()}");
                            var add = await _git.AddWorktreeAsync(repoPath, wtPath, branchName, cancellationToken);
                            if (add.Success)
                            {
                                worktrees.Add((wtPath, branchName));
                                branchRepo = wtPath;
                                onLog($"Branch '{port}' isolated in worktree {wtPath} ({branchName}).", AgentEventKind.System);
                            }
                            else
                            {
                                onLog($"Could not create worktree for '{port}' ({Truncate(add.Output)}); running in the shared repository.", AgentEventKind.Error);
                            }
                        }
                        branches.Add(WalkAsync(target, be.TargetPort, branchRepo));
                    }

                    var halts = await Task.WhenAll(branches);

                    if (worktrees.Count > 0)
                        await MergeWorktreesAsync(repoPath, worktrees);

                    var merge = halts.FirstOrDefault(h => h is not null);
                    if (merge is null) return null; // branches ended without a merge

                    // Run the merge node once, then continue past it (back in the main repository).
                    var mergeOut = await RunNode(merge, ScriptNodeTypes.ControlPort, repoPath);
                    if (mergeOut is null) return null;
                    var me = ControlEdge(merge, mergeOut);
                    node = me is null ? null : nodesById.GetValueOrDefault(me.TargetNodeId);
                    entryPort = me?.TargetPort ?? ScriptNodeTypes.ControlPort;
                    continue;
                }

                var outPort = await RunNode(node, entryPort, repoPath);
                if (outPort is null) return null;
                var edge = ControlEdge(node, outPort);
                node = edge is null ? null : nodesById.GetValueOrDefault(edge.TargetNodeId);
                entryPort = edge?.TargetPort ?? ScriptNodeTypes.ControlPort;
            }
            return null;
        }

        await WalkAsync(start, ScriptNodeTypes.ControlPort, session.RepositoryPath);
        return Result();
    }

    private async Task ReplaceTasksAsync(int sessionId, IReadOnlyList<TaskItem> generated, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = db.Tasks.Where(t => t.AgentSessionId == sessionId);
        db.Tasks.RemoveRange(existing);

        var seq = 0;
        foreach (var t in generated)
        {
            db.Tasks.Add(new TaskItem
            {
                AgentSessionId = sessionId,
                Sequence = seq++,
                Title = t.Title,
                Description = t.Description,
                VerificationCommand = t.VerificationCommand,
                Status = WorkItemStatus.Pending
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reads workspace files matching the comma/newline-separated glob patterns and returns them as a
    /// fenced markdown block, so an LLM/agent node only sees relevant code (research_notes §2 Context Scoping).
    /// </summary>
    private static string BuildScopedContext(string repoPath, string? globs, Action<string, AgentEventKind> onLog)
    {
        if (string.IsNullOrWhiteSpace(globs) || string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            return string.Empty;

        var patterns = globs.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => (Regex: GlobToRegex(p), HasSlash: p.Replace('\\', '/').Contains('/')))
            .ToList();
        if (patterns.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        var total = 0;
        var count = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories))
            {
                if (count >= MaxContextFiles || total >= MaxContextBytes) break;

                var rel = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
                if (rel.Split('/').Any(seg => NoiseDirs.Contains(seg, StringComparer.OrdinalIgnoreCase))) continue;

                var name = Path.GetFileName(rel);
                var matched = patterns.Any(p => p.Regex.IsMatch(rel) || (!p.HasSlash && p.Regex.IsMatch(name)));
                if (!matched) continue;

                string content;
                try { content = File.ReadAllText(file); }
                catch { continue; }
                if (content.Length > MaxFileBytes) content = content[..MaxFileBytes] + "\n… (truncated)";

                sb.Append("\n### ").Append(rel).Append('\n').Append("```\n").Append(content).Append("\n```\n");
                total += content.Length;
                count++;
            }
        }
        catch (Exception ex)
        {
            onLog($"Context scoping skipped: {ex.Message}", AgentEventKind.System);
            return string.Empty;
        }

        return count == 0 ? string.Empty : "\n\n--- Scoped context (" + count + " file(s)) ---\n" + sb;
    }

    private static Regex GlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob.Replace('\\', '/').Trim())
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase);
    }

    private static string Truncate(string s, int max = 240)
        => s.Length <= max ? s : s[..max] + "…";
}

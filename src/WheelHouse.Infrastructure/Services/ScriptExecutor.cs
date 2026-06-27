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

    public ScriptExecutor(
        IGeminiService gemini,
        IAgentOrchestrator orchestrator,
        IVerificationRunner verifier,
        IDbContextFactory<WheelHouseDbContext> dbFactory)
    {
        _gemini = gemini;
        _orchestrator = orchestrator;
        _verifier = verifier;
        _dbFactory = dbFactory;
    }

    public Func<string, Task<bool>>? ApprovalHandler { get; set; }

    public async Task RunGraphAsync(
        AgentSession session,
        Action<string, AgentEventKind> onLog,
        Action<string> onNodeActive,
        CancellationToken cancellationToken)
    {
        var graph = ScriptGraph.FromJson(session.Template?.GraphJson);
        var start = graph.StartNode;
        if (start is null)
        {
            onLog("Graph has no Start node — nothing to run.", AgentEventKind.Error);
            return;
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
            return outputs.TryGetValue(OutKey(edge.SourceNodeId, edge.SourcePort), out var v) ? v : null;
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

        string ComposePrompt(ScriptNode node, string? template)
        {
            var prompt = Render(node, template);
            var scoped = BuildScopedContext(session.RepositoryPath, node.Settings.GetValueOrDefault("ContextGlobs"), onLog);
            return string.IsNullOrEmpty(scoped) ? prompt : prompt + scoped;
        }

        ScriptEdge? ControlEdge(ScriptNode node, string port) =>
            graph.Edges.FirstOrDefault(e =>
                e.SourceNodeId == node.Id && string.Equals(e.SourcePort, port, StringComparison.OrdinalIgnoreCase));

        async Task<(string Text, bool Error)> RunClaude(string prompt, string permissionMode)
        {
            var sb = new StringBuilder();
            var hadError = false;
            var request = new AgentRunRequest(prompt, session.RepositoryPath, PermissionMode: permissionMode);
            await foreach (var evt in _orchestrator.RunAsync(request, cancellationToken))
            {
                onLog(evt.Text, evt.Kind);
                if (evt.Kind == AgentEventKind.AssistantText) sb.AppendLine(evt.Text);
                if (evt.IsError || evt.Kind == AgentEventKind.Error) hadError = true;
            }
            return (sb.ToString().Trim(), hadError);
        }

        // Atomic git commit (research_notes §2): snapshot a code-changing stage so it can be rolled back.
        async Task AutoCommit(ScriptNode node)
        {
            var label = string.IsNullOrWhiteSpace(node.Name) ? "Claude Execute" : node.Name;
            var message = $"{label}: auto-commit";
            onLog($"Auto-committing stage: {message}", AgentEventKind.System);
            await _verifier.RunAsync("git add -A", session.RepositoryPath, cancellationToken: cancellationToken);
            var commit = await _verifier.RunAsync(
                $"git commit -m \"{message.Replace('"', '\'')}\"", session.RepositoryPath, cancellationToken: cancellationToken);
            onLog(commit.Succeeded ? "Stage committed." : $"Nothing to commit.\n{Truncate(commit.Output)}", AgentEventKind.System);
        }

        // Runs one leaf node and returns the control-output port to follow.
        async Task<string> ExecuteNodeAsync(ScriptNode node, string entryPort)
        {
            onNodeActive(node.Id);
            var label = string.IsNullOrWhiteSpace(node.Name) ? node.Type : node.Name;
            onLog($"▶ {label} ({node.Type})", AgentEventKind.System);

            switch (node.Type.ToLowerInvariant())
            {
                case "start":
                    SetOutput(node, "Goal",
                        !string.IsNullOrWhiteSpace(session.PlanningContext) ? session.PlanningContext! : session.Name);
                    SetOutput(node, "RepositoryPath", session.RepositoryPath);
                    return "Next";

                case "gemini-prompt":
                {
                    var prompt = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"));
                    onLog($"Gemini ← {Truncate(prompt)}", AgentEventKind.System);
                    var response = await _gemini.CompleteAsync(prompt, cancellationToken);
                    Publish(node, response);
                    onLog(response, AgentEventKind.AssistantText);
                    return "Next";
                }

                case "claude-prompt":
                {
                    var prompt = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"));
                    var (response, error) = await RunClaude(prompt, "plan");
                    Publish(node, response);
                    return error ? "Failure" : "Success";
                }

                case "claude-execute":
                {
                    var prompt = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"));
                    var (response, error) = await RunClaude(prompt, "acceptEdits");
                    Publish(node, response);
                    if (!error && string.Equals(node.Settings.GetValueOrDefault("AutoCommit"), "on", StringComparison.OrdinalIgnoreCase))
                        await AutoCommit(node);
                    return error ? "Failure" : "Success";
                }

                case "dialogue":
                {
                    var rounds = int.TryParse(node.Settings.GetValueOrDefault("Rounds"), out var r) && r > 0 ? Math.Min(r, 6) : 2;
                    var seed = ComposePrompt(node, node.Settings.GetValueOrDefault("Prompt"));
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

                        var (refined, _) = await RunClaude(
                            $"A reviewer gave this feedback:\n{critique}\n\nRevise and improve the proposal below accordingly. " +
                            $"Return only the revised proposal:\n\n{currentText}", "plan");
                        transcript.AppendLine($"## Claude · round {i}").AppendLine(refined).AppendLine();
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
                        return "Passed";
                    }
                    onLog($"$ {command}", AgentEventKind.System);
                    var result = await _verifier.RunAsync(command, session.RepositoryPath, cancellationToken: cancellationToken);
                    onLog($"(exit {result.ExitCode})\n{result.Output}", result.Succeeded ? AgentEventKind.Result : AgentEventKind.Error);
                    Publish(node, result.Output);
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
                    var result = await _verifier.RunAsync(cmd, session.RepositoryPath, cancellationToken: cancellationToken);
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

        // Walks control flow from `node`. Returns the merge node it halted at (so the caller resumes
        // past it), or null when the path ends. `parallel` nodes fork concurrent sub-walks that rejoin.
        async Task<ScriptNode?> WalkAsync(ScriptNode? node, string entryPort)
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

                    var def = ScriptNodeTypes.Find("parallel");
                    var branches = new List<Task<ScriptNode?>>();
                    foreach (var port in def?.Outputs ?? Array.Empty<string>())
                    {
                        var be = ControlEdge(node, port);
                        if (be is not null && nodesById.TryGetValue(be.TargetNodeId, out var target))
                            branches.Add(WalkAsync(target, be.TargetPort));
                    }

                    var halts = await Task.WhenAll(branches);
                    var merge = halts.FirstOrDefault(h => h is not null);
                    if (merge is null) return null; // branches ended without a merge

                    // Run the merge node once, then continue past it.
                    var mergeOut = await ExecuteNodeAsync(merge, ScriptNodeTypes.ControlPort);
                    var me = ControlEdge(merge, mergeOut);
                    node = me is null ? null : nodesById.GetValueOrDefault(me.TargetNodeId);
                    entryPort = me?.TargetPort ?? ScriptNodeTypes.ControlPort;
                    continue;
                }

                var outPort = await ExecuteNodeAsync(node, entryPort);
                var edge = ControlEdge(node, outPort);
                node = edge is null ? null : nodesById.GetValueOrDefault(edge.TargetNodeId);
                entryPort = edge?.TargetPort ?? ScriptNodeTypes.ControlPort;
            }
            return null;
        }

        await WalkAsync(start, ScriptNodeTypes.ControlPort);
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

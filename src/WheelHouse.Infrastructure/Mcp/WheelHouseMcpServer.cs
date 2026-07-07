using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Mcp;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Mcp;

/// <summary>
/// The WheelHouse MCP server: exposes the live RAG code index to the Claude Code subprocess
/// so it can retrieve context on demand mid-task, instead of relying only on whatever the
/// plan prompt happened to include. Tools resolve scoped services per call, so they always
/// see the current index. Every call is governed by the target repository's
/// <c>.wheelhouse/mcp-policy.json</c>: per-call timeout, a rolling call budget, and an
/// optional audit log.
/// </summary>
public class WheelHouseMcpServer
{
    public const string ServerName = "wheelhouse";
    private const int SnippetCap = 1200;

    private readonly McpJsonRpcHandler _handler;
    private readonly McpCallGate _gate = new();

    public WheelHouseMcpServer(IServiceScopeFactory scopeFactory)
    {
        _handler = new McpJsonRpcHandler(ServerName, "0.1.0", new[]
        {
            new McpTool(
                "search_code",
                "Search the indexed source code of this machine's repositories (semantic + keyword " +
                "hybrid). Use it to find where something is implemented, defined, or configured. " +
                "Returns file paths with matching code snippets.",
                """
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string", "description": "What to find: an identifier, error text, or a natural-language description" },
                    "top_n": { "type": "integer", "description": "Maximum results to return (default 5)" },
                    "repository": { "type": "string", "description": "Absolute repository root to search; omit to search all indexed repositories" }
                  },
                  "required": ["query"]
                }
                """,
                (args, ct) => RunGovernedAsync(scopeFactory, "search_code", args, ct, SearchCodeAsync)),

            new McpTool(
                "get_knowledge",
                "Read the repository's curated knowledge file (.wheelhouse/knowledge.md): " +
                "conventions, architecture notes and gotchas the maintainers wrote down for agents.",
                """
                {
                  "type": "object",
                  "properties": {
                    "repository": { "type": "string", "description": "Absolute repository root" }
                  },
                  "required": ["repository"]
                }
                """,
                (args, ct) => RunGovernedAsync(scopeFactory, "get_knowledge", args, ct,
                    (_, a, c) => GetKnowledgeAsync(a, c)))
        });
    }

    public Task<string?> HandleAsync(string requestJson, CancellationToken cancellationToken = default)
        => _handler.HandleAsync(requestJson, cancellationToken);

    /// <summary>
    /// Applies the target repository's MCP policy around a tool call: rolling call budget
    /// (<see cref="McpPolicy.MaxToolCallsPerTurn"/> per <see cref="McpCallGate.DefaultWindow"/>),
    /// per-call timeout (<see cref="McpPolicy.ToolTimeoutMs"/>), and audit logging to
    /// <c>.wheelhouse/mcp-audit.log</c>. Calls without a resolvable repository run under the
    /// default policy and are budgeted under a shared global key.
    /// </summary>
    private async Task<string> RunGovernedAsync(
        IServiceScopeFactory scopeFactory, string toolName, JsonElement? args, CancellationToken cancellationToken,
        Func<IServiceProvider, JsonElement?, CancellationToken, Task<string>> tool)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var repository = GetString(args, "repository");
        var hasRepo = !string.IsNullOrWhiteSpace(repository) && Directory.Exists(repository);
        var policy = hasRepo
            ? await scope.ServiceProvider.GetRequiredService<IMcpPolicyService>()
                .LoadPolicyAsync(repository!, cancellationToken)
            : new McpPolicy();

        if (!_gate.TryAcquire(hasRepo ? repository! : "(global)", policy.MaxToolCallsPerTurn,
                McpCallGate.DefaultWindow, DateTimeOffset.UtcNow))
            return $"Denied by MCP policy: more than {policy.MaxToolCallsPerTurn} tool calls in " +
                   $"{McpCallGate.DefaultWindow.TotalSeconds:0}s for this repository. Raise " +
                   "maxToolCallsPerTurn in .wheelhouse/mcp-policy.json if this is expected.";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (policy.ToolTimeoutMs > 0) timeout.CancelAfter(policy.ToolTimeoutMs);

        var sw = Stopwatch.StartNew();
        var ok = true;
        string result;
        try
        {
            result = await tool(scope.ServiceProvider, args, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ok = false;
            result = $"Denied by MCP policy: tool call exceeded toolTimeoutMs ({policy.ToolTimeoutMs} ms).";
        }
        catch (Exception)
        {
            if (hasRepo && policy.AuditLog) WriteAudit(repository!, toolName, ok: false, sw.ElapsedMilliseconds);
            throw; // the JSON-RPC handler turns this into an isError result
        }

        if (hasRepo && policy.AuditLog) WriteAudit(repository!, toolName, ok, sw.ElapsedMilliseconds);
        return result;
    }

    private static void WriteAudit(string repository, string toolName, bool ok, long elapsedMs)
    {
        try
        {
            var dir = Path.Combine(repository, ".wheelhouse");
            Directory.CreateDirectory(dir);
            var line = JsonSerializer.Serialize(new
            {
                ts = DateTimeOffset.UtcNow.ToString("O"),
                tool = toolName,
                ok,
                ms = elapsedMs
            });
            File.AppendAllText(Path.Combine(dir, "mcp-audit.log"), line + Environment.NewLine);
        }
        catch
        {
            // Auditing must never break a tool call.
        }
    }

    private static async Task<string> SearchCodeAsync(
        IServiceProvider services, JsonElement? args, CancellationToken cancellationToken)
    {
        var query = GetString(args, "query");
        if (string.IsNullOrWhiteSpace(query)) return "Missing required argument: query";

        var topN = GetInt(args, "top_n") ?? 5;
        var repository = GetString(args, "repository");

        var search = services.GetRequiredService<IVectorSearchService>();
        var results = await search.SearchAsync(
            query, Math.Clamp(topN, 1, 20), repository, cancellationToken: cancellationToken);
        if (results.Count == 0)
            return "No matches in the code index. The repository may not be indexed yet, or try different terms.";

        var sb = new StringBuilder();
        for (var i = 0; i < results.Count; i++)
        {
            var e = results[i].Entry;
            sb.AppendLine($"## {i + 1}. {e.FilePath} ({e.SymbolKind} {e.SymbolName}, score {results[i].Score:0.000})");
            var snippet = e.Snippet.Length > SnippetCap ? e.Snippet[..SnippetCap] + "\n…(truncated)" : e.Snippet;
            sb.AppendLine("```");
            sb.AppendLine(snippet);
            sb.AppendLine("```");
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task<string> GetKnowledgeAsync(JsonElement? args, CancellationToken cancellationToken)
    {
        var repository = GetString(args, "repository");
        if (string.IsNullOrWhiteSpace(repository)) return "Missing required argument: repository";
        if (!Directory.Exists(repository)) return $"Repository not found: {repository}";

        var path = Path.Combine(repository, ".wheelhouse", "knowledge.md");
        if (!File.Exists(path))
            return "No .wheelhouse/knowledge.md in this repository.";
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static string? GetString(JsonElement? args, string name) =>
        args is { } a && a.ValueKind == JsonValueKind.Object &&
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement? args, string name) =>
        args is { } a && a.ValueKind == JsonValueKind.Object &&
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;
}

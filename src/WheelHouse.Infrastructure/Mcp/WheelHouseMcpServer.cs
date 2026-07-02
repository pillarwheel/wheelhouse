using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Mcp;

namespace WheelHouse.Infrastructure.Mcp;

/// <summary>
/// The WheelHouse MCP server: exposes the live RAG code index to the Claude Code subprocess
/// so it can retrieve context on demand mid-task, instead of relying only on whatever the
/// plan prompt happened to include. Tools resolve scoped services per call, so they always
/// see the current index.
/// </summary>
public class WheelHouseMcpServer
{
    public const string ServerName = "wheelhouse";
    private const int SnippetCap = 1200;

    private readonly McpJsonRpcHandler _handler;

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
                (args, ct) => SearchCodeAsync(scopeFactory, args, ct)),

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
                (args, ct) => GetKnowledgeAsync(args, ct))
        });
    }

    public Task<string?> HandleAsync(string requestJson, CancellationToken cancellationToken = default)
        => _handler.HandleAsync(requestJson, cancellationToken);

    private static async Task<string> SearchCodeAsync(
        IServiceScopeFactory scopeFactory, JsonElement? args, CancellationToken cancellationToken)
    {
        var query = GetString(args, "query");
        if (string.IsNullOrWhiteSpace(query)) return "Missing required argument: query";

        var topN = GetInt(args, "top_n") ?? 5;
        var repository = GetString(args, "repository");

        await using var scope = scopeFactory.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();
        var results = await search.SearchAsync(query, Math.Clamp(topN, 1, 20), repository, cancellationToken);
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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WheelHouse.Core.Mcp;

/// <summary>One tool exposed over MCP: metadata for discovery plus the async implementation.</summary>
/// <param name="Name">Tool name as Claude sees it (becomes <c>mcp__&lt;server&gt;__&lt;name&gt;</c>).</param>
/// <param name="Description">What the tool does — Claude reads this to decide when to call it.</param>
/// <param name="InputSchemaJson">JSON Schema (as a JSON string) describing the arguments object.</param>
/// <param name="ExecuteAsync">Runs the tool; receives the parsed arguments (or null) and returns text.</param>
public sealed record McpTool(
    string Name,
    string Description,
    string InputSchemaJson,
    Func<JsonElement?, CancellationToken, Task<string>> ExecuteAsync);

/// <summary>
/// Minimal, dependency-free Model Context Protocol server core: a JSON-RPC 2.0 handler
/// supporting <c>initialize</c>, <c>tools/list</c> and <c>tools/call</c> — enough for
/// Claude Code to discover and invoke WheelHouse tools over the HTTP transport.
/// Pure logic (no I/O), so the protocol behaviour is unit-testable offline.
/// </summary>
public sealed class McpJsonRpcHandler
{
    public const string ProtocolVersion = "2024-11-05";

    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly IReadOnlyList<McpTool> _tools;

    public McpJsonRpcHandler(string serverName, string serverVersion, IReadOnlyList<McpTool> tools)
    {
        _serverName = serverName;
        _serverVersion = serverVersion;
        _tools = tools;
    }

    /// <summary>
    /// Handles one JSON-RPC message. Returns the response JSON, or null for notifications
    /// (which per JSON-RPC receive no response).
    /// </summary>
    public async Task<string?> HandleAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(requestJson); }
        catch { return Error(null, -32700, "Parse error"); }

        if (root is not JsonObject request) return Error(null, -32600, "Invalid request");

        var id = request["id"]?.DeepClone();
        var method = (request["method"] as JsonValue)?.GetValue<string>();
        if (string.IsNullOrEmpty(method))
            return id is null ? null : Error(id, -32600, "Invalid request: missing method");

        switch (method)
        {
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = _serverName, ["version"] = _serverVersion }
                });

            case "notifications/initialized":
            case "notifications/cancelled":
                return null;

            case "ping":
                return Result(id, new JsonObject());

            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = ToolCatalog() });

            case "tools/call":
                return await CallToolAsync(id, request["params"] as JsonObject, cancellationToken);

            default:
                return id is null ? null : Error(id, -32601, $"Method not found: {method}");
        }
    }

    private JsonArray ToolCatalog()
    {
        var tools = new JsonArray();
        foreach (var tool in _tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchemaJson)
            });
        }
        return tools;
    }

    private async Task<string?> CallToolAsync(JsonNode? id, JsonObject? p, CancellationToken cancellationToken)
    {
        var name = (p?["name"] as JsonValue)?.GetValue<string>();
        var tool = _tools.FirstOrDefault(t => t.Name == name);
        if (tool is null) return Error(id, -32602, $"Unknown tool: {name}");

        JsonElement? arguments = null;
        if (p?["arguments"] is JsonNode argsNode)
            arguments = JsonSerializer.Deserialize<JsonElement>(argsNode.ToJsonString());

        string text;
        var isError = false;
        try { text = await tool.ExecuteAsync(arguments, cancellationToken); }
        catch (Exception ex)
        {
            text = $"Tool '{tool.Name}' failed: {ex.Message}";
            isError = true;
        }

        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError
        });
    }

    private static string Result(JsonNode? id, JsonNode result) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    }.ToJsonString();
}

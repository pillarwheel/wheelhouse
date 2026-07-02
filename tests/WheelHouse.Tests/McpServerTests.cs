using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Mcp;
using WheelHouse.Infrastructure.Agents;
using WheelHouse.Infrastructure.Mcp;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using WheelHouse.Infrastructure.Vector;
using Xunit;

namespace WheelHouse.Tests;

public class McpJsonRpcHandlerTests
{
    private static McpJsonRpcHandler NewHandler() => new("test-server", "1.0", new[]
    {
        new McpTool("echo", "Echoes the input back.",
            """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""",
            (args, _) => Task.FromResult("echo: " + args?.GetProperty("text").GetString())),
        new McpTool("boom", "Always fails.", """{"type":"object"}""",
            (_, _) => throw new InvalidOperationException("kaboom"))
    });

    private static JsonElement Parse(string? json) => JsonDocument.Parse(json!).RootElement;

    [Fact]
    public async Task Initialize_Returns_Protocol_And_ServerInfo()
    {
        var res = Parse(await NewHandler().HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}"""));

        Assert.Equal(1, res.GetProperty("id").GetInt32());
        var result = res.GetProperty("result");
        Assert.Equal(McpJsonRpcHandler.ProtocolVersion, result.GetProperty("protocolVersion").GetString());
        Assert.Equal("test-server", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsList_Includes_Names_And_Schemas()
    {
        var res = Parse(await NewHandler().HandleAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""));

        var tools = res.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(2, tools.Count);
        Assert.Equal("echo", tools[0].GetProperty("name").GetString());
        Assert.Equal("object", tools[0].GetProperty("inputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToolsCall_Dispatches_Arguments_And_Returns_Text_Content()
    {
        var res = Parse(await NewHandler().HandleAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}"""));

        var result = res.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var content = result.GetProperty("content")[0];
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal("echo: hi", content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ToolsCall_Unknown_Tool_Is_JsonRpc_Error()
    {
        var res = Parse(await NewHandler().HandleAsync(
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"nope"}}"""));

        Assert.Equal(-32602, res.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ToolsCall_Exception_Becomes_IsError_Result()
    {
        var res = Parse(await NewHandler().HandleAsync(
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"boom","arguments":{}}}"""));

        var result = res.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("kaboom", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Notifications_Get_No_Response()
    {
        Assert.Null(await NewHandler().HandleAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public async Task Malformed_Json_Is_Parse_Error()
    {
        var res = Parse(await NewHandler().HandleAsync("{not json"));
        Assert.Equal(-32700, res.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Unknown_Method_With_Id_Is_MethodNotFound()
    {
        var res = Parse(await NewHandler().HandleAsync(
            """{"jsonrpc":"2.0","id":9,"method":"resources/list"}"""));
        Assert.Equal(-32601, res.GetProperty("error").GetProperty("code").GetInt32());
    }
}

public class McpAgentArgsTests
{
    [Fact]
    public void BuildAgentArgs_Adds_McpConfig_And_Allows_Its_Tools()
    {
        var args = ClaudeCommand.BuildAgentArgs(
            new AgentRunRequest("x", "C:/repo"),
            mcpConfigPath: "C:/cfg/mcp-config.json",
            mcpAllowedTools: new[] { "mcp__wheelhouse__search_code" });

        var i = args.IndexOf("--mcp-config");
        Assert.True(i >= 0);
        Assert.Equal("C:/cfg/mcp-config.json", args[i + 1]);

        var a = args.IndexOf("--allowedTools");
        Assert.Equal("mcp__wheelhouse__search_code", args[a + 1]);
    }

    [Fact]
    public void BuildAgentArgs_Merges_Mcp_Tools_Into_Existing_Allowlist_Without_Duplicates()
    {
        var args = ClaudeCommand.BuildAgentArgs(
            new AgentRunRequest("x", "C:/repo",
                AllowedTools: new[] { "Bash", "mcp__wheelhouse__search_code" }),
            mcpConfigPath: "cfg.json",
            mcpAllowedTools: new[] { "mcp__wheelhouse__search_code", "mcp__wheelhouse__get_knowledge" });

        var a = args.IndexOf("--allowedTools");
        Assert.Equal("Bash,mcp__wheelhouse__search_code,mcp__wheelhouse__get_knowledge", args[a + 1]);
    }

    [Fact]
    public void BuildAgentArgs_Without_McpConfig_Is_Unchanged()
    {
        var args = ClaudeCommand.BuildAgentArgs(new AgentRunRequest("x", "C:/repo"));
        Assert.DoesNotContain("--mcp-config", args);
        Assert.DoesNotContain("--allowedTools", args);
    }
}

/// <summary>End-to-end: an MCP tools/call against a real (offline) index answers with code.</summary>
public class WheelHouseMcpServerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"whmcp_{Guid.NewGuid():N}.db");
    private readonly string _repo = Path.Combine(Path.GetTempPath(), $"whrepo_{Guid.NewGuid():N}");
    private readonly ServiceProvider _provider;

    public WheelHouseMcpServerTests()
    {
        Directory.CreateDirectory(_repo);

        var services = new ServiceCollection();
        services.AddDbContext<WheelHouseDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddSingleton<IEmbeddingProvider, FakeEmbeddings>();
        services.AddSingleton<ICodeCompressionService, CodeCompressionService>();
        services.AddScoped<IVectorStore, CosineVectorStore>();
        services.AddScoped<IVectorSearchService, VectorSearchService>();
        services.AddLogging();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<WheelHouseDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task SearchCode_Tool_Returns_Indexed_Snippets()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "auth.cs"),
            "public class Auth { public void ValidateJwtToken() { } }");
        using (var scope = _provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IVectorSearchService>()
                .IndexRepositoryAsync(_repo);

        var mcp = new WheelHouseMcpServer(_provider.GetRequiredService<IServiceScopeFactory>());
        var request = ToolCall(1, "search_code", new { query = "ValidateJwtToken", repository = _repo });
        var res = JsonDocument.Parse((await mcp.HandleAsync(request))!).RootElement;

        var result = res.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("auth.cs", text);
        Assert.Contains("ValidateJwtToken", text);
    }

    [Fact]
    public async Task GetKnowledge_Tool_Reads_The_Knowledge_File()
    {
        Directory.CreateDirectory(Path.Combine(_repo, ".wheelhouse"));
        await File.WriteAllTextAsync(Path.Combine(_repo, ".wheelhouse", "knowledge.md"),
            "# Conventions\nAlways use EF migrations.");

        var mcp = new WheelHouseMcpServer(_provider.GetRequiredService<IServiceScopeFactory>());
        var request = ToolCall(2, "get_knowledge", new { repository = _repo });
        var res = JsonDocument.Parse((await mcp.HandleAsync(request))!).RootElement;

        var text = res.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Always use EF migrations.", text);
    }

    private static string ToolCall(int id, string tool, object arguments) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = new { name = tool, arguments }
        });

    public void Dispose()
    {
        _provider.Dispose();
        try { Directory.Delete(_repo, recursive: true); } catch { /* ignore */ }
        foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            try { File.Delete(f); } catch { /* ignore */ }
    }

    private sealed class FakeEmbeddings : IEmbeddingProvider
    {
        public string Id => "fake";
        public int Dimensions => 3;
        public bool IsAvailable => true;
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[] { 1f, 0f, 0f });
    }
}

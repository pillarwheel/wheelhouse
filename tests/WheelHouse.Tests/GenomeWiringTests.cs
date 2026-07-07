using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Search;
using WheelHouse.Infrastructure.Agents;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using WheelHouse.Infrastructure.Vector;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Proves the Darwin genome actually drives the harness: KeywordWeight re-balances hybrid
/// search, and the preamble/template overrides reach the Gemini request — with defaults that
/// reproduce canonical behavior exactly.
/// </summary>
public class GenomeWiringTests
{
    private static CodeSearchResult R(int id, string file)
        => new(new CodeIndexEntry { Id = id, FilePath = file, SymbolName = file }, 0.5);

    [Fact]
    public void Weighted_Fusion_Lets_The_Heavier_Leg_Win()
    {
        // One hit per leg, both at rank 1 — weight is the only differentiator.
        var semantic = new[] { R(1, "semantic.cs") };
        var keyword = new[] { R(2, "keyword.cs") };

        var keywordHeavy = SearchFusion.ReciprocalRankFusion(
            semantic, keyword, topN: 2, semanticWeight: 0.2, keywordWeight: 1.8);
        Assert.Equal("keyword.cs", keywordHeavy[0].Entry.FilePath);

        var semanticHeavy = SearchFusion.ReciprocalRankFusion(
            semantic, keyword, topN: 2, semanticWeight: 1.8, keywordWeight: 0.2);
        Assert.Equal("semantic.cs", semanticHeavy[0].Entry.FilePath);
    }

    [Fact]
    public void Equal_Weights_Reproduce_Unweighted_Ordering()
    {
        var semantic = new[] { R(1, "a.cs"), R(2, "both.cs") };
        var keyword = new[] { R(3, "k.cs"), R(2, "both.cs") };

        var unweighted = SearchFusion.ReciprocalRankFusion(semantic, keyword, topN: 3);
        var weighted = SearchFusion.ReciprocalRankFusion(
            semantic, keyword, topN: 3, semanticWeight: 1.0, keywordWeight: 1.0);

        Assert.Equal(
            unweighted.Select(r => r.Entry.Id),
            weighted.Select(r => r.Entry.Id));
    }

    [Fact]
    public async Task KeywordWeight_Rebalances_Hybrid_Search_End_To_End()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"whgen_{Guid.NewGuid():N}.db");
        var repo = Path.Combine(Path.GetTempPath(), $"whrepo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(repo);
        try
        {
            var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
                .UseSqlite($"Data Source={dbPath}").Options;
            await using var db = new WheelHouseDbContext(options);
            db.Database.EnsureCreated();

            // The legs rank the two files in OPPOSITE order: semantic.cs wins the vector leg
            // (same embedding axis as the query) but only weakly matches one keyword token,
            // while exact.cs wins the keyword leg (contains the literal SpecialToken42).
            // KeywordWeight decides whose ranking dominates the fusion.
            await File.WriteAllTextAsync(Path.Combine(repo, "semantic.cs"),
                "public class UserLogin { public void AuthenticationSignIn() { } }");
            await File.WriteAllTextAsync(Path.Combine(repo, "exact.cs"),
                "public class Billing { public void SpecialToken42() { } }");

            var embeddings = new DirectionalEmbeddings();
            var svc = new VectorSearchService(
                embeddings, new CosineVectorStore(db), new CodeCompressionService(),
                NullLogger<VectorSearchService>.Instance);
            await svc.IndexRepositoryAsync(repo);

            var keywordHeavy = await svc.SearchAsync("SpecialToken42 authentication", 1, repo, keywordWeight: 0.9);
            Assert.EndsWith("exact.cs", keywordHeavy[0].Entry.FilePath);

            var semanticHeavy = await svc.SearchAsync("SpecialToken42 authentication", 1, repo, keywordWeight: 0.1);
            Assert.EndsWith("semantic.cs", semanticHeavy[0].Entry.FilePath);
        }
        finally
        {
            try { Directory.Delete(repo, recursive: true); } catch { /* ignore */ }
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(dbPath) + "*"))
                try { File.Delete(f); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task PlanningService_Passes_Genome_Overrides_Through_The_Parameters_Bag()
    {
        var fake = new CapturingGemini();
        var planning = new GeminiPlanningService(fake);

        await planning.GenerateResearchPlanAsync("goal", "ctx", new Dictionary<string, string>
        {
            ["systemPreamble"] = "EVOLVED PREAMBLE",
            ["planningTemplate"] = "EVOLVED TEMPLATE"
        });

        Assert.Equal("EVOLVED PREAMBLE", fake.Preamble);
        Assert.Equal("EVOLVED TEMPLATE", fake.Template);

        await planning.GenerateResearchPlanAsync("goal", "ctx"); // no parameters → no overrides
        Assert.Null(fake.Preamble);
        Assert.Null(fake.Template);
    }

    [Fact]
    public async Task Genome_Preamble_And_Template_Reach_The_Gemini_Request()
    {
        var handler = new CapturingHandler();
        var service = new GeminiService(
            new HttpClient(handler),
            new GeminiOptions { ApiKey = "fake", BaseUrl = "http://unit.test/v1beta", ContextCacheMode = "off" },
            new GeminiContextCache(), NullLogger<GeminiService>.Instance);

        await service.GenerateResearchPlanAsync("the goal", "the context",
            "EVOLVED PREAMBLE", "EVOLVED TEMPLATE");

        Assert.Contains("EVOLVED PREAMBLE", handler.LastBody);
        Assert.Contains("EVOLVED TEMPLATE", handler.LastBody);
        Assert.DoesNotContain("principal architect", handler.LastBody); // default preamble replaced

        // Default genome values must produce the exact same request as passing no overrides.
        await service.GenerateResearchPlanAsync("the goal", "the context");
        var canonical = handler.LastBody;
        await service.GenerateResearchPlanAsync("the goal", "the context",
            new HarnessGenome().GeminiSystemPreamble, new HarnessGenome().PlanningPromptTemplate);
        Assert.Equal(canonical, handler.LastBody);
    }

    // ----- Fakes -----

    private sealed class DirectionalEmbeddings : IEmbeddingProvider
    {
        public string Id => "fake-directional";
        public int Dimensions => 3;
        public bool IsAvailable => true;
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
            // Query and semantic.cs share an axis; exact.cs sits elsewhere.
            => Task.FromResult(text.Contains("UserLogin") || text.Contains("authentication")
                ? new float[] { 1f, 0f, 0f }
                : new float[] { 0f, 1f, 0f });
    }

    private sealed class CapturingGemini : IGeminiService
    {
        public string? Preamble, Template;
        public bool IsConfigured => true;
        public Task<string> CompleteAsync(string prompt, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GenerateResearchPlanAsync(string goal, string ctx, CancellationToken ct = default)
        {
            Preamble = null; Template = null;
            return Task.FromResult("plan");
        }
        public Task<string> GenerateResearchPlanAsync(
            string goal, string ctx, string? systemPreamble, string? planningTemplate, CancellationToken ct = default)
        {
            Preamble = systemPreamble; Template = planningTemplate;
            return Task.FromResult("plan");
        }
        public Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(string plan, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<TaskItem>)new List<TaskItem>());
        public Task<string> TroubleshootAsync(string c, string o, string r, CancellationToken ct = default)
            => Task.FromResult("");
        public Task<float[]> EmbedAsync(string t, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<float>());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string LastBody = "";
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"candidates":[{"content":{"parts":[{"text":"PLAN"}]}}]}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}

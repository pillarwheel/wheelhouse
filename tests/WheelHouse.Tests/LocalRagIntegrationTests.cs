using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Embeddings;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using WheelHouse.Infrastructure.Vector;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Exercises the fully-local RAG path (ONNX embeddings + sqlite-vec). These tests are
/// skipped automatically on machines where the model / native extension aren't present,
/// so the suite stays green in CI but proves the stack on a configured dev box.
/// </summary>
public class LocalRagIntegrationTests
{
    private static LocalOnnxEmbeddingProvider NewLocalProvider()
        => new(new EmbeddingOptions(), NullLogger<LocalOnnxEmbeddingProvider>.Instance);

    [Fact]
    public async Task Local_Embeddings_Are_384d_Normalized_And_Semantic()
    {
        var provider = NewLocalProvider();
        if (!provider.IsAvailable) return; // model not installed → skip

        var cat = await provider.EmbedAsync("a small kitten sleeping on a warm blanket");
        var cat2 = await provider.EmbedAsync("a fluffy cat napping in the sun");
        var finance = await provider.EmbedAsync("quarterly revenue and tax depreciation schedules");

        Assert.Equal(384, cat.Length);
        Assert.Equal(1.0, Math.Sqrt(cat.Sum(x => (double)x * x)), 2); // unit length

        var simCats = VectorMath.CosineSimilarity(cat, cat2);
        var simCross = VectorMath.CosineSimilarity(cat, finance);
        Assert.True(simCats > simCross,
            $"expected cat~cat ({simCats:F3}) > cat~finance ({simCross:F3})");
    }

    [Fact]
    public async Task SqliteVec_Store_Indexes_And_Retrieves_Nearest()
    {
        var loader = new SqliteVecLoader(NullLogger<SqliteVecLoader>.Instance);
        var provider = NewLocalProvider();
        if (!loader.Available || !provider.IsAvailable) return; // skip if stack unavailable

        var dbPath = Path.Combine(Path.GetTempPath(), $"whvec_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
                .UseSqlite($"Data Source={dbPath}").Options;
            using var db = new WheelHouseDbContext(options);
            db.Database.EnsureCreated();

            using var store = new SqliteVecVectorStore(
                db, loader, provider, NullLogger<SqliteVecVectorStore>.Instance);

            await Index(store, provider, "/repo", "auth.cs", "user authentication, login, passwords and JWT tokens");
            await Index(store, provider, "/repo", "payments.cs", "stripe billing, invoices and credit card charges");
            await Index(store, provider, "/repo", "geometry.cs", "triangles, vectors and 3D mesh rendering");

            var query = await provider.EmbedAsync("how do users sign in to their account");
            var results = await store.SearchAsync(query, topN: 1, repositoryPath: "/repo");

            Assert.Single(results);
            Assert.Equal("auth.cs", results[0].Entry.FilePath);
            Assert.True(results[0].Score > 0.2, $"score was {results[0].Score:F3}");
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(dbPath) + "*"))
                try { File.Delete(f); } catch { /* ignore */ }
        }
    }

    private static async Task Index(
        SqliteVecVectorStore store, LocalOnnxEmbeddingProvider provider,
        string repo, string file, string text)
    {
        var vec = await provider.EmbedAsync(text);
        await store.UpsertAsync(new CodeIndexEntry
        {
            RepositoryPath = repo,
            FilePath = file,
            SymbolKind = "file",
            Snippet = text
        }, vec);
    }
}

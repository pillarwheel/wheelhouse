using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using WheelHouse.Infrastructure.Vector;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Proves hybrid retrieval end-to-end: exact identifiers are found even when the embedding
/// space is useless, search degrades gracefully to keyword-only when no embedding provider
/// is available, and repository filtering holds on the keyword leg. Fully offline.
/// </summary>
public class HybridSearchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"whhyb_{Guid.NewGuid():N}.db");
    private readonly string _repo = Path.Combine(Path.GetTempPath(), $"whrepo_{Guid.NewGuid():N}");
    private readonly WheelHouseDbContext _db;

    public HybridSearchTests()
    {
        Directory.CreateDirectory(_repo);
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options;
        _db = new WheelHouseDbContext(options);
        _db.Database.EnsureCreated();
    }

    private VectorSearchService NewService(IEmbeddingProvider embeddings)
        => new(
            embeddings,
            new CosineVectorStore(_db),
            new CodeCompressionService(),
            NullLogger<VectorSearchService>.Instance);

    private async Task SeedRepoAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "hashing.cs"),
            "public class Hashing { public string GetFileHashesAsync() => \"h\"; }");
        await File.WriteAllTextAsync(Path.Combine(_repo, "billing.cs"),
            "public class Billing { public void ChargeInvoice() { } }");
        await File.WriteAllTextAsync(Path.Combine(_repo, "geometry.cs"),
            "public class Geometry { public void RenderMesh() { } }");
    }

    [Fact]
    public async Task Exact_Identifier_Is_Found_Even_When_Vectors_Are_Uninformative()
    {
        await SeedRepoAsync();
        // Every text embeds to the same vector → the semantic leg can't distinguish anything.
        var svc = NewService(new UniformEmbeddingProvider());
        await svc.IndexRepositoryAsync(_repo);

        var results = await svc.SearchAsync("GetFileHashesAsync", topN: 1, _repo);

        var hit = Assert.Single(results);
        Assert.EndsWith("hashing.cs", hit.Entry.FilePath);
    }

    [Fact]
    public async Task Partial_Identifier_Substring_Matches()
    {
        await SeedRepoAsync();
        var svc = NewService(new UniformEmbeddingProvider());
        await svc.IndexRepositoryAsync(_repo);

        var results = await svc.SearchAsync("FileHashes", topN: 1, _repo);

        Assert.EndsWith("hashing.cs", Assert.Single(results).Entry.FilePath);
    }

    [Fact]
    public async Task Search_Works_Keyword_Only_When_Embeddings_Unavailable()
    {
        await SeedRepoAsync();
        var indexer = NewService(new UniformEmbeddingProvider());
        await indexer.IndexRepositoryAsync(_repo);

        // Provider vanishes (e.g. model removed): the semantic leg returns nothing,
        // but keyword search still answers.
        var svc = NewService(new UnavailableEmbeddingProvider());
        var results = await svc.SearchAsync("ChargeInvoice", topN: 2, _repo);

        Assert.NotEmpty(results);
        Assert.EndsWith("billing.cs", results[0].Entry.FilePath);
    }

    [Fact]
    public async Task Keyword_Leg_Respects_Repository_Filter()
    {
        await SeedRepoAsync();
        var otherRepo = Path.Combine(Path.GetTempPath(), $"whrepo2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(otherRepo);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(otherRepo, "other.cs"),
                "public class Other { public void ChargeInvoice() { } }");

            var svc = NewService(new UniformEmbeddingProvider());
            await svc.IndexRepositoryAsync(_repo);
            await svc.IndexRepositoryAsync(otherRepo);

            var results = await svc.SearchAsync("ChargeInvoice", topN: 5, otherRepo);

            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal(otherRepo, r.Entry.RepositoryPath));
        }
        finally
        {
            try { Directory.Delete(otherRepo, recursive: true); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_repo, recursive: true); } catch { /* ignore */ }
        foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            try { File.Delete(f); } catch { /* ignore */ }
    }

    private sealed class UniformEmbeddingProvider : IEmbeddingProvider
    {
        public string Id => "fake-uniform";
        public int Dimensions => 3;
        public bool IsAvailable => true;
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[] { 1f, 0f, 0f });
    }

    private sealed class UnavailableEmbeddingProvider : IEmbeddingProvider
    {
        public string Id => "fake-unavailable";
        public int Dimensions => 3;
        public bool IsAvailable => false;
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<float>());
    }
}

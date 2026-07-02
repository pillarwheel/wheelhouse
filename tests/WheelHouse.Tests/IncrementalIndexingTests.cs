using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using WheelHouse.Infrastructure.Vector;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Proves that re-indexing skips unchanged files (no embedding call), re-embeds edited
/// files, and still prunes deleted ones. Runs fully offline against a counting fake
/// embedding provider and the cosine store.
/// </summary>
public class IncrementalIndexingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"whinc_{Guid.NewGuid():N}.db");
    private readonly string _repo = Path.Combine(Path.GetTempPath(), $"whrepo_{Guid.NewGuid():N}");
    private readonly WheelHouseDbContext _db;

    public IncrementalIndexingTests()
    {
        Directory.CreateDirectory(_repo);
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options;
        _db = new WheelHouseDbContext(options);
        _db.Database.EnsureCreated();
    }

    private VectorSearchService NewService(CountingEmbeddingProvider embeddings)
        => new(
            embeddings,
            new CosineVectorStore(_db),
            new CodeCompressionService(),
            NullLogger<VectorSearchService>.Instance);

    [Fact]
    public async Task Reindex_Skips_Unchanged_Reembeds_Changed_And_Prunes_Deleted()
    {
        var fileA = Path.Combine(_repo, "a.cs");
        var fileB = Path.Combine(_repo, "b.cs");
        await File.WriteAllTextAsync(fileA, "public class Alpha { public int One() => 1; }");
        await File.WriteAllTextAsync(fileB, "public class Beta { public int Two() => 2; }");

        var embeddings = new CountingEmbeddingProvider();
        var svc = NewService(embeddings);

        // First pass embeds everything.
        var count = await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(2, count);
        Assert.Equal(2, embeddings.Calls);

        // Second pass: nothing changed → still reports full coverage, zero new embeddings.
        count = await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(2, count);
        Assert.Equal(2, embeddings.Calls);

        // Edit one file → exactly one re-embed.
        await File.WriteAllTextAsync(fileB, "public class Beta { public int Two() => 22; }");
        count = await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(2, count);
        Assert.Equal(3, embeddings.Calls);

        // Delete a file → pruned from the index, no extra embedding work.
        File.Delete(fileB);
        count = await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(1, count);
        Assert.Equal(3, embeddings.Calls);
        var indexed = await new CosineVectorStore(_db).GetIndexedFilesAsync(_repo);
        Assert.Equal(fileA, Assert.Single(indexed));
    }

    [Fact]
    public async Task Legacy_Rows_Without_Hash_Are_Reembedded_Once_Then_Skipped()
    {
        var fileA = Path.Combine(_repo, "a.cs");
        await File.WriteAllTextAsync(fileA, "public class Alpha { }");

        var embeddings = new CountingEmbeddingProvider();
        var svc = NewService(embeddings);
        await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(1, embeddings.Calls);

        // Simulate a pre-hash row (written before the ContentHash column existed).
        var entry = await _db.CodeIndex.SingleAsync(c => c.FilePath == fileA);
        entry.ContentHash = null;
        await _db.SaveChangesAsync();

        await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(2, embeddings.Calls); // null hash never matches → re-embedded

        await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(2, embeddings.Calls); // hash now stored → skipped
    }

    [Fact]
    public async Task Single_File_Index_Skips_When_Unchanged()
    {
        var fileA = Path.Combine(_repo, "a.cs");
        await File.WriteAllTextAsync(fileA, "public class Alpha { }");

        var embeddings = new CountingEmbeddingProvider();
        var svc = NewService(embeddings);

        Assert.Equal(1, await svc.IndexFileAsync(_repo, fileA));
        Assert.Equal(1, await svc.IndexFileAsync(_repo, fileA));
        Assert.Equal(1, embeddings.Calls);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_repo, recursive: true); } catch { /* ignore */ }
        foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            try { File.Delete(f); } catch { /* ignore */ }
    }

    /// <summary>Deterministic fake embedder that counts how often it is invoked.</summary>
    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        public string Id => "fake-counting";
        public int Dimensions => 3;
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new float[] { 1f, 0f, 0f });
        }
    }
}

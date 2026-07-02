using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using WheelHouse.Infrastructure.Vector;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Proves that large files are indexed as multiple chunk vectors (no tail truncation),
/// that chunking coexists with the content-hash skip, and that edits/deletes replace or
/// prune all of a file's chunks. Fully offline (fake embedder + cosine store).
/// </summary>
public class ChunkedIndexingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"whchunk_{Guid.NewGuid():N}.db");
    private readonly string _repo = Path.Combine(Path.GetTempPath(), $"whrepo_{Guid.NewGuid():N}");
    private readonly WheelHouseDbContext _db;

    public ChunkedIndexingTests()
    {
        Directory.CreateDirectory(_repo);
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options;
        _db = new WheelHouseDbContext(options);
        _db.Database.EnsureCreated();
    }

    private static string LargeSource(string marker, int methods = 120)
    {
        // ~6-7k chars after compression → several chunks at the 1500-char default.
        var body = string.Join('\n', Enumerable.Range(1, methods)
            .Select(i => $"    public int {marker}Method{i:D3}() => {i};"));
        return $"public class {marker}\n{{\n{body}\n    public string TailSentinel_{marker}() => \"tail\";\n}}";
    }

    private VectorSearchService NewService(CountingEmbeddingProvider embeddings)
        => new(
            embeddings,
            new CosineVectorStore(_db),
            new CodeCompressionService(),
            NullLogger<VectorSearchService>.Instance);

    [Fact]
    public async Task Large_File_Produces_Multiple_Chunks_Including_The_Tail()
    {
        var file = Path.Combine(_repo, "big.cs");
        await File.WriteAllTextAsync(file, LargeSource("Big"));

        var embeddings = new CountingEmbeddingProvider();
        var count = await NewService(embeddings).IndexRepositoryAsync(_repo);

        Assert.Equal(1, count); // still counted as one covered file
        var rows = await _db.CodeIndex.Where(c => c.FilePath == file).ToListAsync();
        Assert.True(rows.Count > 1, $"expected multiple chunks, got {rows.Count}");
        Assert.Equal(rows.Count, embeddings.Calls); // one embedding per chunk
        Assert.All(rows, r => Assert.Equal("chunk", r.SymbolKind));
        Assert.Contains(rows, r => r.Snippet.Contains("TailSentinel_Big")); // tail not truncated
        Assert.Single(rows.Select(r => r.ContentHash).Distinct()); // one file hash across chunks
    }

    [Fact]
    public async Task Chunked_Files_Still_Skip_When_Unchanged_And_Replace_When_Edited()
    {
        var file = Path.Combine(_repo, "big.cs");
        await File.WriteAllTextAsync(file, LargeSource("Big"));

        var embeddings = new CountingEmbeddingProvider();
        var svc = NewService(embeddings);

        await svc.IndexRepositoryAsync(_repo);
        var firstRunCalls = embeddings.Calls;
        var firstRunRows = await _db.CodeIndex.CountAsync(c => c.FilePath == file);

        // Unchanged → zero further embedding calls.
        await svc.IndexRepositoryAsync(_repo);
        Assert.Equal(firstRunCalls, embeddings.Calls);

        // Edited → all chunks replaced exactly once, no duplicate rows left behind.
        await File.WriteAllTextAsync(file, LargeSource("Big", methods: 130));
        await svc.IndexRepositoryAsync(_repo);
        Assert.True(embeddings.Calls > firstRunCalls);
        var rows = await _db.CodeIndex.Where(c => c.FilePath == file).ToListAsync();
        Assert.True(rows.Count >= firstRunRows);
        Assert.Single(rows.Select(r => r.ContentHash).Distinct());
        Assert.Contains(rows, r => r.Snippet.Contains("BigMethod130"));
    }

    [Fact]
    public async Task Deleting_A_Chunked_File_Prunes_All_Its_Chunks()
    {
        var file = Path.Combine(_repo, "big.cs");
        var keep = Path.Combine(_repo, "small.cs");
        await File.WriteAllTextAsync(file, LargeSource("Big"));
        await File.WriteAllTextAsync(keep, "public class Small { }");

        var svc = NewService(new CountingEmbeddingProvider());
        await svc.IndexRepositoryAsync(_repo);
        Assert.True(await _db.CodeIndex.CountAsync(c => c.FilePath == file) > 1);

        File.Delete(file);
        var count = await svc.IndexRepositoryAsync(_repo);

        Assert.Equal(1, count);
        Assert.Equal(0, await _db.CodeIndex.CountAsync(c => c.FilePath == file));
        Assert.Equal(1, await _db.CodeIndex.CountAsync(c => c.FilePath == keep));
    }

    [Fact]
    public async Task Search_Reaches_Content_That_Truncation_Used_To_Drop()
    {
        var file = Path.Combine(_repo, "big.cs");
        await File.WriteAllTextAsync(file, LargeSource("Big"));

        var embeddings = new CountingEmbeddingProvider { QueryMatchesSnippetContaining = "TailSentinel_Big" };
        var svc = NewService(embeddings);
        await svc.IndexRepositoryAsync(_repo);

        var results = await svc.SearchAsync("tail sentinel", topN: 1, _repo);

        var hit = Assert.Single(results);
        Assert.Contains("TailSentinel_Big", hit.Entry.Snippet);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_repo, recursive: true); } catch { /* ignore */ }
        foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            try { File.Delete(f); } catch { /* ignore */ }
    }

    /// <summary>
    /// Counts invocations and, when <see cref="QueryMatchesSnippetContaining"/> is set, embeds
    /// texts containing that marker (and the query itself) along one axis and everything else
    /// along another, so cosine search deterministically surfaces the marked chunk.
    /// </summary>
    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        public string Id => "fake-counting";
        public int Dimensions => 3;
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public string? QueryMatchesSnippetContaining { get; set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            var marked = QueryMatchesSnippetContaining is null
                || text.Contains(QueryMatchesSnippetContaining)
                || text.Contains("tail sentinel");
            return Task.FromResult(marked ? new float[] { 1f, 0f, 0f } : new float[] { 0f, 1f, 0f });
        }
    }
}

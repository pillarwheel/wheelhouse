using Microsoft.Extensions.Logging;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Search;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Local RAG orchestration: compresses source, embeds it via the active
/// <see cref="IEmbeddingProvider"/>, and stores/searches through the active
/// <see cref="IVectorStore"/>. Both the embedder and the store are chosen at startup,
/// so this class is agnostic to local-vs-cloud and cosine-vs-ANN.
/// </summary>
public class VectorSearchService : IVectorSearchService
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _store;
    private readonly ICodeCompressionService _compression;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        IEmbeddingProvider embeddings,
        IVectorStore store,
        ICodeCompressionService compression,
        ILogger<VectorSearchService> logger)
    {
        _embeddings = embeddings;
        _store = store;
        _compression = compression;
        _logger = logger;
    }

    public string Backend => $"{_embeddings.Id} + {_store.Backend}";

    public async Task<int> IndexFileAsync(
        string repositoryPath, string filePath, CancellationToken cancellationToken = default)
    {
        var knownHashes = await _store.GetFileHashesAsync(repositoryPath, cancellationToken);
        var (indexed, _) = await IndexFileCoreAsync(repositoryPath, filePath, knownHashes, cancellationToken);
        return indexed;
    }

    /// <summary>
    /// Indexes one file, skipping the embedding calls when the stored content hash matches.
    /// Large files are split into overlapping chunks and embedded per-chunk, so nothing is
    /// truncated away. Returns (indexed, embedded): indexed=1 whenever the file is covered by
    /// the index afterwards (fresh or unchanged), embedded=1 only when new vectors were computed.
    /// </summary>
    private async Task<(int Indexed, int Embedded)> IndexFileCoreAsync(
        string repositoryPath, string filePath,
        IReadOnlyDictionary<string, string?> knownHashes, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return (0, 0);

        var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return (0, 0);

        var snippet = _compression.CompressForFile(raw, filePath);

        var hash = ComputeContentHash(snippet);
        if (knownHashes.TryGetValue(filePath, out var stored) && stored == hash)
            return (1, 0); // up to date — skip the embedding calls entirely

        var chunks = CodeChunker.Split(snippet);
        if (chunks.Count == 0) return (0, 0);

        var name = Path.GetFileNameWithoutExtension(filePath);
        var entries = new List<CodeIndexEntry>(chunks.Count);
        var vectors = new List<float[]>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = await _embeddings.EmbedAsync(chunks[i], cancellationToken);
            if (vector.Length == 0) return (0, 0); // provider unavailable — leave old rows intact

            entries.Add(new CodeIndexEntry
            {
                RepositoryPath = repositoryPath,
                FilePath = filePath,
                SymbolName = chunks.Count == 1 ? name : $"{name}#{i + 1}",
                SymbolKind = chunks.Count == 1 ? "file" : "chunk",
                Snippet = chunks[i],
                ContentHash = hash,
                IndexedAt = DateTime.UtcNow
            });
            vectors.Add(vector);
        }

        await _store.ReplaceFileAsync(repositoryPath, filePath, entries, vectors, cancellationToken);
        return (1, 1);
    }

    public async Task<int> IndexRepositoryAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repositoryPath)) return 0;

        var knownHashes = await _store.GetFileHashesAsync(repositoryPath, cancellationToken);
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        var embedded = 0;
        foreach (var file in EnumerateSourceFiles(repositoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            onDisk.Add(file);
            try
            {
                var (i, e) = await IndexFileCoreAsync(repositoryPath, file, knownHashes, cancellationToken);
                count += i;
                embedded += e;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index {File}", file);
            }
        }

        await PruneDeletedFilesAsync(repositoryPath, knownHashes.Keys, onDisk, cancellationToken);
        _logger.LogInformation(
            "Indexed {Repo}: {Count} files covered, {Embedded} embedded, {Skipped} unchanged.",
            repositoryPath, count, embedded, count - embedded);
        return count;
    }

    private static string ComputeContentHash(string snippet)
        => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snippet)));

    /// <summary>
    /// Drops vectors for files that are indexed but no longer present on disk (deleted, renamed,
    /// or now excluded), so stale embeddings can't surface in search results.
    /// </summary>
    private async Task PruneDeletedFilesAsync(
        string repositoryPath, IEnumerable<string> indexed, HashSet<string> onDisk,
        CancellationToken cancellationToken)
    {
        foreach (var path in indexed)
        {
            if (onDisk.Contains(path)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            try { await _store.DeleteByFileAsync(repositoryPath, path, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to prune stale index entry {File}", path); }
        }
    }

    public async Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        string query, int topN = 5, string? repositoryPath = null,
        CancellationToken cancellationToken = default)
    {
        // Hybrid retrieval: over-fetch both legs, then rank-fuse. Either leg alone still works —
        // keyword search covers exact identifiers (and machines with no embedding provider),
        // vector search covers semantic paraphrases. The fetch floor matters: fusion can only
        // reward agreement between the legs on entries both of them actually returned.
        var fetch = Math.Max(topN * 2, 20);

        var queryVec = await _embeddings.EmbedAsync(query, cancellationToken);
        IReadOnlyList<CodeSearchResult> semantic = queryVec.Length == 0
            ? Array.Empty<CodeSearchResult>()
            : await _store.SearchAsync(queryVec, fetch, repositoryPath, cancellationToken);

        var keyword = await _store.KeywordSearchAsync(query, fetch, repositoryPath, cancellationToken);

        if (keyword.Count == 0) return semantic.Take(topN).ToList();
        if (semantic.Count == 0) return keyword.Take(topN).ToList();
        return SearchFusion.ReciprocalRankFusion(semantic, keyword, topN);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs = Array.Empty<string>();
            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch { continue; }

            foreach (var sub in subDirs)
                if (!IndexableFiles.IsIgnoredDir(Path.GetFileName(sub)))
                    stack.Push(sub);

            foreach (var file in files)
                if (IndexableFiles.HasSupportedExtension(file))
                    yield return file;
        }
    }
}

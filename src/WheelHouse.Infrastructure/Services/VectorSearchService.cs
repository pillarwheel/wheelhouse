using Microsoft.Extensions.Logging;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Local RAG orchestration: compresses source, embeds it via the active
/// <see cref="IEmbeddingProvider"/>, and stores/searches through the active
/// <see cref="IVectorStore"/>. Both the embedder and the store are chosen at startup,
/// so this class is agnostic to local-vs-cloud and cosine-vs-ANN.
/// </summary>
public class VectorSearchService : IVectorSearchService
{
    private static readonly string[] SupportedExtensions =
        { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".razor", ".md", ".json", ".yaml", ".yml" };

    private static readonly string[] IgnoredDirs =
        { "bin", "obj", "node_modules", ".git", ".vs", "dist", "build" };

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
        if (!File.Exists(filePath)) return 0;

        var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        var snippet = _compression.CompressForFile(raw, filePath);
        if (snippet.Length > 8000) snippet = snippet[..8000];

        var embedding = await _embeddings.EmbedAsync(snippet, cancellationToken);
        if (embedding.Length == 0) return 0;

        var entry = new CodeIndexEntry
        {
            RepositoryPath = repositoryPath,
            FilePath = filePath,
            SymbolName = Path.GetFileNameWithoutExtension(filePath),
            SymbolKind = "file",
            Snippet = snippet,
            IndexedAt = DateTime.UtcNow
        };

        await _store.UpsertAsync(entry, embedding, cancellationToken);
        return 1;
    }

    public async Task<int> IndexRepositoryAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repositoryPath)) return 0;

        var count = 0;
        foreach (var file in EnumerateSourceFiles(repositoryPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                count += await IndexFileAsync(repositoryPath, file, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index {File}", file);
            }
        }
        return count;
    }

    public async Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        string query, int topN = 5, string? repositoryPath = null,
        CancellationToken cancellationToken = default)
    {
        var queryVec = await _embeddings.EmbedAsync(query, cancellationToken);
        if (queryVec.Length == 0) return Array.Empty<CodeSearchResult>();
        return await _store.SearchAsync(queryVec, topN, repositoryPath, cancellationToken);
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
            {
                var name = Path.GetFileName(sub);
                if (!IgnoredDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    stack.Push(sub);
            }

            foreach (var file in files)
                if (SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return file;
        }
    }
}

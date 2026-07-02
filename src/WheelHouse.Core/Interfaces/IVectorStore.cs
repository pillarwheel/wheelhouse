using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Storage + nearest-neighbour search for code embeddings. Implementations range from a
/// brute-force cosine scan to a dedicated ANN index (sqlite-vec). All run on-device.
/// </summary>
public interface IVectorStore
{
    /// <summary>Human-readable backend name, e.g. "sqlite-vec" or "sqlite-cosine".</summary>
    string Backend { get; }

    /// <summary>Inserts or replaces the vector + metadata for a single indexed snippet.</summary>
    Task UpsertAsync(CodeIndexEntry entry, float[] vector, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically replaces all indexed rows for one file with the given chunk entries and their
    /// vectors. <paramref name="entries"/> and <paramref name="vectors"/> are parallel lists.
    /// </summary>
    Task ReplaceFileAsync(
        string repositoryPath,
        string filePath,
        IReadOnlyList<CodeIndexEntry> entries,
        IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken = default);

    /// <summary>Removes any stored vectors for a given file.</summary>
    Task DeleteByFileAsync(string repositoryPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Returns the distinct file paths currently indexed for a repository.</summary>
    Task<IReadOnlyList<string>> GetIndexedFilesAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns file path → stored content hash for every file indexed in a repository.
    /// Hashes are null for entries written before content hashing existed.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> GetFileHashesAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>Returns the top-N nearest snippets to <paramref name="queryVector"/>.</summary>
    Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        float[] queryVector,
        int topN,
        string? repositoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lexical leg of hybrid retrieval: case-insensitive substring search over indexed
    /// snippets and file paths. Works without any embedding provider.
    /// </summary>
    Task<IReadOnlyList<CodeSearchResult>> KeywordSearchAsync(
        string query,
        int topN,
        string? repositoryPath,
        CancellationToken cancellationToken = default);
}

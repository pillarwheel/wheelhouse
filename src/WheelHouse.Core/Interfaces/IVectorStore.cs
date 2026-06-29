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

    /// <summary>Removes any stored vectors for a given file.</summary>
    Task DeleteByFileAsync(string repositoryPath, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Returns the distinct file paths currently indexed for a repository.</summary>
    Task<IReadOnlyList<string>> GetIndexedFilesAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>Returns the top-N nearest snippets to <paramref name="queryVector"/>.</summary>
    Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        float[] queryVector,
        int topN,
        string? repositoryPath,
        CancellationToken cancellationToken = default);
}

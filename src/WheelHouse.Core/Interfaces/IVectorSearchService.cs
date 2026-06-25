using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>A single RAG search hit with its similarity score.</summary>
/// <param name="Entry">The matched code index entry.</param>
/// <param name="Score">Cosine similarity in [-1, 1]; higher is closer.</param>
public record CodeSearchResult(CodeIndexEntry Entry, double Score);

/// <summary>Local semantic code search over a pluggable embedding provider + vector store.</summary>
public interface IVectorSearchService
{
    /// <summary>Describes the active backend, e.g. "local-all-MiniLM-L6-v2 + sqlite-vec".</summary>
    string Backend { get; }

    /// <summary>Indexes a single source file (chunking + embedding handled internally).</summary>
    Task<int> IndexFileAsync(
        string repositoryPath,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>Walks a repository and indexes supported source files.</summary>
    Task<int> IndexRepositoryAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the top-N most semantically similar snippets to the query.</summary>
    Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        string query,
        int topN = 5,
        string? repositoryPath = null,
        CancellationToken cancellationToken = default);
}

/// <summary>AST/regex-based compression of source code prior to LLM transmission.</summary>
public interface ICodeCompressionService
{
    /// <summary>Strips comments, doc-comments and redundant whitespace from C#/C-style source.</summary>
    string Compress(string source);

    /// <summary>
    /// Compresses source using the comment style appropriate for the file's extension
    /// (C-style for .cs/.js/.ts/…, hash for .py/.rb/.sh). Returns the source unchanged for
    /// extensions without a known comment style.
    /// </summary>
    string CompressForFile(string source, string filePath);
}

using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>A single RAG search hit with its relevance score.</summary>
/// <param name="Entry">The matched code index entry.</param>
/// <param name="Score">
/// Relevance; higher is better. Cosine similarity in [-1, 1] for a pure vector search,
/// token-match fraction in (0, 1] for a pure keyword search, or a reciprocal-rank fusion
/// score when both legs contributed. Comparable within one result list, not across lists.
/// </param>
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

    /// <summary>Returns the top-N most relevant snippets for the query (hybrid retrieval).</summary>
    /// <param name="keywordWeight">
    /// Balance between the keyword and semantic legs in [0, 1]: 0.5 is neutral (classic RRF),
    /// higher favors exact-identifier matches, lower favors semantic similarity. Tunable via
    /// the Darwin genome's <c>KeywordWeight</c>.
    /// </param>
    Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        string query,
        int topN = 5,
        string? repositoryPath = null,
        double keywordWeight = 0.5,
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

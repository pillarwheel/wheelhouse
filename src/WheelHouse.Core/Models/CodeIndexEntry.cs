namespace WheelHouse.Core.Models;

/// <summary>
/// A semantically indexed code snippet used for local RAG queries across repositories.
/// The embedding vector is stored as JSON for portability with plain SQLite.
/// </summary>
public class CodeIndexEntry
{
    public int Id { get; set; }

    public string RepositoryPath { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Class / method / file name this snippet represents.</summary>
    public string? SymbolName { get; set; }

    /// <summary>"class", "method", "interface", "file", etc.</summary>
    public string SymbolKind { get; set; } = "file";

    /// <summary>The (optionally compressed) source text that was embedded.</summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>Serialized float[] embedding vector.</summary>
    public string EmbeddingJson { get; set; } = "[]";

    /// <summary>
    /// SHA-256 (hex) of the embedded snippet, used to skip re-embedding unchanged files.
    /// Null for rows written before hashing existed; those re-embed once and then carry a hash.
    /// </summary>
    public string? ContentHash { get; set; }

    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
}

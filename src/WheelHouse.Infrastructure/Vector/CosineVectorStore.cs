using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;

namespace WheelHouse.Infrastructure.Vector;

/// <summary>
/// Brute-force vector store: keeps embeddings as JSON in the <c>CodeIndex</c> table and
/// ranks by cosine similarity in-process. Always available (no native extension required).
/// </summary>
public class CosineVectorStore : IVectorStore
{
    private readonly WheelHouseDbContext _db;

    public CosineVectorStore(WheelHouseDbContext db) => _db = db;

    public string Backend => "sqlite-cosine";

    public Task UpsertAsync(CodeIndexEntry entry, float[] vector, CancellationToken cancellationToken = default)
        => ReplaceFileAsync(entry.RepositoryPath, entry.FilePath, new[] { entry }, new[] { vector }, cancellationToken);

    public async Task ReplaceFileAsync(
        string repositoryPath, string filePath,
        IReadOnlyList<CodeIndexEntry> entries, IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken = default)
    {
        await DeleteByFileAsync(repositoryPath, filePath, cancellationToken);
        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].EmbeddingJson = JsonSerializer.Serialize(vectors[i]);
            _db.CodeIndex.Add(entries[i]);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByFileAsync(string repositoryPath, string filePath, CancellationToken cancellationToken = default)
    {
        var existing = await _db.CodeIndex
            .Where(c => c.RepositoryPath == repositoryPath && c.FilePath == filePath)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _db.CodeIndex.RemoveRange(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> GetIndexedFilesAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
        => await _db.CodeIndex
            .Where(c => c.RepositoryPath == repositoryPath)
            .Select(c => c.FilePath)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, string?>> GetFileHashesAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
    {
        var rows = await _db.CodeIndex
            .Where(c => c.RepositoryPath == repositoryPath)
            .Select(c => new { c.FilePath, c.ContentHash })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) map[r.FilePath] = r.ContentHash;
        return map;
    }

    public Task<IReadOnlyList<CodeSearchResult>> KeywordSearchAsync(
        string query, int topN, string? repositoryPath, CancellationToken cancellationToken = default)
        => CodeIndexKeywordSearch.SearchAsync(_db, query, topN, repositoryPath, cancellationToken);

    public async Task<IReadOnlyList<CodeSearchResult>> SearchAsync(
        float[] queryVector, int topN, string? repositoryPath, CancellationToken cancellationToken = default)
    {
        var q = _db.CodeIndex.AsQueryable();
        if (!string.IsNullOrWhiteSpace(repositoryPath))
            q = q.Where(c => c.RepositoryPath == repositoryPath);

        var entries = await q.ToListAsync(cancellationToken);
        return entries
            .Select(e =>
            {
                var vec = JsonSerializer.Deserialize<float[]>(e.EmbeddingJson) ?? Array.Empty<float>();
                return new CodeSearchResult(e, VectorMath.CosineSimilarity(queryVector, vec));
            })
            .OrderByDescending(r => r.Score)
            .Take(topN)
            .ToList();
    }
}

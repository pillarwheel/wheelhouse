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

    public async Task UpsertAsync(CodeIndexEntry entry, float[] vector, CancellationToken cancellationToken = default)
    {
        await DeleteByFileAsync(entry.RepositoryPath, entry.FilePath, cancellationToken);
        entry.EmbeddingJson = JsonSerializer.Serialize(vector);
        _db.CodeIndex.Add(entry);
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

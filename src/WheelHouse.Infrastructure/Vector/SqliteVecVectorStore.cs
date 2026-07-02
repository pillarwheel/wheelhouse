using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Vector;

/// <summary>
/// Dedicated on-device ANN vector store backed by the sqlite-vec (<c>vec0</c>) extension.
/// Vectors live in a <c>vec0</c> virtual table keyed by <see cref="CodeIndexEntry.Id"/>;
/// snippet metadata stays in the EF-managed <c>CodeIndex</c> table.
/// </summary>
public sealed class SqliteVecVectorStore : IVectorStore, IDisposable
{
    private const string VecTable = "whvec";

    private readonly WheelHouseDbContext _db;
    private readonly SqliteVecLoader _loader;
    private readonly IEmbeddingProvider _embeddings;
    private readonly ILogger<SqliteVecVectorStore> _logger;

    private SqliteConnection? _conn;
    private bool _initialized;
    private readonly object _gate = new();

    public SqliteVecVectorStore(
        WheelHouseDbContext db,
        SqliteVecLoader loader,
        IEmbeddingProvider embeddings,
        ILogger<SqliteVecVectorStore> logger)
    {
        _db = db;
        _loader = loader;
        _embeddings = embeddings;
        _logger = logger;
    }

    public string Backend => "sqlite-vec";

    private int Dim => _embeddings.Dimensions;

    public Task UpsertAsync(CodeIndexEntry entry, float[] vector, CancellationToken cancellationToken = default)
        => ReplaceFileAsync(entry.RepositoryPath, entry.FilePath, new[] { entry }, new[] { vector }, cancellationToken);

    public async Task ReplaceFileAsync(
        string repositoryPath, string filePath,
        IReadOnlyList<CodeIndexEntry> entries, IReadOnlyList<float[]> vectors,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // Replace any prior rows for this file (metadata + vectors).
        await DeleteByFileAsync(repositoryPath, filePath, cancellationToken);

        foreach (var entry in entries)
        {
            entry.EmbeddingJson = "[]"; // vectors live in the vec0 table, not here
            _db.CodeIndex.Add(entry);
        }
        await _db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < entries.Count; i++)
        {
            await using var cmd = _conn!.CreateCommand();
            cmd.CommandText = $"INSERT INTO {VecTable}(rowid, embedding) VALUES (@id, @vec)";
            cmd.Parameters.AddWithValue("@id", entries[i].Id);
            cmd.Parameters.AddWithValue("@vec", JsonSerializer.Serialize(vectors[i]));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task DeleteByFileAsync(string repositoryPath, string filePath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var existing = await _db.CodeIndex
            .Where(c => c.RepositoryPath == repositoryPath && c.FilePath == filePath)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0) return;

        foreach (var id in existing)
        {
            await using var del = _conn!.CreateCommand();
            del.CommandText = $"DELETE FROM {VecTable} WHERE rowid = @id";
            del.Parameters.AddWithValue("@id", id);
            await del.ExecuteNonQueryAsync(cancellationToken);
        }

        await _db.CodeIndex.Where(c => existing.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetIndexedFilesAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _db.CodeIndex
            .Where(c => c.RepositoryPath == repositoryPath)
            .Select(c => c.FilePath)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetFileHashesAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
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
        await EnsureInitializedAsync(cancellationToken);

        // Over-fetch when filtering by repo, since KNN happens before the metadata join.
        var k = string.IsNullOrWhiteSpace(repositoryPath) ? topN : Math.Min(topN * 8, 200);

        var hits = new List<(long Id, double Distance)>();
        await using (var cmd = _conn!.CreateCommand())
        {
            cmd.CommandText =
                $"SELECT rowid, distance FROM {VecTable} WHERE embedding MATCH @q ORDER BY distance LIMIT @k";
            cmd.Parameters.AddWithValue("@q", JsonSerializer.Serialize(queryVector));
            cmd.Parameters.AddWithValue("@k", k);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                hits.Add((reader.GetInt64(0), reader.GetDouble(1)));
        }

        if (hits.Count == 0) return Array.Empty<CodeSearchResult>();

        var ids = hits.Select(h => h.Id).ToList();
        var meta = await _db.CodeIndex.Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => (long)c.Id, cancellationToken);

        var results = new List<CodeSearchResult>();
        foreach (var (id, distance) in hits)
        {
            if (!meta.TryGetValue(id, out var entry)) continue;
            if (!string.IsNullOrWhiteSpace(repositoryPath) && entry.RepositoryPath != repositoryPath) continue;
            // cosine distance → similarity
            results.Add(new CodeSearchResult(entry, 1.0 - distance));
            if (results.Count >= topN) break;
        }
        return results;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;
            _initialized = true; // set early; failures throw and are surfaced by the caller
        }

        var connectionString = _db.Database.GetDbConnection().ConnectionString;
        _conn = new SqliteConnection(connectionString);
        await _conn.OpenAsync(cancellationToken);
        _loader.Load(_conn);

        await Exec("PRAGMA busy_timeout = 5000;", cancellationToken);
        await Exec("CREATE TABLE IF NOT EXISTS whvec_config (dim INTEGER);", cancellationToken);

        var storedDim = await ScalarInt("SELECT dim FROM whvec_config LIMIT 1;", cancellationToken);
        if (storedDim is null)
        {
            await CreateVecTable(cancellationToken);
            await Exec("INSERT INTO whvec_config(dim) VALUES (@d);", cancellationToken, ("@d", Dim));
        }
        else if (storedDim != Dim)
        {
            // Embedding dimension changed (e.g. switched providers): rebuild from scratch.
            _logger.LogWarning("Embedding dim changed {Old}→{New}; rebuilding vector index.", storedDim, Dim);
            await Exec($"DROP TABLE IF EXISTS {VecTable};", cancellationToken);
            await CreateVecTable(cancellationToken);
            await Exec("UPDATE whvec_config SET dim = @d;", cancellationToken, ("@d", Dim));
            await _db.CodeIndex.ExecuteDeleteAsync(cancellationToken);
        }
    }

    private Task CreateVecTable(CancellationToken ct) => Exec(
        $"CREATE VIRTUAL TABLE IF NOT EXISTS {VecTable} USING vec0(embedding float[{Dim}] distance_metric=cosine);", ct);

    private async Task Exec(string sql, CancellationToken ct, params (string Name, object Value)[] ps)
    {
        await using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<int?> ScalarInt(string sql, CancellationToken ct)
    {
        await using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || result is DBNull ? null : Convert.ToInt32(result);
    }

    public void Dispose() => _conn?.Dispose();
}

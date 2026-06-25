using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WheelHouse.Core;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Background worker that indexes workspaces (into the local RAG vector store) as they're queued,
/// so newly added repositories become searchable / available to plan generation automatically.
/// </summary>
public class WorkspaceIndexingService : BackgroundService
{
    private readonly IWorkspaceIndexQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmbeddingProvider _embeddings;
    private readonly ILogger<WorkspaceIndexingService> _logger;

    public WorkspaceIndexingService(
        IWorkspaceIndexQueue queue,
        IServiceScopeFactory scopeFactory,
        IEmbeddingProvider embeddings,
        ILogger<WorkspaceIndexingService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _embeddings = embeddings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int workspaceId;
            try { workspaceId = await _queue.DequeueAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }

            try { await ProcessAsync(workspaceId, stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Indexing workspace {Id} failed.", workspaceId); }
        }
    }

    private async Task ProcessAsync(int workspaceId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WheelHouseDbContext>();

        var workspace = await db.Workspaces.FindAsync(new object?[] { workspaceId }, cancellationToken);
        if (workspace is null) return;

        if (!_embeddings.IsAvailable)
        {
            workspace.IndexStatus = IndexState.Unavailable;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Skipped indexing {Path}: no embedding provider available.", workspace.AbsolutePath);
            return;
        }

        workspace.IndexStatus = IndexState.Indexing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var search = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();
            var count = await search.IndexRepositoryAsync(workspace.AbsolutePath, cancellationToken);

            workspace.IndexStatus = IndexState.Indexed;
            workspace.IndexedFileCount = count;
            workspace.LastIndexedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Indexed {Count} files from {Path}.", count, workspace.AbsolutePath);
        }
        catch (Exception ex)
        {
            workspace.IndexStatus = IndexState.Failed;
            await db.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning(ex, "Indexing {Path} failed.", workspace.AbsolutePath);
        }
    }
}

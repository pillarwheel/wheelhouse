using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Services;

/// <summary>Configures the workspace file watcher (event-driven incremental re-indexing).</summary>
public class WorkspaceWatchOptions
{
    /// <summary>"auto"/"on" watch workspace roots (default); "off" disables watching.</summary>
    public string Mode { get; set; } =
        Environment.GetEnvironmentVariable("WHEELHOUSE_WATCH") ?? "auto";

    /// <summary>How long a repository must stay quiet after a change before re-indexing.</summary>
    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How often the watcher set is reconciled against the workspace table.</summary>
    public TimeSpan ReconcileInterval { get; set; } = TimeSpan.FromSeconds(30);

    public bool Enabled => !string.Equals(Mode, "off", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Keeps a <see cref="RepositoryWatcher"/> on every workspace root and enqueues the workspace
/// for background re-indexing when its source files change. Combined with content-hash skip,
/// this keeps the RAG index continuously fresh at near-zero embedding cost. Watchers are
/// reconciled periodically so added/removed/moved workspaces are picked up without a restart.
/// </summary>
public class WorkspaceWatchService : BackgroundService
{
    private readonly IWorkspaceIndexQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkspaceWatchOptions _options;
    private readonly ILogger<WorkspaceWatchService> _logger;
    private readonly Dictionary<int, (string Path, RepositoryWatcher Watcher)> _watchers = new();

    public WorkspaceWatchService(
        IWorkspaceIndexQueue queue,
        IServiceScopeFactory scopeFactory,
        WorkspaceWatchOptions options,
        ILogger<WorkspaceWatchService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Workspace file watching disabled (WHEELHOUSE_WATCH=off).");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ReconcileAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Workspace watcher reconcile failed."); }

                try { await Task.Delay(_options.ReconcileInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            foreach (var (_, entry) in _watchers) entry.Watcher.Dispose();
            _watchers.Clear();
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WheelHouseDbContext>();
        var workspaces = await db.Workspaces
            .Select(w => new { w.Id, w.AbsolutePath })
            .ToListAsync(cancellationToken);

        var wanted = workspaces
            .Where(w => Directory.Exists(w.AbsolutePath))
            .ToDictionary(w => w.Id, w => w.AbsolutePath);

        // Drop watchers for removed workspaces or ones whose path changed.
        foreach (var id in _watchers.Keys.ToList())
        {
            if (wanted.TryGetValue(id, out var path) &&
                string.Equals(path, _watchers[id].Path, StringComparison.OrdinalIgnoreCase))
                continue;
            _watchers[id].Watcher.Dispose();
            _watchers.Remove(id);
        }

        foreach (var (id, path) in wanted)
        {
            if (_watchers.ContainsKey(id)) continue;
            try
            {
                var watcher = new RepositoryWatcher(path, _options.QuietPeriod, () =>
                {
                    _logger.LogDebug("Source change in {Path}; scheduling re-index.", path);
                    _queue.Enqueue(id);
                });
                _watchers[id] = (path, watcher);
                _logger.LogInformation("Watching {Path} for source changes.", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not watch {Path}.", path);
            }
        }
    }
}

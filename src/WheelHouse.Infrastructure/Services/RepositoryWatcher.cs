using WheelHouse.Core.Search;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Watches one repository root for changes to indexable source files and raises a single
/// debounced callback per burst of changes (a save, a branch switch, a formatter run…).
/// The callback is expected to schedule a re-index, which the content-hash skip makes
/// cheap for everything that didn't actually change.
/// </summary>
public sealed class RepositoryWatcher : IDisposable
{
    private readonly string _root;
    private readonly TimeSpan _quietPeriod;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;

    public RepositoryWatcher(string rootPath, TimeSpan quietPeriod, Action onChanged)
    {
        _root = rootPath;
        _quietPeriod = quietPeriod;
        _debounce = new Timer(_ => onChanged(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += (_, e) => OnEvent(e.FullPath);
        _watcher.Changed += (_, e) => OnEvent(e.FullPath);
        _watcher.Deleted += (_, e) => OnEvent(e.FullPath);
        _watcher.Renamed += (_, e) => { OnEvent(e.OldFullPath); OnEvent(e.FullPath); };
        // Buffer overflow (huge burst): events were lost, so just schedule a re-index —
        // the full pass reconciles adds, edits and deletes anyway.
        _watcher.Error += (_, _) => Kick();
        _watcher.EnableRaisingEvents = true;
    }

    private void OnEvent(string fullPath)
    {
        if (IndexableFiles.IsIndexable(_root, fullPath)) Kick();
    }

    /// <summary>(Re)starts the quiet-period countdown; the callback fires once it elapses.</summary>
    private void Kick() => _debounce.Change(_quietPeriod, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}

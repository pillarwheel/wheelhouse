using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>
/// Exercises the real <see cref="RepositoryWatcher"/> against a temp directory: indexable
/// changes fire one debounced callback, ignored paths fire nothing.
/// </summary>
public class RepositoryWatcherTests : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan FireWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SilenceWait = TimeSpan.FromMilliseconds(1500);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"whwatch_{Guid.NewGuid():N}");

    public RepositoryWatcherTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Burst_Of_Source_Changes_Fires_One_Debounced_Callback()
    {
        var fired = 0;
        using var gate = new SemaphoreSlim(0);
        using var watcher = new RepositoryWatcher(_root, Quiet, () =>
        {
            Interlocked.Increment(ref fired);
            gate.Release();
        });

        for (var i = 0; i < 5; i++)
            await File.WriteAllTextAsync(Path.Combine(_root, $"f{i}.cs"), $"class C{i} {{ }}");

        Assert.True(await gate.WaitAsync(FireWait), "watcher never fired for a source change");
        await Task.Delay(SilenceWait); // no further events → no further fires
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Ignored_And_Unsupported_Paths_Do_Not_Fire()
    {
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        var fired = 0;
        using var watcher = new RepositoryWatcher(_root, Quiet, () => Interlocked.Increment(ref fired));

        await File.WriteAllTextAsync(Path.Combine(_root, "obj", "Generated.cs"), "class G { }");
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), "not indexable");

        await Task.Delay(SilenceWait);
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Deleting_A_Source_File_Fires()
    {
        var file = Path.Combine(_root, "gone.cs");
        await File.WriteAllTextAsync(file, "class Gone { }");

        using var gate = new SemaphoreSlim(0);
        using var watcher = new RepositoryWatcher(_root, Quiet, () => gate.Release());

        File.Delete(file);

        Assert.True(await gate.WaitAsync(FireWait), "watcher never fired for a delete");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }
}

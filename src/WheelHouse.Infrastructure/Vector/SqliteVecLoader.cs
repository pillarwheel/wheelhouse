using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace WheelHouse.Infrastructure.Vector;

/// <summary>
/// Locates and loads the native sqlite-vec extension (<c>vec0</c>). Probed once at startup;
/// if loading fails the RAG layer transparently falls back to the cosine store.
/// </summary>
public sealed class SqliteVecLoader
{
    private readonly ILogger<SqliteVecLoader> _logger;
    private readonly Lazy<string?> _libraryPath;

    public SqliteVecLoader(ILogger<SqliteVecLoader> logger)
    {
        _logger = logger;
        _libraryPath = new Lazy<string?>(FindLibrary);
        Available = ProbeOnce();
    }

    /// <summary>True when the extension successfully loaded into a probe connection.</summary>
    public bool Available { get; }

    /// <summary>Loads vec0 into an open connection. Throws on failure.</summary>
    public void Load(SqliteConnection connection)
    {
        var path = _libraryPath.Value
            ?? throw new FileNotFoundException("Native sqlite-vec library (vec0) not found.");
        connection.EnableExtensions(true);
        // sqlite-vec's exported entry point is sqlite3_vec_init, which differs from the
        // name SQLite would auto-derive from "vec0", so it must be passed explicitly.
        connection.LoadExtension(path, "sqlite3_vec_init");
    }

    private bool ProbeOnce()
    {
        if (_libraryPath.Value is null)
        {
            _logger.LogInformation("sqlite-vec native library not found; using cosine fallback.");
            return false;
        }
        try
        {
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            Load(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT vec_version();";
            var version = cmd.ExecuteScalar() as string;
            _logger.LogInformation("Loaded sqlite-vec {Version} from {Path}", version, _libraryPath.Value);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sqlite-vec failed to load; using cosine fallback.");
            return false;
        }
    }

    private string? FindLibrary()
    {
        var fileName = OperatingSystem.IsWindows() ? "vec0.dll"
            : OperatingSystem.IsMacOS() ? "vec0.dylib"
            : "vec0.so";

        var baseDir = AppContext.BaseDirectory;
        var rid = OperatingSystem.IsWindows() ? "win-x64"
            : OperatingSystem.IsMacOS() ? (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                  == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64")
            : "linux-x64";

        var candidates = new[]
        {
            Path.Combine(baseDir, "runtimes", rid, "native", fileName),
            Path.Combine(baseDir, fileName)
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Last resort: shallow recursive search under the app base directory.
        try
        {
            var hit = Directory.EnumerateFiles(baseDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (hit is not null) return hit;
        }
        catch { /* ignore */ }

        return null;
    }
}

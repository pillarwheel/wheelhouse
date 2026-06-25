namespace WheelHouse.Infrastructure.Configuration;

/// <summary>
/// Minimal, dependency-free <c>.env</c> loader. Reads <c>KEY=VALUE</c> lines and sets them
/// as process environment variables so the rest of the app (and spawned subprocesses such as
/// claude/headroom, which inherit the environment) can read them. Real OS environment
/// variables always win, so a deployed environment can override the file.
/// </summary>
public static class EnvFile
{
    /// <summary>
    /// Loads a <c>.env</c> file. If <paramref name="explicitPath"/> is null, honors the
    /// <c>WHEELHOUSE_ENV_FILE</c> variable, then searches the working directory and walks up
    /// from the app base directory. Returns the number of variables applied.
    /// </summary>
    public static int Load(string? explicitPath = null)
    {
        var path = explicitPath
                   ?? Environment.GetEnvironmentVariable("WHEELHOUSE_ENV_FILE")
                   ?? Find();
        if (path is null || !File.Exists(path)) return 0;

        var applied = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].Trim();

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            value = StripQuotes(value);

            if (key.Length == 0) continue;
            // Real OS environment takes precedence; never clobber an already-set variable.
            if (Environment.GetEnvironmentVariable(key) is not null) continue;

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }
        return applied;
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static string? Find()
    {
        foreach (var dir in CandidateDirectories())
        {
            var candidate = Path.Combine(dir, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        yield return Directory.GetCurrentDirectory();

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }
}

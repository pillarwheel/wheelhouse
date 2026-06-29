using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelHouse.Core.Agents;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Agents;

/// <summary>
/// Spawns and manages the <c>claude</c> CLI subprocess. Uses <c>-p/--print</c> with
/// <c>--output-format stream-json</c> and parses the NDJSON stream chunk-by-chunk.
///
/// When Headroom is enabled and installed, the invocation is routed through
/// <c>headroom wrap claude -- &lt;args&gt;</c> so context is compressed before it reaches the
/// model (fewer tokens). The wrapper is transparent to the NDJSON stdout we parse.
/// </summary>
public class ClaudeCliService : IAgentOrchestrator
{
    private readonly ILogger<ClaudeCliService> _logger;
    private readonly HeadroomOptions _headroom;
    private readonly IVerificationRunner _verification;
    private ResolvedExe? _claude;
    private ResolvedExe? _headroomExe;
    private bool _claudeResolved, _headroomResolved;

    public ClaudeCliService(
        ILogger<ClaudeCliService> logger, HeadroomOptions headroom, IVerificationRunner verification)
    {
        _logger = logger;
        _headroom = headroom;
        _verification = verification;
    }

    public async IAsyncEnumerable<AgentStreamEvent> RunAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agentArgs = ClaudeCommand.BuildAgentArgs(request);

        // Decide whether to route through Headroom for token compression.
        var (exe, args, compressed, resolveError) = BuildInvocation(agentArgs);
        if (exe is null)
        {
            yield return new AgentStreamEvent(AgentEventKind.Error, resolveError!, IsError: true);
            yield break;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // closed immediately so print mode doesn't wait for stdin
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        // When compressing via Headroom, prefer Claude's subscription/OAuth login: an inherited
        // ANTHROPIC_API_KEY would be sent to the proxy as a Bearer token and rejected by Anthropic (401).
        if (compressed && _headroom.UseSubscriptionAuth)
        {
            psi.Environment.Remove("ANTHROPIC_API_KEY");
            psi.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
        }

        if (request.Environment is not null)
            foreach (var kv in request.Environment)
                psi.Environment[kv.Key] = kv.Value;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var started = false;
        Exception? startError = null;
        try { started = process.Start(); }
        catch (Exception ex) { startError = ex; }

        if (!started)
        {
            yield return new AgentStreamEvent(AgentEventKind.Error,
                $"Failed to start process: {startError?.Message}", IsError: true);
            yield break;
        }

        // Signal EOF on stdin so claude -p doesn't block waiting for piped input.
        try { process.StandardInput.Close(); } catch { /* ignore */ }

        yield return new AgentStreamEvent(AgentEventKind.System,
            compressed
                ? $"Started claude via Headroom (context compression on) in {request.WorkingDirectory}"
                : $"Started claude in {request.WorkingDirectory}");

        // Drain stderr concurrently: if we only read it after the stdout loop, a child that
        // fills the (~4 KB) stderr pipe buffer before closing stdout would block on the write
        // while we block reading stdout — a classic redirect deadlock.
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync().WaitAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var evt = ParseLine(line);
            if (evt is not null) yield return evt;
        }

        string stderr;
        try { stderr = await stderrTask; } catch (OperationCanceledException) { stderr = string.Empty; }
        try { await process.WaitForExitAsync(cancellationToken); } catch (OperationCanceledException) { }

        if (!string.IsNullOrWhiteSpace(stderr))
            yield return new AgentStreamEvent(AgentEventKind.Error, stderr.Trim(),
                IsError: process.HasExited && process.ExitCode != 0);

        if (process.HasExited)
            yield return new AgentStreamEvent(
                process.ExitCode == 0 ? AgentEventKind.Result : AgentEventKind.Error,
                $"Process exited with code {process.ExitCode}.",
                IsError: process.ExitCode != 0);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ResolveClaude() is not null);

    public Task<AgentRuntimeInfo> GetRuntimeInfoAsync(CancellationToken cancellationToken = default)
    {
        var claude = ResolveClaude();
        var mode = (_headroom.Mode ?? "auto").Trim().ToLowerInvariant();
        var headroom = mode == "off" ? null : ResolveHeadroom();
        return Task.FromResult(new AgentRuntimeInfo(
            ClaudeAvailable: claude is not null,
            ClaudePath: claude?.SourcePath,
            HeadroomAvailable: headroom is not null,
            HeadroomPath: headroom?.SourcePath,
            HeadroomMode: mode,
            CompressionActive: mode != "off" && headroom is not null));
    }

    public async Task<(int ExitCode, string Output)> RunVerificationAsync(
        string command, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var result = await _verification.RunAsync(command, workingDirectory, null, cancellationToken);
        return (result.ExitCode, result.Output);
    }

    /// <summary>
    /// Resolves the executable + args, choosing Headroom when configured/available and
    /// falling back to a direct claude invocation otherwise.
    /// </summary>
    private (ResolvedExe? Exe, IReadOnlyList<string> Args, bool Compressed, string? Error) BuildInvocation(
        List<string> agentArgs)
    {
        var mode = (_headroom.Mode ?? "auto").Trim().ToLowerInvariant();
        if (mode != "off")
        {
            var headroom = ResolveHeadroom();
            if (headroom is not null)
            {
                var wrapArgs = ClaudeCommand.BuildHeadroomArgs(_headroom.WrapFlags, agentArgs);
                return (headroom, Concat(headroom.PrefixArgs, wrapArgs), true, null);
            }
            if (mode == "on")
                _logger.LogWarning("Headroom mode is 'on' but the executable was not found; running claude directly.");
        }

        var claude = ResolveClaude();
        return claude is null
            ? (null, Array.Empty<string>(), false, "Could not locate the 'claude' executable on PATH.")
            : (claude, Concat(claude.PrefixArgs, agentArgs), false, null);
    }

    private static IReadOnlyList<string> Concat(IReadOnlyList<string> prefix, IReadOnlyList<string> rest)
    {
        if (prefix.Count == 0) return rest;
        var list = new List<string>(prefix.Count + rest.Count);
        list.AddRange(prefix);
        list.AddRange(rest);
        return list;
    }

    private ResolvedExe? ResolveClaude()
    {
        if (_claudeResolved) return _claude;
        _claudeResolved = true;

        var names = OperatingSystem.IsWindows()
            ? new[] { "claude.exe", "claude.cmd", "claude.bat", "claude" }
            : new[] { "claude" };
        var extras = new[]
        {
            Path.Combine(UserProfile, ".local", "bin"),
            Path.Combine(UserProfile, "AppData", "Roaming", "npm")
        };
        _claude = ResolveExecutable(names, extras, null);
        if (_claude is null) _logger.LogWarning("claude executable not found on PATH.");
        return _claude;
    }

    private ResolvedExe? ResolveHeadroom()
    {
        if (_headroomResolved) return _headroomExe;
        _headroomResolved = true;

        if (!string.IsNullOrWhiteSpace(_headroom.ExecutablePath) && File.Exists(_headroom.ExecutablePath))
        {
            _headroomExe = BuildResolved(_headroom.ExecutablePath!);
            return _headroomExe;
        }

        var names = OperatingSystem.IsWindows()
            ? new[] { "headroom.exe", "headroom.cmd", "headroom.bat", "headroom" }
            : new[] { "headroom" };
        // pip console scripts commonly land in a Python "Scripts" dir that may not be on PATH.
        var extras = new List<string> { Path.Combine(UserProfile, ".local", "bin") };
        extras.AddRange(PythonScriptDirs());
        _headroomExe = ResolveExecutable(names, extras, null);
        if (_headroomExe is not null)
            _logger.LogInformation("Headroom resolved at {Path}", _headroomExe.SourcePath);
        return _headroomExe;
    }

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Common Windows pip "Scripts" locations for console entry points (e.g. headroom.exe).</summary>
    private static IEnumerable<string> PythonScriptDirs()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), // …\Local
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)        // …\Roaming
        };
        foreach (var root in roots)
        {
            // e.g. %LOCALAPPDATA%\Programs\Python\Python312\Scripts and %APPDATA%\Python\Python312\Scripts
            foreach (var basePath in new[] { Path.Combine(root, "Programs", "Python"), Path.Combine(root, "Python") })
            {
                if (!Directory.Exists(basePath)) continue;
                IEnumerable<string> pyDirs;
                try { pyDirs = Directory.EnumerateDirectories(basePath, "Python*"); }
                catch { continue; }
                foreach (var py in pyDirs)
                    yield return Path.Combine(py, "Scripts");
            }
        }
    }

    /// <summary>Finds an executable by candidate names across PATH plus extra directories.</summary>
    private static ResolvedExe? ResolveExecutable(string[] names, IEnumerable<string> extraDirs, string? _)
    {
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs.Concat(extraDirs))
        {
            foreach (var name in names)
            {
                try
                {
                    var full = Path.Combine(dir, name);
                    if (File.Exists(full)) return BuildResolved(full);
                }
                catch { /* ignore malformed PATH entries */ }
            }
        }
        return null;
    }

    private static ResolvedExe BuildResolved(string path)
    {
        // .cmd/.bat must be launched via cmd.exe with UseShellExecute=false.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cmd" or ".bat"
            ? new ResolvedExe("cmd.exe", new[] { "/c", path }, path)
            : new ResolvedExe(path, Array.Empty<string>(), path);
    }

    private static AgentStreamEvent? ParseLine(string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException)
        {
            // Non-JSON lines (e.g. the Headroom wrap banner) are informational, not model output.
            return new AgentStreamEvent(AgentEventKind.System, line, RawJson: line);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl))
                return new AgentStreamEvent(AgentEventKind.System, line, RawJson: line);

            var type = typeEl.GetString();
            switch (type)
            {
                case "system":
                    var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : "system";
                    return new AgentStreamEvent(AgentEventKind.System, $"[system:{subtype}]", RawJson: line);

                case "assistant":
                case "user":
                    return ParseMessage(root, line);

                case "result":
                    var isError = root.TryGetProperty("is_error", out var ie) && ie.GetBoolean();
                    var resultText = root.TryGetProperty("result", out var r) ? r.GetString() : "(done)";
                    return new AgentStreamEvent(
                        isError ? AgentEventKind.Error : AgentEventKind.Result,
                        resultText ?? "(done)", IsError: isError, RawJson: line);

                default:
                    return new AgentStreamEvent(AgentEventKind.System, $"[{type}]", RawJson: line);
            }
        }
    }

    private static AgentStreamEvent? ParseMessage(JsonElement root, string line)
    {
        if (!root.TryGetProperty("message", out var msg) ||
            !msg.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var block in content.EnumerateArray())
        {
            var blockType = block.TryGetProperty("type", out var bt) ? bt.GetString() : null;
            switch (blockType)
            {
                case "text":
                    var text = block.TryGetProperty("text", out var t) ? t.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text))
                        return new AgentStreamEvent(AgentEventKind.AssistantText, text!, RawJson: line);
                    break;
                case "tool_use":
                    var tool = block.TryGetProperty("name", out var n) ? n.GetString() : "tool";
                    return new AgentStreamEvent(AgentEventKind.ToolUse,
                        $"→ {tool}", ToolName: tool, RawJson: line);
                case "tool_result":
                    return new AgentStreamEvent(AgentEventKind.ToolResult, "← tool result", RawJson: line);
            }
        }
        return null;
    }

    private sealed record ResolvedExe(string FileName, IReadOnlyList<string> PrefixArgs, string SourcePath);
}

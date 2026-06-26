using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Services;

/// <summary>Runs the <c>git</c> CLI in a working directory for session isolation and rollback.</summary>
public class GitService : IGitService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ILogger<GitService> _logger;

    public GitService(ILogger<GitService> logger) => _logger = logger;

    public async Task<bool> IsRepositoryAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var (ok, output) = await RunAsync(repositoryPath, cancellationToken, "rev-parse", "--is-inside-work-tree");
        return ok && output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<GitStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        if (!await IsRepositoryAsync(repositoryPath, cancellationToken))
            return new GitStatus(false, string.Empty, true);

        var branch = await RunAsync(repositoryPath, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD");
        var porcelain = await RunAsync(repositoryPath, cancellationToken, "status", "--porcelain");
        var clean = porcelain.Success && string.IsNullOrWhiteSpace(porcelain.Output);
        return new GitStatus(true, branch.Output.Trim(), clean);
    }

    public Task<GitResult> CreateBranchAsync(string repositoryPath, string branch, CancellationToken cancellationToken = default)
        => RunAsync(repositoryPath, cancellationToken, "checkout", "-b", branch);

    public Task<GitResult> DiscardChangesAsync(string repositoryPath, CancellationToken cancellationToken = default)
        => RunAsync(repositoryPath, cancellationToken, "restore", ".");

    public async Task<IEnumerable<string>> GetRecentCommits(string repositoryPath, int count, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(repositoryPath, cancellationToken, "log", "-n", count.ToString(), "--oneline");
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return [];
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<GitResult> RunAsync(string repositoryPath, CancellationToken cancellationToken, params string[] args)
    {
        if (!Directory.Exists(repositoryPath))
            return new GitResult(false, "Working directory does not exist.");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "git not available or failed to start.");
            return new GitResult(false, $"git unavailable: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return new GitResult(false, "git command timed out.");
        }

        return new GitResult(process.ExitCode == 0, output.ToString().TrimEnd());
    }
}

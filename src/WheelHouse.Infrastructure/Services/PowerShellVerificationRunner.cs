using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Services;

/// <summary>
/// Executes verification commands through <c>powershell.exe</c> on Windows (falling back to
/// <c>/bin/bash</c> elsewhere), capturing combined stdout/stderr and the exit code.
/// </summary>
public class PowerShellVerificationRunner : IVerificationRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
    private readonly ILogger<PowerShellVerificationRunner> _logger;

    public PowerShellVerificationRunner(ILogger<PowerShellVerificationRunner> logger) => _logger = logger;

    public async Task<VerificationResult> RunAsync(
        string command, string workingDirectory, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new VerificationResult(0, "(no verification command)", false);

        var psi = BuildStartInfo(command, workingDirectory);
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
            _logger.LogWarning(ex, "Failed to start verification command.");
            return new VerificationResult(-1, $"Failed to start command: {ex.Message}", false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            var timedOutText = output.ToString();
            return new VerificationResult(-1,
                timedOutText + "\n[verification timed out]", TimedOut: true);
        }

        return new VerificationResult(process.ExitCode, output.ToString().TrimEnd(), false);
    }

    private static ProcessStartInfo BuildStartInfo(string command, string workingDirectory)
    {
        var dir = Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.CurrentDirectory;

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }
        return psi;
    }
}

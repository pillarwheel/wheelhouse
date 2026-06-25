namespace WheelHouse.Core.Interfaces;

/// <summary>Outcome of a verification command run.</summary>
/// <param name="ExitCode">Process exit code (0 = success).</param>
/// <param name="Output">Combined stdout + stderr.</param>
/// <param name="TimedOut">True if the command was killed for exceeding the timeout.</param>
public record VerificationResult(int ExitCode, string Output, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

/// <summary>
/// Runs Test-Driven Handoff verification commands (e.g. <c>dotnet test --filter X</c>) in a
/// workspace directory and reports the combined output and exit code.
/// </summary>
public interface IVerificationRunner
{
    Task<VerificationResult> RunAsync(
        string command,
        string workingDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

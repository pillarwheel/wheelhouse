namespace WheelHouse.Core.Interfaces;

/// <summary>Outcome of a git command.</summary>
public record GitResult(bool Success, string Output);

/// <summary>Git state of a working directory.</summary>
/// <param name="IsRepo">True if the directory is inside a git work tree.</param>
/// <param name="Branch">Current branch (empty when not a repo).</param>
/// <param name="Clean">True when there are no uncommitted changes.</param>
public record GitStatus(bool IsRepo, string Branch, bool Clean);

/// <summary>
/// Thin wrapper over the <c>git</c> CLI for session branch isolation and safe rollback of a
/// coding agent's edits.
/// </summary>
public interface IGitService
{
    /// <summary>Returns whether git is available and the path is a git work tree.</summary>
    Task<bool> IsRepositoryAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>Returns the repo's branch + clean/dirty state.</summary>
    Task<GitStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>Creates and checks out a branch (e.g. <c>wheelhouse/session-12</c>).</summary>
    Task<GitResult> CreateBranchAsync(string repositoryPath, string branch, CancellationToken cancellationToken = default);

    /// <summary>Reverts uncommitted modifications to tracked files (<c>git restore .</c>).</summary>
    Task<GitResult> DiscardChangesAsync(string repositoryPath, CancellationToken cancellationToken = default);
}

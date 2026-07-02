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

    /// <summary>Returns the most recent <paramref name="count"/> commits as one-line summaries.</summary>
    Task<IEnumerable<string>> GetRecentCommits(string repositoryPath, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a linked worktree at <paramref name="worktreePath"/> on a new
    /// <paramref name="branch"/> based at HEAD, so a parallel agent can edit files without
    /// colliding with the main working tree.
    /// </summary>
    Task<GitResult> AddWorktreeAsync(string repositoryPath, string worktreePath, string branch, CancellationToken cancellationToken = default);

    /// <summary>Removes a linked worktree (forced; commit anything you want to keep first).</summary>
    Task<GitResult> RemoveWorktreeAsync(string repositoryPath, string worktreePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges <paramref name="branch"/> into the current branch. On conflict the merge is
    /// aborted (working tree left untouched) and the result reports failure.
    /// </summary>
    Task<GitResult> MergeBranchAsync(string repositoryPath, string branch, CancellationToken cancellationToken = default);

    /// <summary>Deletes a local branch; <paramref name="force"/> drops unmerged work.</summary>
    Task<GitResult> DeleteBranchAsync(string repositoryPath, string branch, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Stages everything and commits (used to snapshot agent edits in a worktree).</summary>
    Task<GitResult> CommitAllAsync(string repositoryPath, string message, CancellationToken cancellationToken = default);
}

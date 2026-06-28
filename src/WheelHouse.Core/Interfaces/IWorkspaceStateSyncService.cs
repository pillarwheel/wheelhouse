namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Service that automatically synchronizes active session state (plan, tasks, status)
/// to the workspace repository filesystem under the .wheelhouse folder (GitOps).
/// </summary>
public interface IWorkspaceStateSyncService
{
    /// <summary>
    /// Synchronizes the current plan, tasks list, and session status to files in the repository.
    /// </summary>
    /// <param name="sessionId">The session ID to sync.</param>
    /// <param name="repositoryPath">The root directory of the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SyncActiveSessionAsync(int sessionId, string repositoryPath, CancellationToken cancellationToken = default);
}

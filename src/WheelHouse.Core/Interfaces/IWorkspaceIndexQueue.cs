namespace WheelHouse.Core.Interfaces;

/// <summary>Queue of workspace ids awaiting background RAG indexing.</summary>
public interface IWorkspaceIndexQueue
{
    /// <summary>Schedules a workspace for (re)indexing.</summary>
    void Enqueue(int workspaceId);

    /// <summary>Awaits the next queued workspace id.</summary>
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}

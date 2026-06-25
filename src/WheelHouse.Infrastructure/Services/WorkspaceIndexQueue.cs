using System.Threading.Channels;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Services;

/// <summary>Unbounded in-memory queue of workspace ids for background indexing.</summary>
public class WorkspaceIndexQueue : IWorkspaceIndexQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public void Enqueue(int workspaceId) => _channel.Writer.TryWrite(workspaceId);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}

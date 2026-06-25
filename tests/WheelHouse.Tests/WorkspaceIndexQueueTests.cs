using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class WorkspaceIndexQueueTests
{
    [Fact]
    public async Task Dequeues_In_FIFO_Order()
    {
        var queue = new WorkspaceIndexQueue();
        queue.Enqueue(3);
        queue.Enqueue(7);

        Assert.Equal(3, await queue.DequeueAsync(CancellationToken.None));
        Assert.Equal(7, await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DequeueAsync_Waits_For_An_Item()
    {
        var queue = new WorkspaceIndexQueue();
        var pending = queue.DequeueAsync(CancellationToken.None);
        Assert.False(pending.IsCompleted);

        queue.Enqueue(42);
        Assert.Equal(42, await pending);
    }

    [Fact]
    public async Task DequeueAsync_Honors_Cancellation()
    {
        var queue = new WorkspaceIndexQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queue.DequeueAsync(cts.Token));
    }
}

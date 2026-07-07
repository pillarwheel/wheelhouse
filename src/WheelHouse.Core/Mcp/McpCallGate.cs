namespace WheelHouse.Core.Mcp;

/// <summary>
/// Rolling-window budget for MCP tool calls, keyed by repository. The MCP protocol carries no
/// turn boundary, so <c>MaxToolCallsPerTurn</c> is enforced as "at most N calls per window"
/// (default 60 s) — an honest approximation of a turn. Thread-safe; pure bookkeeping, so the
/// budget rules are unit-testable with an injected clock.
/// </summary>
public class McpCallGate
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _calls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records a call against <paramref name="key"/> and returns whether it fits the budget.
    /// <paramref name="maxCalls"/> &lt;= 0 means unlimited.
    /// </summary>
    public bool TryAcquire(string key, int maxCalls, TimeSpan window, DateTimeOffset now)
    {
        if (maxCalls <= 0) return true;

        lock (_lock)
        {
            if (!_calls.TryGetValue(key, out var timestamps))
                _calls[key] = timestamps = new Queue<DateTimeOffset>();

            while (timestamps.Count > 0 && now - timestamps.Peek() >= window)
                timestamps.Dequeue();

            if (timestamps.Count >= maxCalls) return false;
            timestamps.Enqueue(now);
            return true;
        }
    }
}

using System.Security.Cryptography;
using System.Text;

namespace WheelHouse.Infrastructure.Agents;

/// <summary>
/// In-memory registry of Gemini explicit context caches: SHA-256 of the context text → the
/// server-side <c>cachedContents</c> resource name and its expiry. Thread-safe bookkeeping
/// only (no HTTP), so the reuse/expiry rules are unit-testable offline.
/// </summary>
public class GeminiContextCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Name, DateTimeOffset ExpiresAt)> _entries = new();

    public static string KeyFor(string contextText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contextText)));

    public bool TryGet(string key, DateTimeOffset now, out string cacheName)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
            {
                cacheName = entry.Name;
                return true;
            }
            _entries.Remove(key); // expired or absent
            cacheName = string.Empty;
            return false;
        }
    }

    public void Store(string key, string cacheName, DateTimeOffset expiresAt)
    {
        lock (_gate) _entries[key] = (cacheName, expiresAt);
    }

    public void Invalidate(string key)
    {
        lock (_gate) _entries.Remove(key);
    }
}

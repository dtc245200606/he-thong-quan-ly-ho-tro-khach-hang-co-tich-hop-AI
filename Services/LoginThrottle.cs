using System.Collections.Concurrent;

namespace CustomerSupportAI.Services;

/// <summary>
/// In-memory brute-force protection: after <see cref="MaxFailures"/> failed sign-ins for the same
/// IP + email within <see cref="Window"/>, further attempts are refused until the window expires.
/// Only failures are counted, so normal sign-ins are never throttled. State resets on app restart.
/// </summary>
public sealed class LoginThrottle
{
    private const int MaxFailures = 5;
    private const int MaxTrackedKeys = 10_000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Start)> entries = new();

    public static string Key(HttpContext context, string normalizedEmail) =>
        $"{context.Connection.RemoteIpAddress}|{normalizedEmail}";

    public bool IsBlocked(string key)
    {
        if (!entries.TryGetValue(key, out var entry)) return false;
        if (DateTimeOffset.UtcNow - entry.Start > Window)
        {
            entries.TryRemove(key, out _);
            return false;
        }
        return entry.Count >= MaxFailures;
    }

    public void RecordFailure(string key)
    {
        var now = DateTimeOffset.UtcNow;
        if (entries.Count > MaxTrackedKeys)
        {
            foreach (var pair in entries)
            {
                if (now - pair.Value.Start > Window) entries.TryRemove(pair.Key, out _);
            }
        }
        entries.AddOrUpdate(key, _ => (1, now),
            (_, entry) => now - entry.Start > Window ? (1, now) : (entry.Count + 1, entry.Start));
    }

    public void Reset(string key) => entries.TryRemove(key, out _);
}

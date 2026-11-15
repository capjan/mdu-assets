using System.Collections.Concurrent;

namespace MduAssets.Security;

/// <summary>
/// Counts events per key in a sliding window and says when a key has too many; used for failed sign-ins (AUTH-08) and for
/// 404 answers on public links (OPS-05). Kept in memory: a restart forgives, which is fine for slowing down guessing.
/// </summary>
public sealed class Throttle(int limit, TimeSpan window, TimeProvider time)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _events = new(StringComparer.Ordinal);

    public TimeSpan Window => window;

    public void Record(string key)
    {
        var now = time.GetUtcNow();
        var events = _events.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (events)
        {
            events.Enqueue(now);
            Trim(events, now);
        }

        // Keys of a quiet past are dropped now and then, so the dictionary does not grow without end.
        if (_events.Count > 10_000)
            foreach (var (k, q) in _events)
                lock (q)
                {
                    Trim(q, now);
                    if (q.Count == 0)
                        _events.TryRemove(k, out _);
                }
    }

    public bool IsBlocked(string key)
    {
        if (!_events.TryGetValue(key, out var events))
            return false;
        lock (events)
        {
            Trim(events, time.GetUtcNow());
            return events.Count >= limit;
        }
    }

    public void Clear(string key) => _events.TryRemove(key, out _);

    private void Trim(Queue<DateTimeOffset> events, DateTimeOffset now)
    {
        while (events.Count > 0 && events.Peek() <= now - window)
            events.Dequeue();
    }
}

/// <summary>Failed sign-ins per user name and per client address (AUTH-08).</summary>
public sealed class SignInThrottle(ServerSettings settings, TimeProvider time)
{
    private readonly Throttle _throttle = new(settings.Options.SignInFailures, TimeSpan.FromMinutes(15), time);

    public TimeSpan Window => _throttle.Window;

    public bool IsBlocked(string? ip, string? user) =>
        (ip is not null && _throttle.IsBlocked("ip:" + ip)) || (user is not null && _throttle.IsBlocked("user:" + user.ToLowerInvariant()));

    public void Failed(string? ip, string? user)
    {
        if (ip is not null)
            _throttle.Record("ip:" + ip);
        if (user is not null)
            _throttle.Record("user:" + user.ToLowerInvariant());
    }
}

/// <summary>404 answers on public links per client address (OPS-05), so ids cannot be guessed quickly.</summary>
public sealed class NotFoundThrottle(ServerSettings settings, TimeProvider time)
{
    private readonly Throttle _throttle = new(settings.Options.PublicNotFoundPerMinute, TimeSpan.FromMinutes(1), time);

    public bool IsBlocked(string? ip) => ip is not null && _throttle.IsBlocked(ip);

    public void Record(string? ip)
    {
        if (ip is not null)
            _throttle.Record(ip);
    }
}

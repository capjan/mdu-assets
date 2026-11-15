using System.Collections.Concurrent;
using MduAssets.Storage;

namespace MduAssets.Statistics;

/// <summary>
/// Counts requests to public links in memory and writes them to SQLite in batches (STA-01 – STA-05), so a request never waits
/// for a database write. Only the asset, the time and the referring page are kept; nothing about the reader (STA-06).
/// </summary>
public sealed class UsageCounter(StatsStore store, ServerSettings settings, TimeProvider time, ILogger<UsageCounter> logger) : BackgroundService
{
    private readonly object _gate = new();
    private Dictionary<string, (long Count, DateTimeOffset Last)> _hits = new(StringComparer.Ordinal);
    private Dictionary<(string Id, string Origin, string Path), (long Count, DateTimeOffset Last)> _referrers = [];
    private readonly string[] _bots = settings.Options.BotUserAgents
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private readonly string _uiOrigin = settings.UiBaseUrl.GetLeftPart(UriPartial.Authority);
    private readonly string _publicOrigin = settings.PublicBaseUrl.GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// Counts a finished public request when it qualifies (STA-02, STA-03): GET with 200 or 304, no known bot, and not shown
    /// inside the web interface, whose previews are not uses of the image.
    /// </summary>
    public void Record(HttpContext context, string assetId)
    {
        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) || context.Response.StatusCode is not (200 or 304))
            return;
        var agent = request.Headers.UserAgent.ToString();
        if (_bots.Any(bot => agent.Contains(bot, StringComparison.OrdinalIgnoreCase)))
            return;

        string? origin = null, path = null;
        if (Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer) && referer.Scheme is "http" or "https")
        {
            origin = referer.GetLeftPart(UriPartial.Authority);
            if (string.Equals(origin, _uiOrigin, StringComparison.OrdinalIgnoreCase))
                return;
            path = referer.AbsolutePath;
            if (string.Equals(origin, _publicOrigin, StringComparison.OrdinalIgnoreCase))
                origin = null;
        }

        var now = time.GetUtcNow();
        lock (_gate)
        {
            _hits[assetId] = _hits.TryGetValue(assetId, out var hit) ? (hit.Count + 1, now) : (1, now);
            if (origin is not null)
            {
                var key = (assetId, origin, path!.Length > 500 ? path[..500] : path);
                _referrers[key] = _referrers.TryGetValue(key, out var seen) ? (seen.Count + 1, now) : (1, now);
            }
        }
    }

    /// <summary>Writes the counted requests; a failed write keeps them for the next try.</summary>
    public void Flush()
    {
        Dictionary<string, (long Count, DateTimeOffset Last)> hits;
        Dictionary<(string Id, string Origin, string Path), (long Count, DateTimeOffset Last)> referrers;
        lock (_gate)
        {
            if (_hits.Count == 0 && _referrers.Count == 0)
                return;
            (hits, _hits) = (_hits, new(StringComparer.Ordinal));
            (referrers, _referrers) = (_referrers, []);
        }

        try
        {
            store.Write(
                hits.Select(h => new HitBatch(h.Key, h.Value.Count, h.Value.Last)).ToList(),
                referrers.Select(r => new ReferrerBatch(r.Key.Id, r.Key.Origin, r.Key.Path, r.Value.Count, r.Value.Last)).ToList());
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            logger.LogWarning(exception, "Writing request statistics failed; trying again with the next batch");
            lock (_gate)
            {
                foreach (var (id, hit) in hits)
                    _hits[id] = _hits.TryGetValue(id, out var newer) ? (newer.Count + hit.Count, newer.Last) : hit;
                foreach (var (key, seen) in referrers)
                    _referrers[key] = _referrers.TryGetValue(key, out var newer) ? (newer.Count + seen.Count, newer.Last) : seen;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.Options.StatsFlushSeconds), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                Flush();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Writes what is left when the server stops.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        Flush();
    }
}

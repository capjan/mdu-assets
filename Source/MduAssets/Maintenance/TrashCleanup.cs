using MduAssets.Storage;

namespace MduAssets.Maintenance;

/// <summary>
/// Deletes trashed assets for good once they have been in the trash longer than the retention period (DEL-02,
/// <c>MDU_ASSETS_TRASHRETENTIONDAYS</c>, 30 days by default) and audit entries older than <c>MDU_ASSETS_AUDITRETENTIONDAYS</c>.
/// Runs at start and then every hour.
/// </summary>
public sealed class TrashCleanup(AssetStore assets, BlobCleanup cleanup, AuditStore audit, ServerSettings settings, TimeProvider time, ILogger<TrashCleanup> logger)
    : BackgroundService
{
    public int RunOnce()
    {
        var cutoff = time.GetUtcNow().AddDays(-settings.Options.TrashRetentionDays);
        var expired = assets.TrashedBefore(cutoff);
        foreach (var id in expired)
            cleanup.Collect(assets.Purge(id));
        // Audit entries are kept for the configured time (OPS-03, 365 days by default).
        audit.DeleteBefore(time.GetUtcNow().AddDays(-settings.Options.AuditRetentionDays));
        if (expired.Count > 0)
            logger.LogInformation("Deleted {Count} assets from the trash for good after {Days} days", expired.Count, settings.Options.TrashRetentionDays);
        return expired.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                RunOnce();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Cleaning the trash failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

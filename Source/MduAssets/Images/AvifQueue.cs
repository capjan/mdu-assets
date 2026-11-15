using System.Collections.Concurrent;
using System.Threading.Channels;
using MduAssets.Imaging;
using MduAssets.Storage;

namespace MduAssets.Images;

/// <summary>
/// Encodes AVIF copies in the background (DLV-04, ADR-0011): about a second per image is too slow for a request. Until a copy
/// exists, delivery uses WebP or the original. Each copy is queued once; a full queue drops new work, which comes back with
/// the next request.
/// </summary>
public sealed class AvifQueue(VariantStore variants, ImageJobs jobs, ServerSettings settings, ILogger<AvifQueue> logger) : BackgroundService
{
    private readonly Channel<Job> _channel = Channel.CreateBounded<Job>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    private sealed record Job(string BlobSha256, string Name, string SourcePath);

    /// <summary>Queues the AVIF copy <paramref name="name"/> of a source file unless it is queued already.</summary>
    public void Enqueue(string blobSha256, string name, string sourcePath)
    {
        if (settings.Options.AvifWorkers == 0 || !_pending.TryAdd(blobSha256 + "/" + name, 0))
            return;
        if (!_channel.Writer.TryWrite(new Job(blobSha256, name, sourcePath)))
            _pending.TryRemove(blobSha256 + "/" + name, out _);
    }

    /// <summary>Number of copies waiting or in progress.</summary>
    public int Pending => _pending.Count;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, settings.Options.AvifWorkers).Select(_ => Work(stoppingToken)));

    private async Task Work(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var avif = await jobs.RunAsync(() => ImageProcessor.Convert(job.SourcePath, "image/avif"), stoppingToken);
                // A copy that is not smaller is useless; remember that, so it is not encoded again.
                if (avif.Content.Length < new FileInfo(job.SourcePath).Length)
                    variants.Put(job.BlobSha256, job.Name, avif.Content);
                else
                    variants.MarkLarger(job.BlobSha256, job.Name);
            }
            catch (Exception exception) when (exception is ImageDecodeException or IOException)
            {
                logger.LogWarning(exception, "No AVIF copy for {Blob}/{Name}", job.BlobSha256, job.Name);
                variants.MarkLarger(job.BlobSha256, job.Name);
            }
            finally
            {
                _pending.TryRemove(job.BlobSha256 + "/" + job.Name, out _);
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using MduAssets.Imaging;
using MduAssets.Storage;

namespace MduAssets.Images;

/// <summary>A file to serve: its path, type and a tag that changes with the content.</summary>
public sealed record ServedFile(string Path, string ContentType, string ETag);

/// <summary>
/// Previews (DLV-06) and smaller widths (DLV-03) of a blob, created on first use and kept in the variant cache. Requests for the
/// same missing variant wait for one job instead of starting several. The format was decided on upload (UP-11), so every
/// request for a link gets the same type, whatever the browser accepts.
/// </summary>
public sealed class Variants(VariantStore store, BlobStore blobs, ImageJobs jobs, ServerSettings settings, ILogger<Variants> logger)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _running = new(StringComparer.Ordinal);

    /// <summary>The preview of a blob as WebP, created when missing.</summary>
    public async Task<string?> ThumbnailAsync(string blobSha256, CancellationToken cancellationToken = default)
    {
        var name = $"thumb-{settings.Options.ThumbnailSize}.webp";
        return store.Find(blobSha256, name) ?? await Once(blobSha256, name, async () =>
        {
            var content = await jobs.RunAsync(() => ImageProcessor.Thumbnail(blobs.PathOf(blobSha256), settings.Options.ThumbnailSize));
            return store.Put(blobSha256, name, content);
        });
    }

    /// <summary>Picks what a public request gets: the stored file, or a smaller width in the same format (BMP becomes PNG).</summary>
    public async Task<ServedFile> ResolveAsync(DeliveryFile file, int? requestedWidth, CancellationToken cancellationToken = default)
    {
        var sha = file.BlobSha256;
        var path = blobs.PathOf(sha);
        var type = file.ContentType;
        var key = "full";

        // SVG is vector; any width serves the original. Widths at or above the original's also serve the original.
        if (type != "image/svg+xml" && requestedWidth is { } requested && settings.SnapWidth(requested) is { } width &&
            (file.Width is not { } original || width < original))
        {
            var resizedType = type == "image/bmp" ? "image/png" : type;
            var name = $"w{width.ToString(CultureInfo.InvariantCulture)}.{Extension(resizedType)}";
            var resized = store.Find(sha, name) ?? await Once(sha, name, async () =>
            {
                var image = await jobs.RunAsync(() => ImageProcessor.Resize(path, type, width));
                return store.Put(sha, name, image.Content);
            });
            if (resized is not null)
                (path, type, key) = (resized, resizedType, $"w{width.ToString(CultureInfo.InvariantCulture)}");
        }

        return new ServedFile(path, type, ETag(sha, key, type));
    }

    /// <remarks>Jobs ignore the request's cancellation: other requests may be waiting for the same variant.</remarks>
    /// <summary>Runs <paramref name="create"/> once per variant at a time; a failure is logged and yields <c>null</c>.</summary>
    private async Task<string?> Once(string blobSha256, string name, Func<Task<string?>> create)
    {
        var key = blobSha256 + "/" + name;
        var job = _running.GetOrAdd(key, _key => new Lazy<Task<string?>>(async () =>
        {
            try
            {
                return await create();
            }
            catch (ImageDecodeException exception)
            {
                logger.LogWarning(exception, "No variant {Name} for {Blob}", name, blobSha256);
                return null;
            }
            finally
            {
                _running.TryRemove(key, out _);
            }
        }));
        return await job.Value;
    }

    private static string ETag(string sha, string key, string type) => $"\"{sha[..32]}-{key}-{Extension(type)}\"";

    private static string Extension(string contentType) =>
        ImageFormat.All.FirstOrDefault(f => f.ContentType == contentType)?.Extension ?? "bin";
}

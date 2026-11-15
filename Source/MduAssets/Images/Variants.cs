using System.Collections.Concurrent;
using System.Globalization;
using MduAssets.Imaging;
using MduAssets.Storage;

namespace MduAssets.Images;

/// <summary>A file to serve: its path, type, a tag that changes with the content, and whether the type depended on <c>Accept</c>.</summary>
public sealed record ServedFile(string Path, string ContentType, string ETag, bool VaryByAccept);

/// <summary>
/// Previews (DLV-06), smaller widths (DLV-03) and WebP or AVIF copies (DLV-04) of a blob, created on first use and kept in the
/// variant cache. Requests for the same missing variant wait for one job instead of starting several.
/// </summary>
public sealed class Variants(VariantStore store, BlobStore blobs, ImageJobs jobs, AvifQueue avif, ServerSettings settings, ILogger<Variants> logger)
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

    /// <summary>
    /// Picks what a public request gets: the original or a smaller width, as AVIF or WebP when the browser accepts it, the
    /// project allows it and the copy is smaller. AVIF copies come from the background queue; until one exists, WebP is
    /// created right away (about a fifth of a second) or the original is served.
    /// </summary>
    public async Task<ServedFile> ResolveAsync(DeliveryFile file, int? requestedWidth, string accept, CancellationToken cancellationToken = default)
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

        // Animated images, SVG and GIF keep their format; AVIF originals are already the smallest format offered.
        var convertible = file.ConvertFormats && !file.Animated && type is "image/png" or "image/jpeg" or "image/bmp" or "image/webp";
        if (!convertible)
            return new ServedFile(path, type, ETag(sha, key, type), VaryByAccept: false);

        if (accept.Contains("image/avif", StringComparison.OrdinalIgnoreCase))
        {
            var name = $"{key}.avif";
            if (store.Find(sha, name) is { } ready)
                return new ServedFile(ready, "image/avif", ETag(sha, key, "image/avif"), VaryByAccept: true);
            if (!store.IsLarger(sha, name))
                avif.Enqueue(sha, name, path);
        }

        if (type != "image/webp" && accept.Contains("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            var name = $"{key}.webp";
            var webp = store.IsLarger(sha, name) ? null : store.Find(sha, name) ?? await Once(sha, name, async () =>
            {
                var source = path;
                var image = await jobs.RunAsync(() => ImageProcessor.Convert(source, "image/webp"));
                if (image.Content.Length < new FileInfo(source).Length)
                    return store.Put(sha, name, image.Content);
                store.MarkLarger(sha, name);
                return null;
            });
            if (webp is not null)
                return new ServedFile(webp, "image/webp", ETag(sha, key, "image/webp"), VaryByAccept: true);
        }

        return new ServedFile(path, type, ETag(sha, key, type), VaryByAccept: true);
    }

    /// <summary>Queues the AVIF copy of the full image after an upload, so it is ready before the first reader comes.</summary>
    public void PrepareAvif(string blobSha256, string contentType, bool animated)
    {
        if (!animated && contentType is "image/png" or "image/jpeg" or "image/bmp" or "image/webp")
            avif.Enqueue(blobSha256, "full.avif", blobs.PathOf(blobSha256));
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

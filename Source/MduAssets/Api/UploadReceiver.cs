using MduAssets.Images;
using MduAssets.Imaging;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>
/// An image received, checked, cleaned and stored as a blob, with the name and alt text that came with it. When it was converted
/// to WebP (UP-11), <c>Original</c> records the type and size of the upload; only the converted file is stored.
/// </summary>
public sealed record ReceivedImage(StoredBlob Blob, ImageInfo Info, bool Animated, string? Name, string? Alt, VersionOriginal? Original = null);

/// <summary>
/// Reads one image from a request, as <c>multipart/form-data</c> (field <c>file</c>, optional <c>name</c> and <c>alt</c>) or
/// as the raw body with <c>?name=</c> and <c>?alt=</c> (API-04). Checks the type from the content (UP-06), the size and pixel
/// limits, sanitizes SVG (UP-07), removes metadata (UP-08), picks the format to serve when <c>convert</c> is set (UP-11) and
/// stores the blob. <c>?lossless=true</c> asks for a conversion without any loss of quality. Shared by uploads and new versions.
/// </summary>
public static class UploadReceiver
{
    private const int MultipartOverhead = 64 * 1024;

    public static async Task<(ReceivedImage? Image, ProblemHttpResult? Problem)> ReceiveAsync(
        HttpContext context, ServerSettings settings, ImageJobs jobs, BlobStore blobs, bool convert, CancellationToken cancellationToken,
        long? projectLimit = null)
    {
        // A project may set a lower limit than the instance (OPS-01).
        var limit = projectLimit is { } lower ? Math.Min(lower, settings.Options.MaxUploadBytes) : settings.Options.MaxUploadBytes;
        if (context.Request.ContentLength > limit + MultipartOverhead)
            return (null, TooLarge(limit));
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            bodySize.MaxRequestBodySize = limit + MultipartOverhead;

        byte[] data;
        string? name, alt;
        try
        {
            (data, name, alt) = context.Request.HasFormContentType
                ? await ReadForm(context.Request, limit, cancellationToken)
                : await ReadBody(context.Request, limit, cancellationToken);
        }
        catch (UploadTooLargeException)
        {
            return (null, TooLarge(limit));
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, TooLarge(limit));
        }
        catch (InvalidDataException exception)
        {
            return (null, Problem(StatusCodes.Status400BadRequest, "Unreadable upload", exception.Message));
        }

        if (data.Length == 0)
            return (null, Problem(StatusCodes.Status400BadRequest, "Empty upload", "The upload contains no file."));

        ImageInfo info;
        byte[] content;
        if (ImageDetector.DetectRaster(data) is { } raster)
            (info, content) = (raster, data);
        else if (SvgSanitizer.Sanitize(data) is { } svg)
            (info, content) = (svg.Info, svg.Content);
        else
            return (null, Problem(StatusCodes.Status415UnsupportedMediaType, "Unsupported file type",
                "Only PNG, JPEG, GIF, WebP, AVIF, BMP and SVG images can be uploaded. The type is read from the content, not from the file name."));

        if (info.Pixels > settings.Options.MaxPixels)
            return (null, Problem(StatusCodes.Status422UnprocessableEntity, "Image too large",
                $"The image has {info.Width} × {info.Height} pixels; at most {settings.Options.MaxPixels:N0} pixels are allowed."));

        // Metadata such as EXIF and GPS never reaches the store; the orientation is applied first (UP-08).
        // Then the format to serve is decided once (UP-11): WebP when it is smaller, from the upload itself, so a photo is
        // compressed only once.
        PreparedImage prepared;
        EncodedImage? webp = null;
        try
        {
            var detected = (Content: content, Info: info);
            prepared = await jobs.RunAsync(() => ImageProcessor.Prepare(detected.Content, detected.Info), cancellationToken);
            if (convert)
            {
                var source = (Animated: prepared.Animated, Size: (long)prepared.Content.Length, Lossless: IsLossless(context.Request));
                webp = await jobs.RunAsync(
                    () => ImageProcessor.ToWebP(detected.Content, detected.Info, source.Animated, source.Size, source.Lossless), cancellationToken);
            }
        }
        catch (ImageDecodeException exception)
        {
            return (null, Problem(StatusCodes.Status422UnprocessableEntity, "Unreadable image", exception.Message));
        }

        if (webp is null)
        {
            var cleaned = await blobs.PutAsync(new MemoryStream(prepared.Content, writable: false), cancellationToken);
            return (new ReceivedImage(cleaned, prepared.Info, prepared.Animated, name, alt), null);
        }

        // Only the converted file is kept; the upload is remembered by its type and size.
        var served = await blobs.PutAsync(new MemoryStream(webp.Content, writable: false), cancellationToken);
        var original = new VersionOriginal(null, info.ContentType, data.Length);
        return (new ReceivedImage(served, prepared.Info with { Format = ImageFormat.WebP }, prepared.Animated, name, alt, original), null);
    }

    private static async Task<(byte[] Data, string? Name, string? Alt)> ReadForm(HttpRequest request, long limit, CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = limit + MultipartOverhead }, cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null)
            return ([], null, null);
        if (file.Length > limit)
            throw new UploadTooLargeException();

        await using var stream = file.OpenReadStream();
        var data = await ReadLimited(stream, limit, cancellationToken);
        var name = form.TryGetValue("name", out var formName) && !string.IsNullOrWhiteSpace(formName) ? formName.ToString() : file.FileName;
        return (data, name, form["alt"].ToString());
    }

    private static async Task<(byte[] Data, string? Name, string? Alt)> ReadBody(HttpRequest request, long limit, CancellationToken cancellationToken)
    {
        var data = await ReadLimited(request.Body, limit, cancellationToken);
        return (data, request.Query["name"].ToString(), request.Query["alt"].ToString());
    }

    private static async Task<byte[]> ReadLimited(Stream stream, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
                throw new UploadTooLargeException();
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsLossless(HttpRequest request) => request.Query["lossless"].ToString() is "true" or "1";

    private static ProblemHttpResult TooLarge(long limit) =>
        Problem(StatusCodes.Status413PayloadTooLarge, "File too large", $"Uploads may have at most {limit:N0} bytes.");

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(detail, statusCode: status, title: title);

    private sealed class UploadTooLargeException : Exception;
}

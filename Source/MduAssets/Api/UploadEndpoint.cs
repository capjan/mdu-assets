using MduAssets.Imaging;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>
/// <c>POST /api/v1/projects/{project}/assets</c>: accepts one image as <c>multipart/form-data</c> (field <c>file</c>,
/// optional <c>name</c> and <c>alt</c>) or as the raw request body with <c>?name=</c> and <c>?alt=</c> (API-04).
/// <c>?folder=</c> puts it into a folder.
/// </summary>
public static class UploadEndpoint
{
    private const int MultipartOverhead = 64 * 1024;

    public static async Task<Results<Created<AssetResponse>, ProblemHttpResult>> Handle(
        string project,
        HttpContext context,
        AssetStore assets,
        BlobStore blobs,
        ProjectAccess access,
        FolderStore folders,
        ServerSettings settings,
        CancellationToken cancellationToken)
    {
        if (!access.TryGet(context.User, project, ProjectRole.Editor, out var target, out var problem))
            return problem;

        // ?folder=<id> files the upload into a folder of the project (ORG-01).
        long? folderId = null;
        if (context.Request.Query["folder"].ToString() is { Length: > 0 } folderText)
        {
            if (!long.TryParse(folderText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ||
                folders.Find(target.Id, id) is null)
                return FolderEndpoints.UnknownFolder(id);
            folderId = id;
        }

        var limit = settings.Options.MaxUploadBytes;
        if (context.Request.ContentLength > limit + MultipartOverhead)
            return TooLarge(limit);
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
            return TooLarge(limit);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(limit);
        }
        catch (InvalidDataException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "Unreadable upload", exception.Message);
        }

        if (data.Length == 0)
            return Problem(StatusCodes.Status400BadRequest, "Empty upload", "The upload contains no file.");

        ImageInfo info;
        byte[] content;
        if (ImageDetector.DetectRaster(data) is { } raster)
        {
            (info, content) = (raster, data);
        }
        else if (SvgSanitizer.Sanitize(data) is { } svg)
        {
            (info, content) = (svg.Info, svg.Content);
        }
        else
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "Unsupported file type",
                "Only PNG, JPEG, GIF, WebP, AVIF, BMP and SVG images can be uploaded. The type is read from the content, not from the file name.");
        }

        if (info.Pixels > settings.Options.MaxPixels)
            return Problem(StatusCodes.Status422UnprocessableEntity, "Image too large",
                $"The image has {info.Width} × {info.Height} pixels; at most {settings.Options.MaxPixels:N0} pixels are allowed.");

        var blob = await blobs.PutAsync(new MemoryStream(content, writable: false), cancellationToken);
        var fileName = FileNames.Normalize(name, info.Extension);
        var altText = string.IsNullOrWhiteSpace(alt) ? FileNames.SuggestAltText(name) : alt.Trim();
        var userId = context.User.Id();

        var asset = assets.Create(new NewAsset(target.Id, folderId, fileName, altText, blob.Hash, info.ContentType, blob.Size, info.Width, info.Height, userId));
        var response = AssetResponse.From(asset, settings);
        return TypedResults.Created(response.Url, response);
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

    private static ProblemHttpResult TooLarge(long limit) =>
        Problem(StatusCodes.Status413PayloadTooLarge, "File too large", $"Uploads may have at most {limit:N0} bytes.");

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(detail, statusCode: status, title: title);

    private sealed class UploadTooLargeException : Exception;
}

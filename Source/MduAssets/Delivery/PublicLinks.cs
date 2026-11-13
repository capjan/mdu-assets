using MduAssets.Storage;
using Microsoft.Net.Http.Headers;

namespace MduAssets.Delivery;

/// <summary>
/// Serves <c>/i/{id}</c> and <c>/i/{id}/{name}</c> on the public host (LNK-01, LNK-02, DLV-01, DLV-02, DLV-05, DLV-08).
/// Only the id is used for the lookup; the name is decoration, so renamed assets keep working under old links.
/// </summary>
public static class PublicLinks
{
    // Uploaded SVG files are sanitized; this policy is the second line of defense when a browser opens one directly.
    private const string SvgPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox";

    public static IResult Serve(string id, HttpContext context, AssetStore assets, BlobStore blobs, ServerSettings settings)
    {
        if (!AssetId.IsValid(id) || assets.FindCurrent(id) is not { } asset)
            return Results.NotFound();

        var headers = context.Response.Headers;
        headers.CacheControl =
            $"public, max-age={settings.Options.PublicMaxAgeSeconds}, stale-while-revalidate={settings.Options.PublicStaleWhileRevalidateSeconds}";
        headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        if (asset.ContentType == "image/svg+xml")
            headers.ContentSecurityPolicy = SvgPolicy;

        return TypedResults.PhysicalFile(
            blobs.PathOf(asset.BlobSha256),
            asset.ContentType,
            lastModified: asset.CreatedAt,
            entityTag: new EntityTagHeaderValue($"\"{asset.BlobSha256}\""),
            enableRangeProcessing: true);
    }
}

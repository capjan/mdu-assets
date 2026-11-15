using System.Globalization;
using MduAssets.Images;
using MduAssets.Storage;
using Microsoft.Net.Http.Headers;

namespace MduAssets.Delivery;

/// <summary>
/// Serves <c>/i/{id}</c> and <c>/i/{id}/{name}</c> on the public host (LNK-01, LNK-02, DLV-01 – DLV-05, DLV-08).
/// Only the id is used for the lookup; the name is decoration, so renamed assets keep working under old links.
/// <c>?w=</c> asks for a smaller width, and the <c>Accept</c> header may get WebP or AVIF instead of the original.
/// </summary>
public static class PublicLinks
{
    // Uploaded SVG files are sanitized; this policy is the second line of defense when a browser opens one directly.
    private const string SvgPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox";

    public static async Task<IResult> Serve(string id, HttpContext context, AssetStore assets, Variants variants, ServerSettings settings)
    {
        if (!AssetId.IsValid(id) || assets.FindForDelivery(id) is not { } asset)
            return Results.NotFound();

        int? width = null;
        if (context.Request.Query["w"].ToString() is { Length: > 0 } text)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var requested) || requested < 1)
                return Results.BadRequest();
            width = requested;
        }

        var file = await variants.ResolveAsync(asset, width, context.Request.Headers.Accept.ToString(), context.RequestAborted);

        var headers = context.Response.Headers;
        headers.CacheControl =
            $"public, max-age={settings.Options.PublicMaxAgeSeconds}, stale-while-revalidate={settings.Options.PublicStaleWhileRevalidateSeconds}";
        headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        if (file.VaryByAccept)
            headers.Vary = "Accept";
        if (file.ContentType == "image/svg+xml")
            headers.ContentSecurityPolicy = SvgPolicy;

        return TypedResults.PhysicalFile(
            file.Path,
            file.ContentType,
            lastModified: asset.CreatedAt,
            entityTag: new EntityTagHeaderValue(file.ETag),
            enableRangeProcessing: true);
    }
}

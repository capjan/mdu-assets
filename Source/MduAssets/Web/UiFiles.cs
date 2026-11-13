using System.Collections.Frozen;
using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace MduAssets.Web;

/// <summary>
/// Serves the web interface from files embedded in the program (UI-06): <c>/</c> is <c>index.html</c>,
/// everything else lives under <c>/ui/</c>. Files are revalidated on every load, so an update shows at once.
/// </summary>
public sealed class UiFiles
{
    private const string Prefix = "Ui/";

    private static readonly FrozenDictionary<string, string> ContentTypes = new Dictionary<string, string>
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".svg"] = "image/svg+xml",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly FrozenDictionary<string, UiFile> _files;
    private readonly string _policy;

    public UiFiles(ServerSettings settings)
    {
        var assembly = typeof(UiFiles).Assembly;
        var files = new Dictionary<string, UiFile>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) ||
                !ContentTypes.TryGetValue(Path.GetExtension(name), out var contentType))
                continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var bytes = copy.ToArray();
            var etag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"");
            files[name[Prefix.Length..]] = new UiFile(bytes, contentType, etag);
        }

        _files = files.ToFrozenDictionary(StringComparer.Ordinal);

        // Previews load from the public host; everything else comes from the UI host itself.
        var publicOrigin = settings.PublicBaseUrl.GetLeftPart(UriPartial.Authority);
        _policy = string.Join("; ",
            "default-src 'none'",
            "script-src 'self'",
            "style-src 'self'",
            $"img-src 'self' data: blob: {publicOrigin}",
            "connect-src 'self'",
            "base-uri 'none'",
            "form-action 'none'",
            "frame-ancestors 'none'");
    }

    public IReadOnlyCollection<string> Paths => _files.Keys;

    public IResult Serve(string path, HttpContext context)
    {
        if (!_files.TryGetValue(path, out var file))
            return Results.NotFound();

        var headers = context.Response.Headers;
        headers.CacheControl = "no-cache";
        headers["Referrer-Policy"] = "same-origin";
        if (file.ContentType.StartsWith("text/html", StringComparison.Ordinal))
        {
            headers.ContentSecurityPolicy = _policy;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
        }

        return TypedResults.Bytes(file.Content, file.ContentType, entityTag: file.ETag);
    }

    private sealed record UiFile(byte[] Content, string ContentType, EntityTagHeaderValue ETag);
}

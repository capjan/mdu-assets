namespace MduAssets;

/// <summary>
/// Settings from <c>appsettings.json</c> and <c>MDU_ASSETS_*</c> environment variables,
/// for example <c>MDU_ASSETS_PUBLICBASEURL=https://img.example.com</c>.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>Folder for the database and all files. Relative paths start at the working directory.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>Base URL of the web interface and the API, for example <c>https://assets.example.com</c>.</summary>
    public string? UiBaseUrl { get; set; }

    /// <summary>Base URL of public links, for example <c>https://img.example.com</c>. Must differ from <see cref="UiBaseUrl"/>.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Accept passwords over plain HTTP. Only for local development and tests.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// Comma-separated IP addresses or networks (CIDR) of reverse proxies whose <c>X-Forwarded-*</c> headers are trusted.
    /// Loopback is always trusted.
    /// </summary>
    public string? TrustedProxies { get; set; }

    /// <summary>Largest accepted upload in bytes.</summary>
    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>Largest accepted image in pixels (width × height), checked before decoding.</summary>
    public long MaxPixels { get; set; } = 50_000_000;

    /// <summary>How long browsers and caches may reuse a public link without asking again, in seconds.</summary>
    public int PublicMaxAgeSeconds { get; set; } = 300;

    /// <summary>How long caches may serve a stale copy while they revalidate it, in seconds.</summary>
    public int PublicStaleWhileRevalidateSeconds { get; set; } = 86_400;
}

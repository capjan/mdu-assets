using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace MduAssets;

/// <summary>An error in the configuration that stops the start, with a message for the operator.</summary>
public sealed class StartupException(string message) : Exception(message);

/// <summary>The validated settings the server runs with.</summary>
public sealed class ServerSettings
{
    private ServerSettings(ServerOptions options, Uri uiBaseUrl, Uri publicBaseUrl, IReadOnlyList<IPNetwork> trustedProxies)
    {
        Options = options;
        UiBaseUrl = uiBaseUrl;
        PublicBaseUrl = publicBaseUrl;
        TrustedProxies = trustedProxies;
    }

    public ServerOptions Options { get; }

    public Uri UiBaseUrl { get; }

    public Uri PublicBaseUrl { get; }

    public IReadOnlyList<IPNetwork> TrustedProxies { get; }

    /// <summary>The host pattern that routes requests to the UI and API endpoints.</summary>
    public string UiHost => HostPattern(UiBaseUrl);

    /// <summary>The host pattern that routes requests to public links.</summary>
    public string PublicHost => HostPattern(PublicBaseUrl);

    /// <summary>The public link of an asset: <c>{PublicBaseUrl}/i/{id}/{fileName}</c>.</summary>
    public string PublicLink(string id, string fileName) =>
        $"{PublicBaseUrl.GetLeftPart(UriPartial.Authority)}{PublicBaseUrl.AbsolutePath.TrimEnd('/')}/i/{id}/{Uri.EscapeDataString(fileName)}";

    /// <summary>
    /// Checks the options. In development, missing base URLs default to <c>http://127.0.0.1:5180</c> (UI) and
    /// <c>http://127.0.0.1:5181</c> (public links).
    /// </summary>
    public static ServerSettings Create(ServerOptions options, bool isDevelopment)
    {
        var ui = BaseUrl(options.UiBaseUrl, isDevelopment ? "http://127.0.0.1:5180" : null, "UIBASEURL");
        var publicLinks = BaseUrl(options.PublicBaseUrl, isDevelopment ? "http://127.0.0.1:5181" : null, "PUBLICBASEURL");

        if (string.Equals(HostPattern(ui), HostPattern(publicLinks), StringComparison.OrdinalIgnoreCase))
            throw new StartupException(
                $"MDU_ASSETS_UIBASEURL and MDU_ASSETS_PUBLICBASEURL use the same host ({ui.Authority}). " +
                "Public links need a host of their own, so that uploaded files cannot reach the web interface (ADR-0009).");

        if (options.MaxUploadBytes <= 0)
            throw new StartupException("MDU_ASSETS_MAXUPLOADBYTES must be greater than 0.");
        if (options.MaxPixels <= 0)
            throw new StartupException("MDU_ASSETS_MAXPIXELS must be greater than 0.");

        return new ServerSettings(options, ui, publicLinks, ParseProxies(options.TrustedProxies));
    }

    private static Uri BaseUrl(string? value, string? fallback, string name)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (text is null)
            throw new StartupException($"MDU_ASSETS_{name} is missing. Set it to the base URL, for example https://assets.example.com.");
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new StartupException($"MDU_ASSETS_{name} must be an absolute http or https URL without query, but is '{text}'.");
        return uri;
    }

    private static List<IPNetwork> ParseProxies(string? value)
    {
        var networks = new List<IPNetwork>();
        foreach (var entry in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPNetwork.TryParse(entry, out var network))
                networks.Add(network);
            else if (IPAddress.TryParse(entry, out var address))
                networks.Add(new IPNetwork(address, address.GetAddressBytes().Length * 8));
            else
                throw new StartupException($"MDU_ASSETS_TRUSTEDPROXIES contains '{entry}', which is neither an IP address nor a network such as 172.16.0.0/12.");
        }

        return networks;
    }

    // Without an explicit port, a host pattern matches any port: behind a reverse proxy, the Host header has none.
    private static string HostPattern(Uri uri) => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    public void ApplyTo(ForwardedHeadersOptions forwarded)
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var network in TrustedProxies)
            forwarded.KnownIPNetworks.Add(network);
    }
}

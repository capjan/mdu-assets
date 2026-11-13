using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace MduAssets.Test;

public sealed class WebInterfaceTest : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task AsksForThePasswordOnTheFirstVisit()
    {
        var response = await _server.Ui(user: null).GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("Basic realm=", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task ServesThePageWithAStrictContentSecurityPolicy()
    {
        var response = await _server.Ui().GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<title>MDU Assets</title>", html);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'none'", policy);
        Assert.Contains("script-src 'self'", policy);
        Assert.Contains("img-src 'self' data: blob: http://img.test", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/ui/app.js", "text/javascript")]
    [InlineData("/ui/theme.js", "text/javascript")]
    [InlineData("/ui/app.css", "text/css")]
    [InlineData("/ui/tokens.css", "text/css")]
    [InlineData("/ui/icon.svg", "image/svg+xml")]
    public async Task ServesTheEmbeddedFiles(string path, string contentType)
    {
        var client = _server.Ui();
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Headers.ETag);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.IfNoneMatch.Add(response.Headers.ETag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("/ui/tokens.pin.json")]
    [InlineData("/ui/missing.js")]
    public async Task ServesNothingElse(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Ui().GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>
    /// HttpClient would resolve <c>..</c> before sending, so the raw path goes straight to the server.
    /// <c>/ui/app.js</c> shows that the request reaches the file endpoint at all.
    /// </summary>
    [Theory]
    [InlineData("/ui/app.js", HttpStatusCode.OK)]
    [InlineData("/ui/../appsettings.json", HttpStatusCode.NotFound)]
    [InlineData("/ui/../MduAssets.dll", HttpStatusCode.NotFound)]
    [InlineData("/ui/..%2Fappsettings.json", HttpStatusCode.NotFound)]
    [InlineData("/ui//etc/passwd", HttpStatusCode.NotFound)]
    public async Task ServesNoFilesOutsideTheInterface(string rawPath, HttpStatusCode expected)
    {
        var context = await _server.Send(request =>
        {
            request.Request.Method = "GET";
            request.Request.Scheme = "http";
            request.Request.Host = new Microsoft.AspNetCore.Http.HostString("ui.test");
            request.Request.Path = new Microsoft.AspNetCore.Http.PathString(rawPath);
            request.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{TestServer.AdminName}:{TestServer.AdminPassword}"));
        });

        Assert.Equal((int)expected, context.Response.StatusCode);
    }

    [Fact]
    public async Task ServesTheInterfaceOnlyOnTheUiHost()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Public().GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Public().GetAsync("/ui/app.js", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>The copy of the MDU design tokens must not be edited by hand (ADR-0010).</summary>
    [Fact]
    public void TokensMatchThePinnedVersion()
    {
        var assembly = typeof(Program).Assembly;
        using var tokens = assembly.GetManifestResourceStream("Ui/tokens.css")!;
        using var pinStream = assembly.GetManifestResourceStream("Ui/tokens.pin.json")!;
        using var pin = JsonDocument.Parse(pinStream);

        var actual = Convert.ToHexStringLower(SHA256.HashData(tokens));

        Assert.True(pin.RootElement.GetProperty("sha256").GetString() == actual,
            "Ui/tokens.css differs from tokens.pin.json. Do not edit the copy; run " +
            "'dotnet run --project Tools/SyncTokens -- <path to mdu-design-system>' in Source instead.");
    }
}

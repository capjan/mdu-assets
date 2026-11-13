using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Imaging.Test;

namespace MduAssets.Test;

public sealed class PublicLinkTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private async Task<AssetResponse> Upload(byte[] data, string name)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await _server.Ui().PostAsync($"/api/v1/projects/default/assets?name={name}", content, TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken))!;
    }

    private Task<HttpResponseMessage> Get(string url, Action<HttpRequestMessage>? configure = null, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        configure?.Invoke(request);
        return _server.Public().SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ServesTheFileWithCachingAndSecurityHeaders()
    {
        var data = TestImages.Png(8, 8);
        var asset = await Upload(data, "diagram.png");

        var response = await Get(asset.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(data, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=300, stale-while-revalidate=86400", response.Headers.CacheControl?.ToString());
        Assert.NotNull(response.Headers.ETag);
        Assert.NotNull(response.Content.Headers.LastModified);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("cross-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/old-name.png")]
    [InlineData("/any/thing/else")]
    public async Task IgnoresTheNameInTheLink(string suffix)
    {
        var asset = await Upload(TestImages.Png(2, 2), "diagram.png");

        var response = await Get($"http://img.test/i/{asset.Id}{suffix}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnswersConditionalRequestsWithNotModified()
    {
        var asset = await Upload(TestImages.Png(2, 2), "a.png");
        var etag = (await Get(asset.Url)).Headers.ETag!;

        var response = await Get(asset.Url, request => request.Headers.IfNoneMatch.Add(etag));

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task SupportsHeadAndRangeRequests()
    {
        var data = TestImages.Png(3, 3);
        var asset = await Upload(data, "a.png");

        var head = await Get(asset.Url, method: HttpMethod.Head);
        var range = await Get(asset.Url, request => request.Headers.Range = new RangeHeaderValue(0, 7));

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(data.Length, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal(data[..8], await range.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SandboxesSvg()
    {
        var asset = await Upload(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"/>"), "x.svg");

        var response = await Get(asset.Url);

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'none'", policy);
        Assert.Contains("sandbox", policy);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/i/AAAAAAAAAA/x.png")]
    [InlineData("/i/short")]
    [InlineData("/i/../../etc/passwd")]
    public async Task AnswersUnknownIdsWithNotFound(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Get("http://img.test" + path)).StatusCode);
    }

    [Fact]
    public async Task ServesLinksOnlyOnThePublicHostAndTheApiOnlyOnTheUiHost()
    {
        var asset = await Upload(TestImages.Png(2, 2), "a.png");
        var onUiHost = await _server.Ui().GetAsync($"/i/{asset.Id}/a.png", TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects/default/assets") { Content = new ByteArrayContent(TestImages.Png(1, 1)) };
        request.Headers.Authorization = _server.Ui().DefaultRequestHeaders.Authorization;
        var apiOnPublicHost = await _server.Public().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, onUiHost.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, apiOnPublicHost.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Imaging.Test;

namespace MduAssets.Test;

public sealed class UploadTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TestServer _server = new(new() { ["MaxUploadBytes"] = "4096", ["MaxPixels"] = "1000000" });

    public void Dispose() => _server.Dispose();

    private static ByteArrayContent Raw(byte[] data, string type = "application/octet-stream")
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    private Task<HttpResponseMessage> Post(HttpContent content, string query = "", string project = "default") =>
        _server.Ui().PostAsync($"/api/v1/projects/{project}/assets{query}", content, TestContext.Current.CancellationToken);

    [Fact]
    public async Task UploadsARawBodyAndReturnsTheLinkAndSnippets()
    {
        var response = await Post(Raw(TestImages.Png(1000, 500), "image/png"), "?name=Screen%20Shot%20(2).PNG");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);
        Assert.NotNull(asset);
        Assert.Matches("^[0-9A-Za-z]{10}$", asset.Id);
        Assert.Equal($"http://img.test/i/{asset.Id}/Screen-Shot-2.png", asset.Url);
        Assert.Equal(asset.Url, response.Headers.Location?.ToString());
        Assert.Equal("Screen Shot (2)", asset.AltText);
        Assert.Equal($"![Screen Shot (2)]({asset.Url})", asset.Markdown);
        Assert.Equal($"<img src=\"{asset.Url}\" alt=\"Screen Shot (2)\" width=\"1000\" height=\"500\">", asset.Html);
        Assert.Equal((1000, 500), (asset.Width, asset.Height));
        Assert.Equal("image/png", asset.ContentType);
        Assert.Equal(1, asset.Version);
    }

    [Fact]
    public async Task UploadsMultipartWithNameAndAltText()
    {
        using var form = new MultipartFormDataContent
        {
            { Raw(TestImages.Gif(10, 20)), "file", "ignored.bin" },
            { new StringContent("Animated [demo]"), "alt" },
            { new StringContent("demo animation.gif"), "name" },
        };

        var asset = await (await Post(form)).Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);

        Assert.NotNull(asset);
        Assert.Equal("demo-animation.gif", asset.FileName);
        Assert.Equal(@"![Animated \[demo\]](" + asset.Url + ")", asset.Markdown);
        Assert.Equal("image/gif", asset.ContentType);
    }

    [Fact]
    public async Task TakesTheTypeFromTheContentNotTheName()
    {
        var asset = await (await Post(Raw(TestImages.Jpeg(30, 20), "image/png"), "?name=photo.png"))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);

        Assert.NotNull(asset);
        Assert.Equal("image/jpeg", asset.ContentType);
        Assert.Equal("photo.jpg", asset.FileName);
    }

    [Fact]
    public async Task StoresSanitizedSvg()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\"><script>alert(1)</script><rect width=\"20\" height=\"10\"/></svg>";

        var asset = await (await Post(Raw(Encoding.UTF8.GetBytes(svg)), "?name=logo.svg"))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);
        var served = await _server.Public().GetStringAsync(asset!.Url, TestContext.Current.CancellationToken);

        Assert.Equal("image/svg+xml", asset.ContentType);
        Assert.Equal((20, 10), (asset.Width, asset.Height));
        Assert.DoesNotContain("script", served);
        Assert.Contains("<rect", served);
    }

    [Fact]
    public async Task RejectsOtherFileTypes()
    {
        var response = await Post(Raw("<html><body>hi</body></html>"u8.ToArray(), "image/png"), "?name=x.png");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RejectsFilesOverTheSizeLimit()
    {
        var data = TestImages.Png(10, 10).Concat(new byte[5000]).ToArray();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Post(Raw(data, "image/png"))).StatusCode);
    }

    [Fact]
    public async Task RejectsImagesOverThePixelLimitBeforeDecoding()
    {
        var response = await Post(Raw(TestImages.Png(2000, 2000), "image/png"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RejectsEmptyUploadsAndUnknownProjects()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(Raw([]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(Raw(TestImages.Png(1, 1)), project: "nope")).StatusCode);
    }

    [Fact]
    public async Task StoresIdenticalContentOnceButCreatesTwoAssets()
    {
        var first = await (await Post(Raw(TestImages.Png(5, 5)))).Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);
        var second = await (await Post(Raw(TestImages.Png(5, 5)))).Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);

        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_server.DataDirectory, "blobs"), "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("Screen Shot (2).PNG", "png", "Screen-Shot-2.png")]
    [InlineData("../../etc/passwd", "png", "passwd.png")]
    [InlineData("C:\\Users\\me\\Diagramm Übersicht.svg", "svg", "Diagramm-bersicht.svg")]
    [InlineData("", "gif", "image.gif")]
    [InlineData("...", "webp", "image.webp")]
    public void NormalizesFileNames(string uploaded, string extension, string expected)
    {
        Assert.Equal(expected, FileNames.Normalize(uploaded, extension));
    }
}

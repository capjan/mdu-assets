using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Imaging.Test;

namespace MduAssets.Test;

/// <summary>Every accepted format gets a preview in the web interface, also where libvips cannot read the format.</summary>
public sealed class PreviewFormatsTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private async Task<AssetResponse> Upload(byte[] data, string name)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await _server.Ui().PostAsync($"/api/v1/projects/default/assets?name={name}", content, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    [Fact]
    public async Task PreviewsSvg()
    {
        var asset = await Upload(TestImages.Svg("""<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100"><rect width="200" height="100" fill="teal"/></svg>"""), "shape.svg");

        var response = await _server.Ui().GetAsync(asset.ThumbnailUrl, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PreviewsBmp()
    {
        var asset = await Upload(SampleImages.Bmp(40, 20), "old.bmp");

        var response = await _server.Ui().GetAsync(asset.ThumbnailUrl, Cancel);
        var link = await _server.Public().GetAsync(asset.Url + "?w=320", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("image/", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
    }
}

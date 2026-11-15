using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Images;
using MduAssets.Imaging.Test;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MduAssets.Test;

/// <summary>Metadata removal, previews, widths, WebP and AVIF, light and dark pairs and duplicates (M5).</summary>
public sealed class ImageProcessingTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string ViewerHash = PasswordHasher.Hash("viewer-password");
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new();
    private readonly HttpClient _admin;

    public ImageProcessingTest()
    {
        _admin = _server.Ui();
    }

    public void Dispose() => _server.Dispose();

    private async Task<HttpResponseMessage> Post(byte[] data, string type, string query, string project = "default")
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return await _admin.PostAsync($"/api/v1/projects/{project}/assets{query}", content, Cancel);
    }

    private async Task<AssetResponse> Upload(byte[] data, string name, string type = "image/png", string project = "default")
    {
        var response = await Post(data, type, $"?name={name}", project);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    private async Task<HttpResponseMessage> Fetch(string url, string? accept = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (accept is not null)
            request.Headers.TryAddWithoutValidation("Accept", accept);
        return await _server.Public().SendAsync(request, Cancel);
    }

    [Fact]
    public async Task RemovesMetadataAndAppliesTheOrientation()
    {
        var asset = await Upload(SampleImages.JpegWithExif(160, 90), "photo.jpg", "image/jpeg");

        var stored = await (await Fetch(asset.Url)).Content.ReadAsByteArrayAsync(Cancel);

        Assert.Equal((90, 160), (asset.Width, asset.Height));
        Assert.Equal((90, 160, 1), SampleImages.Measure(stored));
        Assert.DoesNotContain(SampleImages.Fields(stored), f => f.StartsWith("exif", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsImagesWithoutMetadataUnchanged()
    {
        var png = SampleImages.Png(200, 100);
        var asset = await Upload(png, "clean.png");

        Assert.Equal(png, await (await Fetch(asset.Url)).Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task ServesPreviewsOnlyToMembers()
    {
        var asset = await Upload(SampleImages.Png(1200, 600), "wide.png");
        var users = _server.Services.GetRequiredService<UserStore>();
        users.Create("sam", ViewerHash, isAdmin: false);

        var response = await _admin.GetAsync(asset.ThumbnailUrl, Cancel);
        var preview = await response.Content.ReadAsByteArrayAsync(Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal((480, 240, 1), SampleImages.Measure(preview));
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Ui("sam", "viewer-password").GetAsync(asset.ThumbnailUrl, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Public().GetAsync(asset.ThumbnailUrl, Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("?w=640", 640)]
    [InlineData("?w=700", 800)]
    [InlineData("?w=1000", 1024)]
    [InlineData("?w=1200", 1200)]
    [InlineData("?w=5000", 1200)]
    [InlineData("", 1200)]
    public async Task ServesConfiguredWidthsAndNeverUpscales(string query, int width)
    {
        var asset = await Upload(SampleImages.Png(1200, 600), "wide.png");

        var response = await Fetch(asset.Url + query);

        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(width, SampleImages.Measure(await response.Content.ReadAsByteArrayAsync(Cancel)).Width);
    }

    [Fact]
    public async Task KeepsAllFramesOfAnimatedImages()
    {
        var asset = await Upload(SampleImages.AnimatedGif(800, 400, 3), "animation.gif", "image/gif");

        var response = await Fetch(asset.Url + "?w=320", "image/avif,image/webp,*/*");

        Assert.True(asset.Animated);
        Assert.Equal("image/gif", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((320, 160, 3), SampleImages.Measure(await response.Content.ReadAsByteArrayAsync(Cancel)));
        Assert.Empty(response.Headers.Vary);
    }

    [Fact]
    public async Task RejectsWidthsThatAreNoNumber()
    {
        var asset = await Upload(SampleImages.Png(100, 50), "small.png");

        Assert.Equal(HttpStatusCode.BadRequest, (await Fetch(asset.Url + "?w=wide")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Fetch(asset.Url + "?w=0")).StatusCode);
    }

    [Fact]
    public async Task NegotiatesWebPAndAvif()
    {
        var asset = await Upload(SampleImages.Png(800, 400), "photo.png");

        var plain = await Fetch(asset.Url);
        var webp = await Fetch(asset.Url, "image/webp,*/*");

        Assert.Equal("image/png", plain.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept", plain.Headers.Vary);
        Assert.Equal("image/webp", webp.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual(plain.Headers.ETag, webp.Headers.ETag);

        // The AVIF copy is made in the background after the upload; until then the answer is WebP.
        var queue = _server.Services.GetRequiredService<AvifQueue>();
        for (var i = 0; i < 300 && queue.Pending > 0; i++)
            await Task.Delay(100, Cancel);
        var avif = await Fetch(asset.Url, "image/avif,image/webp,*/*");
        Assert.Equal("image/avif", avif.Content.Headers.ContentType?.MediaType);
        Assert.Equal((800, 400, 1), SampleImages.Measure(await avif.Content.ReadAsByteArrayAsync(Cancel)));
    }

    [Fact]
    public async Task OwnersCanTurnConversionOff()
    {
        var asset = await Upload(SampleImages.Png(800, 400), "photo.png");

        var project = await (await _admin.PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges(null, null, ConvertFormats: false), Json, Cancel))
            .Content.ReadFromJsonAsync<ProjectResponse>(Json, Cancel);
        var response = await Fetch(asset.Url, "image/avif,image/webp,*/*");

        Assert.False(project!.ConvertFormats);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.Vary);
    }

    [Fact]
    public async Task ReportsDuplicatesWhenAsked()
    {
        var png = SampleImages.Png(300, 200);
        var first = await Upload(png, "first.png");

        var rejected = await Post(png, "image/png", "?name=again.png&duplicates=reject");
        var duplicate = await rejected.Content.ReadFromJsonAsync<DuplicateResponse>(Json, Cancel);
        var kept = await Post(png, "image/png", "?name=again.png");

        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(first.Id, duplicate!.Existing.Id);
        Assert.Equal(HttpStatusCode.Created, kept.StatusCode);
        var list = await _admin.GetFromJsonAsync<AssetList>("/api/v1/projects/default/assets", Json, Cancel);
        Assert.Equal(2, list!.Items.Count);
    }

    [Fact]
    public async Task PairsLightAndDarkVariants()
    {
        var light = await Upload(SampleImages.Png(300, 200), "settings-light.png");
        var dark = await Upload(SampleImages.Png(301, 200), "settings-dark.png");
        var other = await Upload(SampleImages.Png(302, 200), "other.png");

        var paired = await (await _admin.PutAsJsonAsync($"/api/v1/assets/{light.Id}/pair", new PairChange(dark.Id, "light"), Json, Cancel))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        var partner = await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{dark.Id}", Json, Cancel);

        Assert.Equal((dark.Id, "light"), (paired!.Pair!.Id, paired.Pair.Role));
        Assert.Equal((light.Id, "dark"), (partner!.Pair!.Id, partner.Pair.Role));
        Assert.Equal($"![Settings light]({light.Url}#only-light)\n![Settings light]({dark.Url}#only-dark)", paired.Pair.Markdown);

        // Pairing one of them again dissolves the old pair on both sides.
        await _admin.PutAsJsonAsync($"/api/v1/assets/{other.Id}/pair", new PairChange(light.Id, "dark"), Json, Cancel);
        Assert.Null((await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{dark.Id}", Json, Cancel))!.Pair);

        await _admin.DeleteAsync($"/api/v1/assets/{light.Id}/pair", Cancel);
        Assert.Null((await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{other.Id}", Json, Cancel))!.Pair);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.PutAsJsonAsync($"/api/v1/assets/{light.Id}/pair", new PairChange(light.Id, "light"), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.PutAsJsonAsync($"/api/v1/assets/{light.Id}/pair", new PairChange(dark.Id, "dim"), Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task PairsOnlyWithinOneProject()
    {
        await _admin.PostAsJsonAsync("/api/v1/projects", new NewProject("other", "Other", null), Json, Cancel);
        var here = await Upload(SampleImages.Png(40, 20), "here.png");
        var there = await Upload(SampleImages.Png(41, 20), "there.png", project: "other");

        var response = await _admin.PutAsJsonAsync($"/api/v1/assets/{here.Id}/pair", new PairChange(there.Id, "light"), Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

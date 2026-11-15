using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Imaging.Test;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MduAssets.Test;

/// <summary>Metadata removal, the format chosen on upload, previews, widths, light and dark pairs and duplicates (M5, UP-11).</summary>
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

        Assert.Equal("image/webp", asset.ContentType);
        Assert.Equal((90, 160), (asset.Width, asset.Height));
        Assert.Equal((90, 160, 1), SampleImages.Measure(stored));
        Assert.DoesNotContain(SampleImages.Fields(stored), f => f.StartsWith("exif", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsImagesWithoutMetadataUnchanged()
    {
        await TurnConversion(on: false);
        var png = SampleImages.Png(200, 100);
        var asset = await Upload(png, "clean.png");

        Assert.Equal(png, await (await Fetch(asset.Url)).Content.ReadAsByteArrayAsync(Cancel));
    }

    private async Task TurnConversion(bool on)
    {
        var response = await _admin.PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges(null, null, ConvertFormats: on), Json, Cancel);
        Assert.Equal(on, (await response.Content.ReadFromJsonAsync<ProjectResponse>(Json, Cancel))!.ConvertFormats);
    }

    private static bool SamePixels(byte[] first, byte[] second)
    {
        using var a = NetVips.Image.NewFromBuffer(first);
        using var b = NetVips.Image.NewFromBuffer(second);
        using var difference = (a - b).Abs();
        return (a.Width, a.Height, a.Bands) == (b.Width, b.Height, b.Bands) && difference.Max() == 0;
    }

    /// <summary>A screenshot-like PNG: flat areas and hard edges, where lossless WebP is small.</summary>
    private static byte[] GraphicPng(int width, int height)
    {
        using var stripes = (NetVips.Image.Xyz(width, height)[0] / 40 % 2 * 200 + 30).Cast(NetVips.Enums.BandFormat.Uchar);
        using var rgb = stripes.Bandjoin(stripes, stripes).Copy(interpretation: NetVips.Enums.Interpretation.Srgb);
        return rgb.PngsaveBuffer(keep: NetVips.Enums.ForeignKeep.None);
    }

    /// <summary>A photo-like PNG, such as a screenshot of a picture: soft noise, where lossless WebP is many times larger.</summary>
    private static byte[] PhotoPng(int width, int height)
    {
        using var noise = NetVips.Image.Gaussnoise(width, height, mean: 128, sigma: 50);
        using var soft = noise.Bandjoin(noise.Rot180(), noise.FlipHor()).Gaussblur(1.5).Cast(NetVips.Enums.BandFormat.Uchar)
            .Copy(interpretation: NetVips.Enums.Interpretation.Srgb);
        return soft.PngsaveBuffer(keep: NetVips.Enums.ForeignKeep.None);
    }

    private async Task<AssetResponse> UploadLossless(byte[] data, string name, string type)
    {
        var response = await Post(data, type, $"?name={name}&lossless=true");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    [Fact]
    public async Task StoresGraphicsAsLosslessWebP()
    {
        var png = GraphicPng(800, 400);
        var asset = await Upload(png, "diagram.png");

        var response = await Fetch(asset.Url, "image/png,*/*;q=0.1");
        var served = await response.Content.ReadAsByteArrayAsync(Cancel);

        Assert.Equal(("diagram.webp", "image/webp", "image/png", (long)png.Length), (asset.FileName, asset.ContentType, asset.OriginalContentType, asset.OriginalSize));
        Assert.EndsWith("/diagram.webp", asset.Url, StringComparison.Ordinal);
        Assert.True(asset.Size < png.Length);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.Vary);
        Assert.True(SamePixels(png, served));

        var version = (await _admin.GetFromJsonAsync<VersionList>($"/api/v1/assets/{asset.Id}/versions", Json, Cancel))!.Items.Single();
        Assert.Equal(("image/png", (long)png.Length), (version.OriginalContentType, version.OriginalSize));
        Assert.Equal(HttpStatusCode.OK, (await Fetch(asset.Url.Replace(".webp", ".png", StringComparison.Ordinal))).StatusCode);
    }

    [Fact]
    public async Task StoresScreenshotsOfPhotosAsLossyWebP()
    {
        var png = PhotoPng(800, 400);
        var asset = await Upload(png, "landscape.png");
        var lossless = await UploadLossless(png, "landscape-exact.png", "image/png");

        Assert.Equal(("landscape.webp", "image/webp"), (asset.FileName, asset.ContentType));
        Assert.True(asset.Size * Imaging.ImageProcessor.LosslessAllowance < lossless.Size, $"lossy {asset.Size}, lossless {lossless.Size}");
        Assert.False(SamePixels(png, await (await Fetch(asset.Url)).Content.ReadAsByteArrayAsync(Cancel)));
        Assert.True(SamePixels(png, await (await Fetch(lossless.Url)).Content.ReadAsByteArrayAsync(Cancel)));
    }

    [Fact]
    public async Task KeepsJpegUnchangedWhenLosslessIsAsked()
    {
        var jpeg = SampleImages.Jpeg(400, 300);
        var asset = await UploadLossless(jpeg, "photo.jpg", "image/jpeg");

        Assert.Equal(("photo.jpg", "image/jpeg", null), (asset.FileName, asset.ContentType, asset.OriginalContentType));
        Assert.Equal(jpeg, await (await Fetch(asset.Url)).Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task StoresPhotosAsLossyWebP()
    {
        var jpeg = SampleImages.Jpeg(1200, 800);
        var asset = await Upload(jpeg, "beach.jpg", "image/jpeg");

        Assert.Equal(("beach.webp", "image/webp", "image/jpeg"), (asset.FileName, asset.ContentType, asset.OriginalContentType));
        Assert.True(asset.Size < jpeg.Length);
        Assert.Equal((1200, 800, 1), SampleImages.Measure(await (await Fetch(asset.Url)).Content.ReadAsByteArrayAsync(Cancel)));
    }

    [Fact]
    public async Task KeepsFormatsThatNeedNoConversion()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>"u8.ToArray();

        var vector = await Upload(svg, "icon.svg", "image/svg+xml");
        var animation = await Upload(SampleImages.AnimatedGif(100, 50, 2), "spinner.gif", "image/gif");

        Assert.Equal(("icon.svg", null), (vector.FileName, vector.OriginalContentType));
        Assert.Equal(("spinner.gif", "image/gif", null), (animation.FileName, animation.ContentType, animation.OriginalContentType));
    }

    [Fact]
    public void KeepsTheFormatWhenWebPIsNotSmaller()
    {
        var png = SampleImages.Png(50, 50);
        var info = Imaging.ImageDetector.DetectRaster(png)!;

        Assert.NotNull(Imaging.ImageProcessor.ToWebP(png, info, animated: false, currentSize: png.Length));
        Assert.Null(Imaging.ImageProcessor.ToWebP(png, info, animated: false, currentSize: 10));
        Assert.Null(Imaging.ImageProcessor.ToWebP(png, info, animated: true, currentSize: png.Length));
    }

    [Fact]
    public async Task RenamesTheAssetWhenANewVersionChangesTheFormat()
    {
        await TurnConversion(on: false);
        var asset = await Upload(SampleImages.Png(400, 200), "chart.png");
        await TurnConversion(on: true);

        var content = new ByteArrayContent(SampleImages.Png(401, 200));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await _admin.PostAsync($"/api/v1/assets/{asset.Id}/versions", content, Cancel);
        var added = (await response.Content.ReadFromJsonAsync<VersionAdded>(Json, Cancel))!;
        var versions = (await _admin.GetFromJsonAsync<VersionList>($"/api/v1/assets/{asset.Id}/versions", Json, Cancel))!.Items;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(added.FormatChanged);
        Assert.Equal(("chart.webp", "image/webp"), (added.Asset.FileName, added.Asset.ContentType));
        Assert.Equal(["/v2/chart.webp", "/v1/chart.png"], versions.Select(v => v.Url[v.Url.LastIndexOf("/v", StringComparison.Ordinal)..]));
        Assert.Equal("image/webp", (await Fetch(asset.Url)).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StoresOnlyTheConvertedFile()
    {
        var asset = await Upload(GraphicPng(300, 300), "only.png");
        var blobs = Directory.EnumerateFiles(Path.Combine(_server.DataDirectory, "blobs"), "*", SearchOption.AllDirectories).ToList();
        var project = (await _admin.GetFromJsonAsync<ProjectList>("/api/v1/projects", Json, Cancel))!.Items.Single(p => p.Slug == "default");

        Assert.Equal(asset.Sha256, Path.GetFileName(Assert.Single(blobs)));
        Assert.Null(_server.Services.GetRequiredService<AssetStore>().Versions(asset.Id).Single().Original!.BlobSha256);
        Assert.Equal(asset.Size, project.UsedBytes);
    }

    [Fact]
    public async Task ExportsAndImportsWhatTheUploadWas()
    {
        var png = GraphicPng(320, 160);
        var asset = await Upload(png, "moved.png");

        var export = await _admin.GetByteArrayAsync("/api/v1/projects/default/export", Cancel);
        var imported = await _admin.PostAsync("/api/v1/projects/import?slug=copy", new ByteArrayContent(export), Cancel);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);

        var copy = (await _admin.GetFromJsonAsync<AssetList>("/api/v1/projects/copy/assets", Json, Cancel))!.Items.Single();
        Assert.Equal(("moved.webp", asset.Sha256, "image/png", (long)png.Length), (copy.FileName, copy.Sha256, copy.OriginalContentType, copy.OriginalSize));
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

        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
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
    public async Task OwnersCanTurnConversionOff()
    {
        await TurnConversion(on: false);
        var png = SampleImages.Png(800, 400);
        var asset = await Upload(png, "photo.png");

        var response = await Fetch(asset.Url, "image/avif,image/webp,*/*");

        Assert.Equal(("photo.png", "image/png", null), (asset.FileName, asset.ContentType, asset.OriginalContentType));
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await response.Content.ReadAsByteArrayAsync(Cancel));
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

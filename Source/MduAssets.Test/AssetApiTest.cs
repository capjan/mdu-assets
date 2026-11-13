using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Imaging.Test;

namespace MduAssets.Test;

public sealed class AssetApiTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private async Task<AssetResponse> Upload(string name, uint width = 4, uint height = 3)
    {
        var content = new ByteArrayContent(TestImages.Png(width, height));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await _server.Ui().PostAsync($"/api/v1/projects/default/assets?name={name}", content, TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task ReportsTheSignedInUser()
    {
        var me = await _server.Ui().GetFromJsonAsync<MeResponse>("/api/v1/me", Json, TestContext.Current.CancellationToken);

        Assert.Equal(new MeResponse(TestServer.AdminName, IsAdmin: true), me);
    }

    [Fact]
    public async Task ListsProjectsWithCountsAndSizes()
    {
        await Upload("a.png");
        await Upload("b.png");

        var projects = await _server.Ui().GetFromJsonAsync<ProjectList>("/api/v1/projects", Json, TestContext.Current.CancellationToken);

        var project = Assert.Single(projects!.Items);
        Assert.Equal(("default", "Default", 2), (project.Slug, project.Name, project.AssetCount));
        Assert.Equal(2 * TestImages.Png(4, 3).Length, project.TotalSize);
    }

    [Fact]
    public async Task ListsTheAssetsOfAProjectNewestFirst()
    {
        var first = await Upload("first.png");
        var second = await Upload("second.png");

        var list = await _server.Ui().GetFromJsonAsync<AssetList>("/api/v1/projects/default/assets", Json, TestContext.Current.CancellationToken);

        Assert.Equal([second.Id, first.Id], list!.Items.Select(a => a.Id));
        Assert.Equal(second.Markdown, list.Items[0].Markdown);
    }

    [Fact]
    public async Task ReturnsOneAsset()
    {
        var asset = await Upload("one.png");

        var loaded = await _server.Ui().GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{asset.Id}", Json, TestContext.Current.CancellationToken);

        Assert.Equal(asset, loaded);
    }

    [Fact]
    public async Task ChangesNameAndAltTextWithoutBreakingTheLink()
    {
        var asset = await Upload("screenshot.png");

        var response = await _server.Ui().PatchAsJsonAsync($"/api/v1/assets/{asset.Id}",
            new AssetChanges("Settings Dialog.jpg", "The settings dialog [dark]"), Json, TestContext.Current.CancellationToken);
        var updated = await response.Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Settings-Dialog.png", updated!.FileName);
        Assert.Equal($"http://img.test/i/{asset.Id}/Settings-Dialog.png", updated.Url);
        Assert.Equal(@"![The settings dialog \[dark\]](" + updated.Url + ")", updated.Markdown);
        Assert.Equal(HttpStatusCode.OK, (await _server.Public().GetAsync(asset.Url, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task KeepsWhatIsLeftOut()
    {
        var asset = await Upload("keep.png");

        var updated = await (await _server.Ui().PatchAsJsonAsync($"/api/v1/assets/{asset.Id}", new AssetChanges(null, "New text"), Json,
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<AssetResponse>(Json, TestContext.Current.CancellationToken);

        Assert.Equal("keep.png", updated!.FileName);
        Assert.Equal("New text", updated.AltText);
    }

    [Fact]
    public async Task RejectsEmptyAltText()
    {
        var asset = await Upload("x.png");

        var response = await _server.Ui().PatchAsJsonAsync($"/api/v1/assets/{asset.Id}", new AssetChanges(null, "  "), Json, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/assets/AAAAAAAAAA")]
    [InlineData("/api/v1/assets/not-an-id")]
    [InlineData("/api/v1/projects/nope/assets")]
    public async Task AnswersUnknownThingsWithNotFound(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Ui().GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/projects/default/assets")]
    public async Task RequiresSignIn(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _server.Ui(user: null).GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
    }
}

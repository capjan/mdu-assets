using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MduAssets.Api;
using MduAssets.Imaging.Test;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MduAssets.Test;

/// <summary>Tags as the way to organize images: filters with several tags, tagging many images, renaming and deleting tags (ORG-03, ORG-04).</summary>
public sealed class TagsTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new TestServer().KeepUploadedFormats();
    private readonly HttpClient _admin;
    private int _width = 4;

    public TagsTest()
    {
        _admin = _server.Ui();
    }

    public void Dispose() => _server.Dispose();

    private async Task<AssetResponse> Upload(string name, params string[] tags)
    {
        var content = new ByteArrayContent(SampleImages.Png(_width++, 3));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var query = string.Concat(tags.Select(t => $"&tag={Uri.EscapeDataString(t)}"));
        var response = await _admin.PostAsync($"/api/v1/projects/default/assets?name={name}{query}", content, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    private async Task<List<string>> Names(string query) =>
        (await _admin.GetFromJsonAsync<AssetList>($"/api/v1/projects/default/assets{query}", Json, Cancel))!
            .Items.Select(a => a.FileName).Order(StringComparer.Ordinal).ToList();

    private async Task<List<string>> TagsOf(string id) => [.. (await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{id}", Json, Cancel))!.Tags];

    private Task<HttpResponseMessage> Change(TagChange change, string project = "default") =>
        _admin.PostAsJsonAsync($"/api/v1/projects/{project}/assets/tags", change, Json, Cancel);

    [Fact]
    public async Task UploadsWithTags()
    {
        var asset = await Upload("setup.png", "Setup", "macOS", "setup");

        Assert.Equal(["macOS", "Setup"], asset.Tags.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ShowsOnlyImagesWithEveryChosenTag()
    {
        await Upload("both.png", "Setup", "macOS");
        await Upload("setup.png", "Setup");
        await Upload("mac.png", "macOS");
        await Upload("none.png");

        Assert.Equal(["both.png", "setup.png"], await Names("?tag=setup"));
        Assert.Equal(["both.png"], await Names("?tag=Setup&tag=MACOS"));
        Assert.Equal(["none.png"], await Names("?untagged=true"));
        Assert.Empty(await Names("?tag=Setup&tag=Windows"));
    }

    [Fact]
    public async Task TagsSeveralImagesAtOnceAndKeepsTheSpelling()
    {
        var a = await Upload("a.png", "Dark Mode");
        var b = await Upload("b.png", "Old");
        var c = await Upload("c.png", "Old");

        var response = await Change(new TagChange([a.Id, b.Id, "AAAAAAAAAA"], ["dark mode", "Setup"], ["old"]));
        var result = await response.Content.ReadFromJsonAsync<TagChangeResult>(Json, Cancel);

        Assert.Equal((HttpStatusCode.OK, 2), (response.StatusCode, result!.Changed));
        Assert.Equal(["Dark Mode", "Setup"], (await TagsOf(a.Id)).Order(StringComparer.Ordinal));
        Assert.Equal(["Dark Mode", "Setup"], (await TagsOf(b.Id)).Order(StringComparer.Ordinal));
        Assert.Equal(["Old"], await TagsOf(c.Id));
    }

    [Fact]
    public async Task RefusesTooManyTagsWithoutChangingAnything()
    {
        var asset = await Upload("full.png", [.. Enumerable.Range(1, TagNames.MaxPerAsset).Select(i => $"t{i}")]);
        var other = await Upload("other.png");

        var response = await Change(new TagChange([other.Id, asset.Id], ["one more"], []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await TagsOf(other.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(new TagChange([asset.Id], [], []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(new TagChange([], ["x"], []))).StatusCode);
    }

    [Fact]
    public async Task RenamesAndMergesTags()
    {
        var a = await Upload("a.png", "screenshot");
        var b = await Upload("b.png", "Screenshots", "screenshot");
        var c = await Upload("c.png", "Screenshots");

        var renamed = await _admin.PatchAsJsonAsync("/api/v1/projects/default/tags", new TagRename("SCREENSHOT", "Screenshots"), Json, Cancel);
        var cased = await _admin.PatchAsJsonAsync("/api/v1/projects/default/tags", new TagRename("screenshots", "Screen shots "), Json, Cancel);

        Assert.Equal((HttpStatusCode.OK, 2), (renamed.StatusCode, (await renamed.Content.ReadFromJsonAsync<TagResult>(Json, Cancel))!.Assets));
        Assert.Equal(HttpStatusCode.OK, cased.StatusCode);
        Assert.Equal(["Screen shots"], await TagsOf(a.Id));
        Assert.Equal(["Screen shots"], await TagsOf(b.Id));
        Assert.Equal(["Screen shots"], await TagsOf(c.Id));
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PatchAsJsonAsync("/api/v1/projects/default/tags", new TagRename("missing", "x"), Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task DeletesATagFromAllImages()
    {
        var a = await Upload("a.png", "Draft", "Setup");
        await Upload("b.png", "draft");

        var response = await _admin.DeleteAsync("/api/v1/projects/default/tags?name=DRAFT", Cancel);

        Assert.Equal((HttpStatusCode.OK, 2), (response.StatusCode, (await response.Content.ReadFromJsonAsync<TagResult>(Json, Cancel))!.Assets));
        Assert.Equal(["Setup"], await TagsOf(a.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync("/api/v1/projects/default/tags?name=Draft", Cancel)).StatusCode);
    }

    [Fact]
    public async Task ViewersCannotChangeTags()
    {
        var asset = await Upload("a.png", "Setup");
        var users = _server.Services.GetRequiredService<UserStore>();
        var projects = _server.Services.GetRequiredService<ProjectStore>();
        var viewer = users.Create("vic", PasswordHasher.Hash("viewer-password"), isAdmin: false);
        projects.SetRole(projects.Find("default")!.Id, viewer.Id, ProjectRole.Viewer);
        var client = _server.Ui("vic", "viewer-password");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/projects/default/assets?tag=Setup", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/projects/default/assets/tags", new TagChange([asset.Id], ["x"], []), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/v1/projects/default/tags?name=Setup", Cancel)).StatusCode);
    }

    [Fact]
    public async Task ImportsFoldersOfOldExportsAsTags()
    {
        var asset = await Upload("deep.png", "Draft");
        var export = await _admin.GetByteArrayAsync("/api/v1/projects/default/export", Cancel);

        // Turn the export into format 1, as servers with folders wrote it: Guides/Setup holds the image.
        using var buffer = new MemoryStream();
        buffer.Write(export);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(TransferEndpoints.ManifestName)!;
            JsonNode manifest;
            using (var reader = entry.Open())
                manifest = JsonNode.Parse(reader)!;
            Assert.Null(manifest["folders"]);
            manifest["format"] = 1;
            manifest["folders"] = new JsonArray(
                new JsonObject { ["id"] = 7, ["parentId"] = null, ["name"] = "Guides" },
                new JsonObject { ["id"] = 8, ["parentId"] = 7, ["name"] = "Setup" });
            manifest["assets"]![0]!["folderId"] = 8;
            entry.Delete();
            using var writer = zip.CreateEntry(TransferEndpoints.ManifestName).Open();
            JsonSerializer.Serialize(writer, manifest);
        }

        var response = await _admin.PostAsync("/api/v1/projects/import?slug=old", new ByteArrayContent(buffer.ToArray()), Cancel);
        var copy = (await _admin.GetFromJsonAsync<AssetList>("/api/v1/projects/old/assets", Json, Cancel))!.Items.Single();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["Draft", "Guides", "Setup"], copy.Tags.Order(StringComparer.Ordinal));
        Assert.Equal(asset.FileName, copy.FileName);
    }
}

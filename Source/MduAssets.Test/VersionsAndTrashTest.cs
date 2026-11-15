using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Imaging.Test;
using MduAssets.Maintenance;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MduAssets.Test;

/// <summary>New versions under the same link, version links and the trash (M6).</summary>
public sealed class VersionsAndTrashTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string EditorHash = PasswordHasher.Hash("editor-password");
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new();
    private readonly HttpClient _admin;

    public VersionsAndTrashTest()
    {
        _admin = _server.Ui();
    }

    public void Dispose() => _server.Dispose();

    private static ByteArrayContent Image(byte[] data, string type = "image/png")
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    private async Task<AssetResponse> Upload(byte[] data, string name)
    {
        var response = await _admin.PostAsync($"/api/v1/projects/default/assets?name={name}", Image(data), Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    private async Task<VersionAdded> Replace(string id, byte[] data, string type = "image/png")
    {
        var response = await _admin.PostAsync($"/api/v1/assets/{id}/versions", Image(data, type), Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VersionAdded>(Json, Cancel))!;
    }

    private Task<byte[]> Bytes(string url) => _server.Public().GetByteArrayAsync(url, Cancel);

    private async Task<HttpClient> Editor()
    {
        var users = _server.Services.GetRequiredService<UserStore>();
        var projects = _server.Services.GetRequiredService<ProjectStore>();
        projects.SetRole(projects.Find("default")!.Id, users.Create("ed", EditorHash, isAdmin: false).Id, ProjectRole.Editor);
        return await Task.FromResult(_server.Ui("ed", "editor-password"));
    }

    [Fact]
    public async Task ReplacesAnImageUnderTheSameLink()
    {
        var first = SampleImages.Png(200, 100);
        var second = SampleImages.Png(300, 100);
        var asset = await Upload(first, "chart.png");

        var added = await Replace(asset.Id, second);

        Assert.Equal((asset.Url, 2, 300), (added.Asset.Url, added.Asset.Version, added.Asset.Width));
        Assert.False(added.FormatChanged);
        Assert.Equal(second, await Bytes(asset.Url));
        Assert.Equal(first, await Bytes(asset.Url.Replace("/chart.png", "/v1/chart.png", StringComparison.Ordinal)));
        Assert.Equal(second, await Bytes(added.Asset.VersionUrl));

        var pinned = await _server.Public().GetAsync(added.Asset.VersionUrl, Cancel);
        Assert.Contains("immutable", pinned.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Public().GetAsync(asset.Url.Replace("/chart.png", "/v9/chart.png", StringComparison.Ordinal), Cancel)).StatusCode);

        var versions = await _admin.GetFromJsonAsync<VersionList>($"/api/v1/assets/{asset.Id}/versions", Json, Cancel);
        Assert.Equal([(2, true, 300), (1, false, 200)], versions!.Items.Select(v => (v.Number, v.IsCurrent, v.Width ?? 0)));
        Assert.Equal(TestServer.AdminName, versions.Items[0].CreatedBy);
    }

    [Fact]
    public async Task AllowsAnotherFormatAndSaysSo()
    {
        var asset = await Upload(SampleImages.Png(200, 100), "diagram.png");

        var added = await Replace(asset.Id, SampleImages.Jpeg(200, 100), "image/jpeg");
        var response = await _server.Public().GetAsync(asset.Url, Cancel);

        Assert.True(added.FormatChanged);
        Assert.Equal("image/png", added.PreviousContentType);
        Assert.Equal("diagram.png", added.Asset.FileName);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RestoresAnOldVersionAsANewOne()
    {
        var first = SampleImages.Png(200, 100);
        var asset = await Upload(first, "chart.png");
        await Replace(asset.Id, SampleImages.Png(300, 100));

        var restored = await (await _admin.PostAsync($"/api/v1/assets/{asset.Id}/versions/1/restore", null, Cancel))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);

        Assert.Equal(3, restored!.Version);
        Assert.Equal(first, await Bytes(asset.Url));
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/v1/assets/{asset.Id}/versions/3/restore", null, Cancel)).StatusCode);
    }

    [Fact]
    public async Task OwnersDeleteOldVersions()
    {
        var editor = await Editor();
        var asset = await Upload(SampleImages.Png(200, 100), "chart.png");
        await Replace(asset.Id, SampleImages.Png(300, 100));
        var v1 = asset.Url.Replace("/chart.png", "/v1/chart.png", StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync($"/api/v1/assets/{asset.Id}/versions/1", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"/api/v1/assets/{asset.Id}/versions/2", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/assets/{asset.Id}/versions/1", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Public().GetAsync(v1, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _server.Public().GetAsync(asset.Url, Cancel)).StatusCode);
    }

    [Fact]
    public async Task KeepsOnlyTheConfiguredNumberOfVersions()
    {
        await _admin.PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges(null, null, MaxVersions: 2), Json, Cancel);
        var asset = await Upload(SampleImages.Png(100, 100), "chart.png");
        await Replace(asset.Id, SampleImages.Png(101, 100));
        await Replace(asset.Id, SampleImages.Png(102, 100));

        var versions = await _admin.GetFromJsonAsync<VersionList>($"/api/v1/assets/{asset.Id}/versions", Json, Cancel);

        Assert.Equal([3, 2], versions!.Items.Select(v => v.Number));
    }

    [Fact]
    public async Task MovesImagesToTheTrashAndBack()
    {
        var editor = await Editor();
        var asset = await Upload(SampleImages.Png(200, 100), "old.png");

        var trashed = await (await editor.DeleteAsync($"/api/v1/assets/{asset.Id}", Cancel)).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);

        Assert.NotNull(trashed!.DeletedAt);
        Assert.Equal(HttpStatusCode.Gone, (await _server.Public().GetAsync(asset.Url, Cancel)).StatusCode);
        Assert.Empty((await _admin.GetFromJsonAsync<AssetList>("/api/v1/projects/default/assets", Json, Cancel))!.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PatchAsJsonAsync($"/api/v1/assets/{asset.Id}", new AssetChanges("x.png", null), Json, Cancel)).StatusCode);
        var trash = await editor.GetFromJsonAsync<TrashList>("/api/v1/projects/default/trash", Json, Cancel);
        var item = Assert.Single(trash!.Items);
        Assert.Equal(("ed", 30), (item.DeletedBy, (item.DeletesAt - item.DeletedAt).Days));

        Assert.Equal(HttpStatusCode.OK, (await editor.PostAsync($"/api/v1/assets/{asset.Id}/restore", null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _server.Public().GetAsync(asset.Url, Cancel)).StatusCode);
        Assert.Single((await _admin.GetFromJsonAsync<AssetList>("/api/v1/projects/default/assets", Json, Cancel))!.Items);
    }

    [Fact]
    public async Task EmptyingTheTrashDeletesForGood()
    {
        var editor = await Editor();
        var asset = await Upload(SampleImages.Png(200, 100), "old.png");
        await _admin.DeleteAsync($"/api/v1/assets/{asset.Id}", Cancel);
        var blob = _server.Services.GetRequiredService<BlobStore>().PathOf(asset.Sha256);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync("/api/v1/projects/default/trash", Cancel)).StatusCode);
        var result = await (await _admin.DeleteAsync("/api/v1/projects/default/trash", Cancel)).Content.ReadFromJsonAsync<PurgeResult>(Json, Cancel);

        Assert.Equal(1, result!.Deleted);
        Assert.False(File.Exists(blob));
        Assert.Equal(HttpStatusCode.Gone, (await _server.Public().GetAsync(asset.Url, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PostAsync($"/api/v1/assets/{asset.Id}/restore", null, Cancel)).StatusCode);
        Assert.Empty((await _admin.GetFromJsonAsync<TrashList>("/api/v1/projects/default/trash", Json, Cancel))!.Items);
    }

    [Fact]
    public async Task KeepsBlobsThatOtherAssetsStillUse()
    {
        var png = SampleImages.Png(200, 100);
        var first = await Upload(png, "a.png");
        var second = await Upload(png, "b.png");
        await _admin.DeleteAsync($"/api/v1/assets/{first.Id}", Cancel);

        await _admin.DeleteAsync($"/api/v1/projects/default/trash/{first.Id}", Cancel);

        Assert.Equal(png, await Bytes(second.Url));
    }

    [Fact]
    public async Task DeletesExpiredTrashAfterTheRetentionPeriod()
    {
        var asset = await Upload(SampleImages.Png(200, 100), "old.png");
        await _admin.DeleteAsync($"/api/v1/assets/{asset.Id}", Cancel);
        var services = _server.Services;
        TrashCleanup At(TimeSpan later) => new(services.GetRequiredService<AssetStore>(), services.GetRequiredService<BlobCleanup>(), services.GetRequiredService<AuditStore>(),
            services.GetRequiredService<ServerSettings>(), new ShiftedTime(later), NullLogger<TrashCleanup>.Instance);

        Assert.Equal(0, At(TimeSpan.FromDays(29)).RunOnce());
        Assert.Equal(1, At(TimeSpan.FromDays(31)).RunOnce());
        Assert.Empty((await _admin.GetFromJsonAsync<TrashList>("/api/v1/projects/default/trash", Json, Cancel))!.Items);
    }

    [Fact]
    public async Task DeletesAProjectOnceItsImagesAreInTheTrash()
    {
        await _admin.PostAsJsonAsync("/api/v1/projects", new NewProject("old", "Old", null), Json, Cancel);
        var response = await _admin.PostAsync("/api/v1/projects/old/assets?name=a.png", Image(SampleImages.Png(20, 10)), Cancel);
        var asset = (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync("/api/v1/projects/old", Cancel)).StatusCode);
        await _admin.DeleteAsync($"/api/v1/assets/{asset.Id}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync("/api/v1/projects/old", Cancel)).StatusCode);

        // The id is retired: the link stays dead and no new asset can get it.
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Public().GetAsync(asset.Url, Cancel)).StatusCode);
        using var connection = _server.Services.GetRequiredService<Database>().Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM retired_asset_ids WHERE id = $id;";
        command.Parameters.AddWithValue("$id", asset.Id);
        Assert.Equal(1L, command.ExecuteScalar());
    }

    private sealed class ShiftedTime(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + offset;
    }
}

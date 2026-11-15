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

namespace MduAssets.Test;

/// <summary>Limits, quotas, rate limits, the audit log, export and import, and backups (M9).</summary>
public sealed class OperationsTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string OwnerHash = PasswordHasher.Hash("owner-password");
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly List<TestServer> _servers = [];

    public void Dispose()
    {
        foreach (var server in _servers)
            server.Dispose();
    }

    private TestServer Server(Dictionary<string, string?>? settings = null)
    {
        var server = new TestServer(settings);
        _servers.Add(server);
        return server.KeepUploadedFormats();
    }

    private static ByteArrayContent Png(int width, int height = 50)
    {
        var content = new ByteArrayContent(SampleImages.Png(width, height));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    private static async Task<AssetResponse> Upload(HttpClient client, string name, int width, string project = "default")
    {
        var response = await client.PostAsync($"/api/v1/projects/{project}/assets?name={name}", Png(width), Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    private static HttpClient Owner(TestServer server)
    {
        var users = server.Services.GetRequiredService<UserStore>();
        var projects = server.Services.GetRequiredService<ProjectStore>();
        projects.SetRole(projects.Find("default")!.Id, users.Create("olga", OwnerHash, isAdmin: false).Id, ProjectRole.Owner);
        return server.Ui("olga", "owner-password");
    }

    [Fact]
    public async Task OwnersLowerTheUploadLimit()
    {
        var server = Server();
        var owner = Owner(server);

        await owner.PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges(null, null, MaxFileBytes: 2000), Json, Cancel);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await owner.PostAsync("/api/v1/projects/default/assets?name=big.png", Png(400, 300), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/v1/projects/default/assets?name=small.png", Png(4, 3), Cancel)).StatusCode);
    }

    [Fact]
    public async Task AdminsSetQuotasThatCountAllVersionsAndTheTrash()
    {
        var server = Server();
        var owner = Owner(server);
        var admin = server.Ui();
        var first = await Upload(admin, "a.png", 60);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges(null, null, QuotaBytes: 1), Json, Cancel)).StatusCode);
        var limited = await (await admin.PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges(null, null, QuotaBytes: first.Size + 10), Json, Cancel))
            .Content.ReadFromJsonAsync<ProjectResponse>(Json, Cancel);
        Assert.Equal((first.Size, first.Size + 10), (limited!.UsedBytes, limited.QuotaBytes!.Value));

        await admin.DeleteAsync($"/api/v1/assets/{first.Id}", Cancel);
        var refused = await owner.PostAsync("/api/v1/projects/default/assets?name=b.png", Png(61), Cancel);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(Quotas.ProblemType, problem.GetProperty("type").GetString());

        await admin.DeleteAsync("/api/v1/projects/default/trash", Cancel);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/v1/projects/default/assets?name=b.png", Png(61), Cancel)).StatusCode);
    }

    [Fact]
    public async Task SlowsDownGuessedPasswords()
    {
        var server = Server(new() { ["SignInFailures"] = "3" });

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await server.Ui(TestServer.AdminName, "wrong-" + i).GetAsync("/api/v1/me", Cancel)).StatusCode);
        var blocked = await server.Ui().GetAsync("/api/v1/me", Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
        Assert.Empty(blocked.Headers.WwwAuthenticate);
        var log = server.Services.GetRequiredService<AuditStore>().List(null, 10);
        Assert.Equal(3, log.Count(e => e.Action == "sign-in.failed"));
    }

    [Fact]
    public async Task SlowsDownGuessedIdsAndUploads()
    {
        var server = Server(new() { ["PublicNotFoundPerMinute"] = "3", ["UploadsPerMinute"] = "2" });
        var admin = server.Ui();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await server.Public().GetAsync($"/i/AAAAAAAAA{i}/x.png", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await server.Public().GetAsync("/i/AAAAAAAAAA/x.png", Cancel)).StatusCode);

        await Upload(admin, "a.png", 10);
        await Upload(admin, "b.png", 11);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await admin.PostAsync("/api/v1/projects/default/assets?name=c.png", Png(12), Cancel)).StatusCode);
    }

    [Fact]
    public async Task WritesTheAuditLog()
    {
        var server = Server();
        var owner = Owner(server);
        var admin = server.Ui();
        var asset = await Upload(owner, "chart.png", 40);
        await owner.PatchAsJsonAsync($"/api/v1/assets/{asset.Id}", new AssetChanges(null, "Chart"), Json, Cancel);
        await owner.DeleteAsync($"/api/v1/assets/{asset.Id}", Cancel);
        await admin.PostAsJsonAsync("/api/v1/users", new NewUser("nina", false), Json, Cancel);

        var project = await owner.GetFromJsonAsync<AuditList>("/api/v1/projects/default/audit", Json, Cancel);
        var all = await admin.GetFromJsonAsync<AuditList>("/api/v1/admin/audit", Json, Cancel);

        Assert.Equal(["asset.trash", "asset.update", "asset.upload"], project!.Items.Select(e => e.Action));
        Assert.All(project.Items, e => Assert.Equal(("olga", "default", asset.Id), (e.User, e.Project, e.Target)));
        Assert.All(project.Items, e => Assert.Null(e.Ip));
        Assert.Contains(all!.Items, e => e is { Action: "user.create", Target: "nina", User: TestServer.AdminName });
        Assert.Contains(all.Items, e => e.Action == "sign-in" && e.User == "olga");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/audit", Cancel)).StatusCode);

        var audit = server.Services.GetRequiredService<AuditStore>();
        Assert.Equal(0, audit.DeleteBefore(DateTimeOffset.UtcNow.AddDays(-365)));
        Assert.True(audit.DeleteBefore(DateTimeOffset.UtcNow.AddMinutes(1)) > 0);
    }

    [Fact]
    public async Task ExportsAndImportsProjects()
    {
        var source = Server();
        var admin = source.Ui();
        var folder = await (await admin.PostAsJsonAsync("/api/v1/projects/default/folders", new NewFolder("Screens", null), Json, Cancel))
            .Content.ReadFromJsonAsync<FolderResponse>(Json, Cancel);
        var light = await Upload(admin, $"settings-light.png?folder={folder!.Id}".Replace("?", "&", StringComparison.Ordinal), 60);
        var dark = await Upload(admin, "settings-dark.png", 61);
        await admin.PatchAsJsonAsync($"/api/v1/assets/{light.Id}", new AssetChanges(null, "Settings", "The settings dialog", ["ui"]), Json, Cancel);
        await admin.PutAsJsonAsync($"/api/v1/assets/{light.Id}/pair", new PairChange(dark.Id, "light"), Json, Cancel);
        await admin.PostAsync($"/api/v1/assets/{dark.Id}/versions", Png(62), Cancel);

        var export = await admin.GetAsync("/api/v1/projects/default/export?versions=all", Cancel);
        var zip = await export.Content.ReadAsByteArrayAsync(Cancel);
        Assert.Equal("application/zip", export.Content.Headers.ContentType?.MediaType);

        // Into another instance: the ids are free, so links keep their ids.
        var target = Server();
        var imported = await Import(target.Ui(), zip, "docs");
        Assert.Empty(imported.ChangedIds);
        Assert.Equal(2, imported.Assets);
        var copy = await target.Ui().GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{light.Id}", Json, Cancel);
        Assert.Equal(("docs", "The settings dialog", dark.Id), (copy!.Project, copy.Description, copy.Pair!.Id));
        Assert.Equal(["ui"], copy.Tags);
        Assert.NotNull(copy.FolderId);
        Assert.Equal(2, (await target.Ui().GetFromJsonAsync<VersionList>($"/api/v1/assets/{dark.Id}/versions", Json, Cancel))!.Items.Count);
        Assert.Equal(await source.Public().GetByteArrayAsync(light.Url, Cancel), await target.Public().GetByteArrayAsync(copy.Url, Cancel));

        // Into the same instance again: the ids are taken, so the assets get new ones and the answer maps them.
        var again = await Import(admin, zip, "docs-copy");
        Assert.Equal(2, again.ChangedIds.Count);
        Assert.Contains(again.ChangedIds, c => c.OldId == light.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/v1/projects/import?slug=docs-copy", new ByteArrayContent(zip), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/projects/import?slug=junk", new ByteArrayContent([1, 2, 3]), Cancel)).StatusCode);
    }

    private static async Task<ImportResult> Import(HttpClient client, byte[] zip, string slug)
    {
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var response = await client.PostAsync($"/api/v1/projects/import?slug={slug}", content, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportResult>(Json, Cancel))!;
    }

    [Fact]
    public async Task BacksUpAnInstanceThatCanBeStartedAgain()
    {
        var server = Server();
        var asset = await Upload(server.Ui(), "chart.png", 40);
        var target = Path.Combine(Path.GetTempPath(), "mdu-assets-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, BackupCommand.Run(server.DataDirectory, target));
            Assert.Equal(1, BackupCommand.Run(server.DataDirectory, target));

            var restored = Server(new() { ["DataDirectory"] = target });
            Assert.Equal(await server.Public().GetByteArrayAsync(asset.Url, Cancel), await restored.Public().GetByteArrayAsync(asset.Url, Cancel));
            Assert.Equal(asset.Id, (await restored.Ui().GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{asset.Id}", Json, Cancel))!.Id);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }
    }
}

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

/// <summary>Folders, tags, search and the reverse lookup (ORG-01 – ORG-04, LNK-09).</summary>
public sealed class OrganizeTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string ViewerHash = PasswordHasher.Hash("viewer-password");
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new();
    private readonly HttpClient _admin;

    public OrganizeTest()
    {
        _admin = _server.Ui();
    }

    public void Dispose() => _server.Dispose();

    private async Task<FolderResponse> CreateFolder(string name, long? parentId = null)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/projects/default/folders", new NewFolder(name, parentId), Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FolderResponse>(Json, Cancel))!;
    }

    private async Task<AssetResponse> Upload(string name, long? folderId = null, uint width = 4)
    {
        var content = new ByteArrayContent(TestImages.Png(width, 3));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var folder = folderId is null ? "" : $"&folder={folderId}";
        var response = await _admin.PostAsync($"/api/v1/projects/default/assets?name={name}{folder}", content, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    private async Task<List<string>> Search(string query) =>
        (await _admin.GetFromJsonAsync<AssetList>($"/api/v1/projects/default/assets{query}", Json, Cancel))!
            .Items.Select(a => a.FileName).Order(StringComparer.Ordinal).ToList();

    private Task<HttpResponseMessage> Patch(string id, AssetChanges changes) =>
        _admin.PatchAsJsonAsync($"/api/v1/assets/{id}", changes, Json, Cancel);

    [Fact]
    public async Task BuildsAFolderTree()
    {
        var guides = await CreateFolder("Guides");
        var setup = await CreateFolder("Setup", guides.Id);
        await Upload("a.png", setup.Id);

        var tree = await _admin.GetFromJsonAsync<FolderList>("/api/v1/projects/default/folders", Json, Cancel);

        Assert.Equal([("Guides", (long?)null, 0), ("Setup", guides.Id, 1)], tree!.Items.Select(f => (f.Name, f.ParentId, f.AssetCount)));
        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsJsonAsync("/api/v1/projects/default/folders", new NewFolder("guides", null), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PostAsJsonAsync("/api/v1/projects/default/folders", new NewFolder("X", 999), Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task RenamesAndMovesFoldersButNotIntoThemselves()
    {
        var guides = await CreateFolder("Guides");
        var setup = await CreateFolder("Setup", guides.Id);

        var renamed = await (await _admin.PatchAsJsonAsync($"/api/v1/projects/default/folders/{setup.Id}", new FolderChanges("Installation"), Json, Cancel))
            .Content.ReadFromJsonAsync<FolderResponse>(Json, Cancel);
        Assert.Equal("Installation", renamed!.Name);

        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsJsonAsync($"/api/v1/projects/default/folders/{guides.Id}/move", new FolderMove(setup.Id), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsJsonAsync($"/api/v1/projects/default/folders/{guides.Id}/move", new FolderMove(guides.Id), Json, Cancel)).StatusCode);

        var moved = await (await _admin.PostAsJsonAsync($"/api/v1/projects/default/folders/{setup.Id}/move", new FolderMove(null), Json, Cancel))
            .Content.ReadFromJsonAsync<FolderResponse>(Json, Cancel);
        Assert.Null(moved!.ParentId);
    }

    [Fact]
    public async Task DeletingAFolderKeepsItsContentAndLinks()
    {
        var guides = await CreateFolder("Guides");
        var setup = await CreateFolder("Setup", guides.Id);
        var image = await Upload("a.png", setup.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/projects/default/folders/{setup.Id}", Cancel)).StatusCode);
        var asset = await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{image.Id}", Json, Cancel);
        Assert.Equal(guides.Id, asset!.FolderId);
        Assert.Equal(HttpStatusCode.OK, (await _server.Public().GetAsync(image.Url, Cancel)).StatusCode);

        // A subfolder that would land next to a folder of the same name blocks the deletion.
        var other = await CreateFolder("Other");
        await CreateFolder("Guides", other.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"/api/v1/projects/default/folders/{other.Id}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task MovesSeveralAssetsWithoutChangingTheirLinks()
    {
        var folder = await CreateFolder("Screens");
        var a = await Upload("a.png");
        var b = await Upload("b.png");
        await _admin.PostAsJsonAsync("/api/v1/projects", new NewProject("other", "Other", null), Json, Cancel);
        var content = new ByteArrayContent(TestImages.Png(2, 2));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var foreign = (await (await _admin.PostAsync("/api/v1/projects/other/assets?name=c.png", content, Cancel))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;

        var result = await (await _admin.PostAsJsonAsync("/api/v1/projects/default/assets/move", new AssetMove([a.Id, b.Id, foreign.Id], folder.Id), Json, Cancel))
            .Content.ReadFromJsonAsync<AssetMoveResult>(Json, Cancel);

        Assert.Equal(2, result!.Moved);
        Assert.Equal(["a.png", "b.png"], await Search($"?folder={folder.Id}"));
        Assert.Equal(a.Url, (await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{a.Id}", Json, Cancel))!.Url);
        Assert.Null((await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{foreign.Id}", Json, Cancel))!.FolderId);

        await _admin.PostAsJsonAsync("/api/v1/projects/default/assets/move", new AssetMove([a.Id], null), Json, Cancel);
        Assert.Equal(["a.png"], await Search("?folder=none"));
    }

    [Fact]
    public async Task TagsAreCleanedAndSuggested()
    {
        var a = await Upload("a.png");
        var b = await Upload("b.png");

        var updated = await (await Patch(a.Id, new AssetChanges(null, null, "Settings dialog in dark mode", ["  dark   mode ", "Settings", "settings", ""])))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        await Patch(b.Id, new AssetChanges(null, null, null, ["Settings"]));

        Assert.Equal(["dark mode", "Settings"], updated!.Tags);
        Assert.Equal("Settings dialog in dark mode", updated.Description);
        var facets = await _admin.GetFromJsonAsync<FacetsResponse>("/api/v1/projects/default/facets", Json, Cancel);
        Assert.Equal([("Settings", 2), ("dark mode", 1)], facets!.Tags.Select(t => (t.Name, t.Count)));
        Assert.Equal(["admin"], facets.Uploaders);
        Assert.Equal(["image/png"], facets.Types);
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(a.Id, new AssetChanges(null, null, null, [new string('x', 51)]))).StatusCode);
    }

    [Fact]
    public async Task SearchesNamesTextAndTags()
    {
        var a = await Upload("login-screen.png");
        var b = await Upload("settings.png");
        await Upload("about_100%.png");
        await Patch(a.Id, new AssetChanges(null, "The sign-in form", null, ["auth"]));
        await Patch(b.Id, new AssetChanges(null, null, "Shows the auth providers", null));

        Assert.Equal(["login-screen.png"], await Search("?q=sign-in"));
        Assert.Equal(["login-screen.png", "settings.png"], await Search("?q=AUTH"));
        Assert.Equal(["login-screen.png"], await Search("?q=auth%20form"));
        Assert.Equal(["login-screen.png"], await Search("?tag=Auth"));
        Assert.Equal(["about_100.png"], await Search("?q=100"));
        Assert.Empty(await Search("?q=%25%25"));
        Assert.Empty(await Search("?q=_x_"));
    }

    [Fact]
    public async Task FiltersByFolderTypeDateAndUploader()
    {
        var guides = await CreateFolder("Guides");
        var setup = await CreateFolder("Setup", guides.Id);
        await Upload("top.png", guides.Id);
        await Upload("deep.png", setup.Id);
        await Upload("loose.png");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(["top.png"], await Search($"?folder={guides.Id}"));
        Assert.Equal(["deep.png", "top.png"], await Search($"?folder={guides.Id}&subfolders=true"));
        Assert.Equal(["loose.png"], await Search("?folder=none"));
        Assert.Equal(3, (await Search($"?type=png&from={today}&to={today}&uploader=admin")).Count);
        Assert.Empty(await Search("?type=svg"));
        Assert.Empty(await Search("?from=2999-01-01"));
        Assert.Empty(await Search("?uploader=nobody"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/projects/default/assets?type=exe", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/projects/default/assets?from=14.11.2026", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/v1/projects/default/assets?folder=999", Cancel)).StatusCode);
    }

    [Fact]
    public async Task FindsAnAssetByItsLink()
    {
        var asset = await Upload("diagram.png");
        var id = asset.Id;

        foreach (var link in new[]
                 {
                     asset.Url, $"{asset.Url}?w=800", $"{asset.Url}#only-dark", $"http://img.test/i/{id}", id,
                     $"img.test/i/{id}/old-name.png", $"  {asset.Url}  ",
                 })
        {
            var found = await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/lookup?url={Uri.EscapeDataString(link)}", Json, Cancel);
            Assert.Equal((id, "default"), (found!.Id, found.Project));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/lookup?url=https%3A%2F%2Fexample.com%2Fpicture.png", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/v1/lookup?url=AAAAAAAAAA", Cancel)).StatusCode);
    }

    [Fact]
    public async Task ViewersSeeButDoNotOrganize()
    {
        var users = _server.Services.GetRequiredService<UserStore>();
        var projects = _server.Services.GetRequiredService<ProjectStore>();
        var viewer = users.Create("vic", ViewerHash, isAdmin: false);
        projects.SetRole(projects.Find("default")!.Id, viewer.Id, ProjectRole.Viewer);
        users.Create("sam", ViewerHash, isAdmin: false);
        var folder = await CreateFolder("Guides");
        var asset = await Upload("a.png");
        var vic = _server.Ui("vic", "viewer-password");
        var sam = _server.Ui("sam", "viewer-password");

        Assert.Equal(HttpStatusCode.OK, (await vic.GetAsync("/api/v1/projects/default/folders", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await vic.GetAsync($"/api/v1/lookup?url={asset.Id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await vic.PostAsJsonAsync("/api/v1/projects/default/folders", new NewFolder("X", null), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await vic.DeleteAsync($"/api/v1/projects/default/folders/{folder.Id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await vic.PostAsJsonAsync("/api/v1/projects/default/assets/move", new AssetMove([asset.Id], folder.Id), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sam.GetAsync($"/api/v1/lookup?url={asset.Id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sam.GetAsync("/api/v1/projects/default/facets", Cancel)).StatusCode);
    }
    [Fact]
    public async Task IgnoresCaseForEveryAlphabet()
    {
        var a = await Upload("a.png");
        var b = await Upload("b.png");
        await Patch(a.Id, new AssetChanges(null, "Änderungen im Dialog", null, ["Übersicht"]));
        await Patch(b.Id, new AssetChanges(null, null, null, ["übersicht"]));

        Assert.Equal(["a.png"], await Search("?q=%C3%A4nderungen"));
        Assert.Equal(["a.png", "b.png"], await Search("?tag=%C3%9CBERSICHT"));
        Assert.Equal(["a.png", "b.png"], await Search("?q=%C3%BCbersicht"));
        var facets = await _admin.GetFromJsonAsync<FacetsResponse>("/api/v1/projects/default/facets", Json, Cancel);
        Assert.Equal(2, Assert.Single(facets!.Tags).Count);
        var duplicate = await (await Patch(a.Id, new AssetChanges(null, null, null, ["Ärger", "ärger"]))).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        Assert.Equal(["Ärger"], duplicate!.Tags);
    }

    [Fact]
    public async Task FiltersByExactTimes()
    {
        await Upload("now.png");
        var now = DateTimeOffset.UtcNow;
        string Time(DateTimeOffset t) => Uri.EscapeDataString(t.ToString("o", System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(["now.png"], await Search($"?fromTime={Time(now.AddMinutes(-5))}&beforeTime={Time(now.AddMinutes(5))}"));
        Assert.Empty(await Search($"?beforeTime={Time(now.AddMinutes(-5))}"));
        Assert.Empty(await Search($"?fromTime={Time(now.AddMinutes(5).ToOffset(TimeSpan.FromHours(14)))}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/projects/default/assets?fromTime=yesterday", Cancel)).StatusCode);
    }

    [Fact]
    public async Task ChangesTextAndFolderInOneRequest()
    {
        var folder = await CreateFolder("Screens");
        var asset = await Upload("a.png");

        var moved = await (await Patch(asset.Id, new AssetChanges(null, "Moved", null, null, folder.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        Assert.Equal((folder.Id, "Moved"), (moved!.FolderId, moved.AltText));

        var kept = await (await Patch(asset.Id, new AssetChanges(null, "Kept"))).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        Assert.Equal(folder.Id, kept!.FolderId);

        var unfiled = await (await Patch(asset.Id, new AssetChanges(null, null, null, null, "none"))).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        Assert.Null(unfiled!.FolderId);

        var rejected = await Patch(asset.Id, new AssetChanges(null, "Not saved", null, null, "999"));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.Equal("Kept", (await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{asset.Id}", Json, Cancel))!.AltText);
    }

    [Theory]
    [InlineData("/api/v1/projects/default/assets?folder=guides")]
    [InlineData("/api/v1/projects/default/assets?folder=-1")]
    public async Task ExplainsAFolderThatIsNoId(string path)
    {
        var response = await _admin.GetAsync(path, Cancel);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid folder", problem.GetProperty("title").GetString());
    }
}

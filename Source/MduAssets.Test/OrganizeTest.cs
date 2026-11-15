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

/// <summary>Tags, search and the reverse lookup (ORG-03, ORG-04, LNK-09); more on tags is in TagsTest.</summary>
public sealed class OrganizeTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string ViewerHash = PasswordHasher.Hash("viewer-password");
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new TestServer().KeepUploadedFormats();
    private readonly HttpClient _admin;

    public OrganizeTest()
    {
        _admin = _server.Ui();
    }

    public void Dispose() => _server.Dispose();

    private async Task<AssetResponse> Upload(string name, int width = 4)
    {
        var content = new ByteArrayContent(SampleImages.Png(width, 3));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await _admin.PostAsync($"/api/v1/projects/default/assets?name={name}", content, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    private async Task<List<string>> Search(string query) =>
        (await _admin.GetFromJsonAsync<AssetList>($"/api/v1/projects/default/assets{query}", Json, Cancel))!
            .Items.Select(a => a.FileName).Order(StringComparer.Ordinal).ToList();

    private Task<HttpResponseMessage> Patch(string id, AssetChanges changes) =>
        _admin.PatchAsJsonAsync($"/api/v1/assets/{id}", changes, Json, Cancel);

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
    public async Task FiltersByTypeDateAndUploader()
    {
        await Upload("top.png");
        await Upload("deep.png", 5);
        await Upload("loose.png", 6);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(3, (await Search($"?type=png&from={today}&to={today}&uploader=admin")).Count);
        Assert.Empty(await Search("?type=svg"));
        Assert.Empty(await Search("?from=2999-01-01"));
        Assert.Empty(await Search("?uploader=nobody"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/projects/default/assets?type=exe", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/projects/default/assets?from=14.11.2026", Cancel)).StatusCode);
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
        var asset = await Upload("a.png");
        var vic = _server.Ui("vic", "viewer-password");
        var sam = _server.Ui("sam", "viewer-password");

        Assert.Equal(HttpStatusCode.OK, (await vic.GetAsync("/api/v1/projects/default/facets", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await vic.GetAsync($"/api/v1/lookup?url={asset.Id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await vic.PatchAsJsonAsync($"/api/v1/assets/{asset.Id}", new AssetChanges(null, "Changed"), Json, Cancel)).StatusCode);
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
}

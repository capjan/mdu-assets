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

/// <summary>Roles in projects (section 4.2 of the requirements), project management and members.</summary>
public sealed class CollaborationTest : IDisposable
{
    private const string Password = "a-long-password";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new();

    public CollaborationTest()
    {
        var users = _server.Services.GetRequiredService<UserStore>();
        var projects = _server.Services.GetRequiredService<ProjectStore>();
        var docs = projects.Find("default")!.Id;
        foreach (var (name, role) in new[] { ("vera", ProjectRole.Viewer), ("ed", ProjectRole.Editor), ("olga", ProjectRole.Owner) })
            projects.SetRole(docs, users.Create(name, PasswordHasher.Hash(Password), isAdmin: false).Id, role);
        users.Create("stranger", PasswordHasher.Hash(Password), isAdmin: false);
    }

    public void Dispose() => _server.Dispose();

    private HttpClient As(string user) => user == TestServer.AdminName ? _server.Ui() : _server.Ui(user, Password);

    private async Task<HttpResponseMessage> Upload(string user, string project = "default")
    {
        var content = new ByteArrayContent(TestImages.Png(3, 2));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return await As(user).PostAsync($"/api/v1/projects/{project}/assets?name=pic.png", content, Cancel);
    }

    private async Task<AssetResponse> UploadedAsset() =>
        (await (await Upload(TestServer.AdminName)).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;

    [Theory]
    [InlineData("vera", HttpStatusCode.Forbidden)]
    [InlineData("ed", HttpStatusCode.Created)]
    [InlineData("olga", HttpStatusCode.Created)]
    [InlineData("stranger", HttpStatusCode.NotFound)]
    public async Task OnlyEditorsAndOwnersUpload(string user, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await Upload(user)).StatusCode);
    }

    [Theory]
    [InlineData("vera", HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData("ed", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("stranger", HttpStatusCode.NotFound, HttpStatusCode.NotFound)]
    public async Task ViewersReadAndEditorsChangeAssets(string user, HttpStatusCode read, HttpStatusCode change)
    {
        var asset = await UploadedAsset();

        Assert.Equal(read, (await As(user).GetAsync($"/api/v1/assets/{asset.Id}", Cancel)).StatusCode);
        Assert.Equal(read, (await As(user).GetAsync("/api/v1/projects/default/assets", Cancel)).StatusCode);
        Assert.Equal(change, (await As(user).PatchAsJsonAsync($"/api/v1/assets/{asset.Id}", new AssetChanges(null, "New"), Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task ListsOnlyTheCallersProjectsButAllForAdmins()
    {
        var created = await As(TestServer.AdminName).PostAsJsonAsync("/api/v1/projects", new NewProject("handbook", "Handbook", null), Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        // The admin created it and owns it; nobody else is a member yet.

        var forVera = await As("vera").GetFromJsonAsync<ProjectList>("/api/v1/projects", Json, Cancel);
        var forStranger = await As("stranger").GetFromJsonAsync<ProjectList>("/api/v1/projects", Json, Cancel);
        var forAdmin = await As(TestServer.AdminName).GetFromJsonAsync<ProjectList>("/api/v1/projects", Json, Cancel);

        Assert.Equal([("default", "viewer")], forVera!.Items.Select(p => (p.Slug, p.Role)));
        Assert.Empty(forStranger!.Items);
        Assert.Equal(["default", "handbook"], forAdmin!.Items.Select(p => p.Slug).Order());
        Assert.All(forAdmin.Items, p => Assert.Equal("owner", p.Role));
    }

    [Fact]
    public async Task ProjectsAreCreatedByAdminsUnlessUsersAreAllowed()
    {
        var request = new NewProject("team-docs", "Team docs", "Shared screenshots");

        Assert.Equal(HttpStatusCode.Forbidden, (await As("ed").PostAsJsonAsync("/api/v1/projects", request, Json, Cancel)).StatusCode);
        Assert.False((await As("ed").GetFromJsonAsync<MeResponse>("/api/v1/me", Json, Cancel))!.CanCreateProjects);

        var settings = await As(TestServer.AdminName).PatchAsJsonAsync("/api/v1/settings", new SettingsChanges(true), Json, Cancel);
        Assert.True((await settings.Content.ReadFromJsonAsync<SettingsResponse>(Json, Cancel))!.UsersCanCreateProjects);

        var response = await As("ed").PostAsJsonAsync("/api/v1/projects", request, Json, Cancel);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>(Json, Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("team-docs", "Team docs", "Shared screenshots", "owner"), (project!.Slug, project.Name, project.Description, project.Role));
        Assert.Equal(HttpStatusCode.Conflict, (await As("ed").PostAsJsonAsync("/api/v1/projects", request, Json, Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("Team Docs")]
    [InlineData("-docs")]
    [InlineData("docs-")]
    [InlineData("")]
    public async Task RejectsInvalidShortNames(string slug)
    {
        var response = await As(TestServer.AdminName).PostAsJsonAsync("/api/v1/projects", new NewProject(slug, "Docs", null), Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RenamingAProjectKeepsItsLinks()
    {
        var asset = await UploadedAsset();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await As("ed").PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges("Renamed", null), Json, Cancel)).StatusCode);
        var response = await As("olga").PatchAsJsonAsync("/api/v1/projects/default", new ProjectChanges("Product docs", "All screenshots"), Json, Cancel);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>(Json, Cancel);

        Assert.Equal(("default", "Product docs", "All screenshots"), (project!.Slug, project.Name, project.Description));
        var reloaded = await As("olga").GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{asset.Id}", Json, Cancel);
        Assert.Equal(asset.Url, reloaded!.Url);
        Assert.Equal(HttpStatusCode.OK, (await _server.Public().GetAsync(asset.Url, Cancel)).StatusCode);
    }

    [Fact]
    public async Task OwnersManageMembersAndKeepAnOwner()
    {
        Assert.Equal(HttpStatusCode.Forbidden,
            (await As("ed").PutAsJsonAsync("/api/v1/projects/default/members/stranger", new MemberChange("viewer"), Json, Cancel)).StatusCode);

        var added = await As("olga").PutAsJsonAsync("/api/v1/projects/default/members/STRANGER", new MemberChange("editor"), Json, Cancel);
        var members = await added.Content.ReadFromJsonAsync<MemberList>(Json, Cancel);
        Assert.Contains(new MemberResponse("stranger", "editor", false), members!.Items);
        Assert.Equal(HttpStatusCode.Created, (await Upload("stranger")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await As("olga").DeleteAsync("/api/v1/projects/default/members/stranger", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload("stranger")).StatusCode);

        // The admin of the instance is the other owner of the default project; without them, olga is the last one.
        await As("olga").DeleteAsync($"/api/v1/projects/default/members/{TestServer.AdminName}", Cancel);
        Assert.Equal(HttpStatusCode.Conflict,
            (await As("olga").PutAsJsonAsync("/api/v1/projects/default/members/olga", new MemberChange("editor"), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await As("olga").DeleteAsync("/api/v1/projects/default/members/olga", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await As("olga").PutAsJsonAsync("/api/v1/projects/default/members/ed", new MemberChange("king"), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await As("olga").PutAsJsonAsync("/api/v1/projects/default/members/nobody", new MemberChange("viewer"), Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task DeletesOnlyEmptyProjects()
    {
        await As(TestServer.AdminName).PostAsJsonAsync("/api/v1/projects", new NewProject("empty", "Empty", null), Json, Cancel);
        await UploadedAsset();

        Assert.Equal(HttpStatusCode.Forbidden, (await As("ed").DeleteAsync("/api/v1/projects/default", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await As("olga").DeleteAsync("/api/v1/projects/default", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await As(TestServer.AdminName).DeleteAsync("/api/v1/projects/empty", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await As(TestServer.AdminName).GetAsync("/api/v1/projects/empty", Cancel)).StatusCode);
    }
}

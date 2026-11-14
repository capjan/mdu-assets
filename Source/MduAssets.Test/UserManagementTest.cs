using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Security;

namespace MduAssets.Test;

/// <summary>Users managed by admins (AUTH-04), start passwords (AUTH-05) and the admin overview (OPS-04).</summary>
public sealed class UserManagementTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private async Task<StartPasswordResponse> CreateUser(string name, bool isAdmin = false)
    {
        var response = await _server.Ui().PostAsJsonAsync("/api/v1/users", new NewUser(name, isAdmin), Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _server.Ui().GetAsync(response.Headers.Location, Cancel)).StatusCode);
        return (await response.Content.ReadFromJsonAsync<StartPasswordResponse>(Json, Cancel))!;
    }

    private async Task ChangePassword(string name, string current, string next)
    {
        var response = await _server.Ui(name, current).PostAsJsonAsync("/api/v1/me/password", new PasswordChange(current, next), Json, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task NewUsersMustChangeTheirStartPassword()
    {
        var created = await CreateUser("nina");
        Assert.Equal(16, created.StartPassword.Length);
        Assert.True(created.User.MustChangePassword);
        var nina = _server.Ui("nina", created.StartPassword);

        var me = await nina.GetFromJsonAsync<MeResponse>("/api/v1/me", Json, Cancel);
        var blocked = await nina.GetAsync("/api/v1/projects", Cancel);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel);

        Assert.True(me!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(PasswordChangeGate.ProblemType, problem.GetProperty("type").GetString());

        await ChangePassword("nina", created.StartPassword, "ninas-own-password");

        Assert.Equal(HttpStatusCode.Unauthorized, (await nina.GetAsync("/api/v1/me", Cancel)).StatusCode);
        var signedIn = _server.Ui("nina", "ninas-own-password");
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/v1/projects", Cancel)).StatusCode);
        Assert.False((await signedIn.GetFromJsonAsync<MeResponse>("/api/v1/me", Json, Cancel))!.MustChangePassword);
    }

    [Theory]
    [InlineData("wrong-password", "a-new-long-password", "Wrong password")]
    [InlineData(null, "a-new-long-password", "Wrong password")]
    [InlineData(TestServer.AdminPassword, "short", "Password too short")]
    [InlineData(TestServer.AdminPassword, TestServer.AdminPassword, "Same password")]
    public async Task ChecksPasswordChanges(string? current, string? next, string title)
    {
        var response = await _server.Ui().PostAsJsonAsync("/api/v1/me/password", new PasswordChange(current, next), Json, Cancel);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(title, problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("with:colon")]
    [InlineData("")]
    public async Task RejectsInvalidUserNames(string name)
    {
        var response = await _server.Ui().PostAsJsonAsync("/api/v1/users", new NewUser(name, false), Json, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RejectsDuplicateUserNames()
    {
        await CreateUser("nina");

        var response = await _server.Ui().PostAsJsonAsync("/api/v1/users", new NewUser("NINA", false), Json, Cancel);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task OnlyAdminsManageUsers()
    {
        var created = await CreateUser("nina");
        await ChangePassword("nina", created.StartPassword, "ninas-own-password");
        var nina = _server.Ui("nina", "ninas-own-password");

        Assert.Equal(HttpStatusCode.Forbidden, (await nina.GetAsync("/api/v1/users", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nina.PostAsJsonAsync("/api/v1/users", new NewUser("x", true), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nina.GetAsync("/api/v1/admin/overview", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nina.PatchAsJsonAsync("/api/v1/settings", new SettingsChanges(true), Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task LockedUsersCannotSignIn()
    {
        var created = await CreateUser("nina");
        var nina = _server.Ui("nina", created.StartPassword);
        Assert.Equal(HttpStatusCode.OK, (await nina.GetAsync("/api/v1/me", Cancel)).StatusCode);

        await _server.Ui().PatchAsJsonAsync("/api/v1/users/nina", new UserChanges(null, IsLocked: true), Json, Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, (await nina.GetAsync("/api/v1/me", Cancel)).StatusCode);

        await _server.Ui().PatchAsJsonAsync("/api/v1/users/nina", new UserChanges(null, IsLocked: false), Json, Cancel);
        Assert.Equal(HttpStatusCode.OK, (await nina.GetAsync("/api/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task ResettingAPasswordReplacesTheOldOne()
    {
        var created = await CreateUser("nina");
        await ChangePassword("nina", created.StartPassword, "ninas-own-password");

        var reset = await (await _server.Ui().PostAsync("/api/v1/users/nina/password-reset", null, Cancel))
            .Content.ReadFromJsonAsync<StartPasswordResponse>(Json, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _server.Ui("nina", "ninas-own-password").GetAsync("/api/v1/me", Cancel)).StatusCode);
        Assert.True((await _server.Ui("nina", reset!.StartPassword).GetFromJsonAsync<MeResponse>("/api/v1/me", Json, Cancel))!.MustChangePassword);
    }

    [Fact]
    public async Task KeepsTheLastActiveAdmin()
    {
        var admin = _server.Ui();

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PatchAsJsonAsync($"/api/v1/users/{TestServer.AdminName}", new UserChanges(IsAdmin: false, null), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PatchAsJsonAsync($"/api/v1/users/{TestServer.AdminName}", new UserChanges(null, IsLocked: true), Json, Cancel)).StatusCode);

        await CreateUser("second", isAdmin: true);
        var response = await admin.PatchAsJsonAsync($"/api/v1/users/{TestServer.AdminName}", new UserChanges(IsAdmin: false, null), Json, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<UserResponse>(Json, Cancel))!.IsAdmin);
    }

    [Fact]
    public async Task ShowsTheOverview()
    {
        await CreateUser("nina");
        await _server.Ui().PostAsJsonAsync("/api/v1/projects", new NewProject("handbook", "Handbook", null), Json, Cancel);

        var overview = await _server.Ui().GetFromJsonAsync<OverviewResponse>("/api/v1/admin/overview", Json, Cancel);

        Assert.Equal((2, 1, 0, 2, 0, 0L), (overview!.Users, overview.Admins, overview.LockedUsers, overview.Projects, overview.Assets, overview.StorageUsed));
        Assert.Equal("http://img.test/", overview.Server.PublicBaseUrl);
        Assert.True(Path.IsPathFullyQualified(overview.Server.DataDirectory));
        Assert.False(overview.Settings.UsersCanCreateProjects);
    }
}

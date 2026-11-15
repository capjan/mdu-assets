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

/// <summary>Personal API tokens, the public link check and the OpenAPI description (M8).</summary>
public sealed class ApiForToolsTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new TestServer().KeepUploadedFormats();
    private readonly HttpClient _admin;

    public ApiForToolsTest()
    {
        _admin = _server.Ui();
    }

    public void Dispose() => _server.Dispose();

    private async Task<TokenCreated> CreateToken(NewToken token, HttpClient? client = null)
    {
        var response = await (client ?? _admin).PostAsJsonAsync("/api/v1/me/tokens", token, Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenCreated>(Json, Cancel))!;
    }

    private HttpClient WithToken(string secret)
    {
        var client = _server.Ui(user: null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private static ByteArrayContent Png(int width = 20)
    {
        var content = new ByteArrayContent(SampleImages.Png(width, 10));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    [Fact]
    public async Task CreatesTokensThatAreShownOnce()
    {
        var created = await CreateToken(new NewToken("ShareX", null, null, null));

        Assert.StartsWith("mdua_", created.Secret);
        Assert.Equal(45, created.Secret.Length);
        Assert.StartsWith(created.Token.Prefix, created.Secret);
        Assert.Equal(("write", 90.0), (created.Token.Scope, Math.Round((created.Token.ExpiresAt!.Value - created.Token.CreatedAt).TotalDays)));

        var list = await _admin.GetFromJsonAsync<TokenList>("/api/v1/me/tokens", Json, Cancel);
        Assert.Equal("ShareX", Assert.Single(list!.Items).Name);
        Assert.DoesNotContain(created.Secret, await _admin.GetStringAsync("/api/v1/me/tokens", Cancel));

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/me/tokens/{created.Token.Id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(created.Secret).GetAsync("/api/v1/projects", Cancel)).StatusCode);
    }

    [Fact]
    public async Task UploadsWithAToken()
    {
        var created = await CreateToken(new NewToken("curl", "never", "default", "write"));
        var client = WithToken(created.Secret);

        var response = await client.PostAsync("/api/v1/projects/default/assets?name=shot.png", Png(), Cancel);
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(TestServer.AdminName, asset!.UploadedBy);
        Assert.Null(created.Token.ExpiresAt);
        Assert.NotNull((await _admin.GetFromJsonAsync<TokenList>("/api/v1/me/tokens", Json, Cancel))!.Items[0].LastUsedAt);
    }

    [Fact]
    public async Task LimitsTokensToTheirScopeAndProject()
    {
        await _admin.PostAsJsonAsync("/api/v1/projects", new NewProject("other", "Other", null), Json, Cancel);
        var read = WithToken((await CreateToken(new NewToken("read", null, null, "read"))).Secret);
        var limited = WithToken((await CreateToken(new NewToken("docs only", null, "default", "write"))).Secret);

        Assert.Equal(HttpStatusCode.OK, (await read.GetAsync("/api/v1/projects/default/assets", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await read.PostAsync("/api/v1/projects/default/assets?name=a.png", Png(), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await read.PostAsJsonAsync("/api/v1/projects", new NewProject("x", "X", null), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await limited.PostAsync("/api/v1/projects/default/assets?name=a.png", Png(), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await limited.PostAsync("/api/v1/projects/other/assets?name=a.png", Png(21), Cancel)).StatusCode);
        Assert.Equal(["default"], (await limited.GetFromJsonAsync<ProjectList>("/api/v1/projects", Json, Cancel))!.Items
            .Where(p => p.Slug == "default").Select(p => p.Slug));
    }

    [Fact]
    public async Task TokensCannotManageAccounts()
    {
        var token = WithToken((await CreateToken(new NewToken("all", null, null, "write"))).Secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync("/api/v1/me/tokens", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await token.PostAsJsonAsync("/api/v1/me/tokens", new NewToken("more", null, null, null), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await token.PostAsJsonAsync("/api/v1/me/password", new PasswordChange(TestServer.AdminPassword, "a-new-long-password"), Json, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await token.GetAsync("/api/v1/users", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await token.GetAsync("/api/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task RejectsExpiredTokensAndLockedUsers()
    {
        var tokens = _server.Services.GetRequiredService<TokenStore>();
        var users = _server.Services.GetRequiredService<UserStore>();
        var nina = users.Create("nina", PasswordHasher.Hash("ninas-password-123"), isAdmin: false);
        var expired = TokenStore.NewSecret();
        tokens.Create(nina.Id, "old", expired, null, "write", DateTimeOffset.UtcNow.AddMinutes(-1));
        var valid = TokenStore.NewSecret();
        tokens.Create(nina.Id, "new", valid, null, "write", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(expired).GetAsync("/api/v1/me", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WithToken(valid).GetAsync("/api/v1/me", Cancel)).StatusCode);
        users.SetLocked(nina.Id, locked: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(valid).GetAsync("/api/v1/me", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken("mdua_" + new string('x', 40)).GetAsync("/api/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task ChecksLinksWithoutSigningIn()
    {
        var live = await (await _admin.PostAsync("/api/v1/projects/default/assets?name=live.png", Png(), Cancel)).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        var trashed = await (await _admin.PostAsync("/api/v1/projects/default/assets?name=old.png", Png(21), Cancel)).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        var gone = await (await _admin.PostAsync("/api/v1/projects/default/assets?name=gone.png", Png(22), Cancel)).Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel);
        await _admin.DeleteAsync($"/api/v1/assets/{trashed!.Id}", Cancel);
        await _admin.DeleteAsync($"/api/v1/assets/{gone!.Id}", Cancel);
        await _admin.DeleteAsync($"/api/v1/projects/default/trash/{gone.Id}", Cancel);

        string[] urls =
        [
            live!.Url + "?w=800#only-dark", live.VersionUrl, live.Url.Replace("/live.png", "/v7/live.png", StringComparison.Ordinal),
            trashed.Url, gone.Url, "http://img.test/i/AAAAAAAAAA/x.png", "https://example.com/picture.png",
        ];
        var response = await _server.Ui(user: null).PostAsJsonAsync("/api/v1/public/check", new CheckRequest(urls), Json, Cancel);
        var check = await response.Content.ReadFromJsonAsync<CheckResponse>(Json, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["ok", "ok", "unknown", "trash", "deleted", "unknown", "invalid"], check!.Results.Select(r => r.Status));
        Assert.Equal(urls, check.Results.Select(r => r.Url));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _server.Ui(user: null).PostAsJsonAsync("/api/v1/public/check", new CheckRequest([]), Json, Cancel)).StatusCode);
    }

    [Fact]
    public async Task LimitsTheLinkCheckPerClient()
    {
        var client = _server.Ui(user: null);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 62; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/v1/public/check", new CheckRequest(["AAAAAAAAAA"]), Json, Cancel)).StatusCode);

        Assert.Equal(60, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task DescribesTheApiWithOpenApi()
    {
        var response = await _server.Ui(user: null).GetAsync("/api/v1/openapi.json", Cancel);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("MDU Asset Server API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/projects/{project}/assets", out _));
        Assert.True(paths.TryGetProperty("/api/v1/public/check", out _));
        Assert.False(paths.TryGetProperty("/i/{id}", out _));
    }
}

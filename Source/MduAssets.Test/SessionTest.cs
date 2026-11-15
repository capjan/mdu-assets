using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MduAssets.Test;

/// <summary>Signing in on the sign-in page with a session cookie, and signing out (AUTH-01, ADR-0021).</summary>
public sealed class SessionTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private async Task<(HttpClient Browser, HttpResponseMessage Response)> SignIn(string name = TestServer.AdminName, string password = TestServer.AdminPassword, bool remember = false)
    {
        var browser = _server.Browser();
        var response = await browser.PostAsJsonAsync("/api/v1/session", new SignInRequest(name, password, remember), Json, Cancel);
        return (browser, response);
    }

    [Fact]
    public async Task SignsInWithACookieThatScriptsCannotRead()
    {
        var (browser, response) = await SignIn();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("mdu-assets-session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TestServer.AdminName, (await browser.GetFromJsonAsync<MeResponse>("/api/v1/me", Json, Cancel))!.Name);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/login", Cancel)).StatusCode);
    }

    [Fact]
    public async Task RemembersTheSessionWhenAsked()
    {
        var (_, response) = await SignIn(remember: true);

        Assert.Contains("expires", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TestServer.AdminName, "wrong-password")]
    [InlineData("nobody", TestServer.AdminPassword)]
    public async Task RefusesWrongCredentialsWithoutAPrompt(string name, string password)
    {
        var (browser, response) = await SignIn(name, password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task SlowsDownGuessing()
    {
        for (var i = 0; i < 10; i++)
            await SignIn(password: $"guess-{i}");

        var (_, response) = await SignIn();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task SignsOut()
    {
        var (browser, _) = await SignIn();

        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync("/api/v1/session", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/", Cancel)).StatusCode);
    }

    [Fact]
    public async Task EndsSessionsOfLockedUsersAndAfterPasswordResets()
    {
        var users = _server.Services.GetRequiredService<UserStore>();
        var nina = users.Create("nina", PasswordHasher.Hash("ninas-password-123"), isAdmin: false);
        var (locked, _) = await SignIn("nina", "ninas-password-123");
        var (reset, _) = await SignIn("nina", "ninas-password-123");

        users.SetLocked(nina.Id, locked: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await locked.GetAsync("/api/v1/me", Cancel)).StatusCode);
        users.SetLocked(nina.Id, locked: false);

        users.SetPassword(nina.Id, PasswordHasher.Hash("another-password-1"), mustChange: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await reset.GetAsync("/api/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task KeepsTheOwnSessionAfterAPasswordChange()
    {
        var users = _server.Services.GetRequiredService<UserStore>();
        users.Create("nina", PasswordHasher.Hash("start-password-1"), isAdmin: false, mustChangePassword: true);
        var (browser, _) = await SignIn("nina", "start-password-1");
        var (other, _) = await SignIn("nina", "start-password-1");

        var me = await browser.GetFromJsonAsync<MeResponse>("/api/v1/me", Json, Cancel);
        var change = await browser.PostAsJsonAsync("/api/v1/me/password", new PasswordChange("start-password-1", "my-own-password-1"), Json, Cancel);

        Assert.True(me!.MustChangePassword);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.False((await browser.GetFromJsonAsync<MeResponse>("/api/v1/me", Json, Cancel))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task RefusesSignInsFromOtherSites()
    {
        var browser = _server.Browser();
        browser.DefaultRequestHeaders.Add("Origin", "https://attacker.example");

        var response = await browser.PostAsJsonAsync("/api/v1/session", new SignInRequest(TestServer.AdminName, TestServer.AdminPassword), Json, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ToolsStillUseBasicCredentials()
    {
        Assert.Equal(HttpStatusCode.OK, (await _server.Ui().GetAsync("/api/v1/me", Cancel)).StatusCode);
    }

    [Fact]
    public async Task IgnoresBasicCredentialsThatBrowsersRemember()
    {
        // A browser that signed in with Basic Auth before keeps sending it; it must sign in on the page instead.
        var browser = _server.Ui();
        browser.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me", Cancel)).StatusCode);
    }
}

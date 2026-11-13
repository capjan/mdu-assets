using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using MduAssets.Imaging.Test;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MduAssets.Test;

public sealed class AuthenticationTest : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private static ByteArrayContent Png()
    {
        var content = new ByteArrayContent(TestImages.Png(4, 3));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client) =>
        client.PostAsync("/api/v1/projects/default/assets", Png(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task AcceptsTheFirstAdmin()
    {
        var response = await Upload(_server.Ui());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(TestServer.AdminName, "wrong-password")]
    [InlineData("nobody", TestServer.AdminPassword)]
    public async Task RejectsMissingOrWrongCredentials(string? user, string? password)
    {
        var response = await Upload(_server.Ui(user, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("Basic realm=", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task IgnoresTheCaseOfUserNames()
    {
        var response = await Upload(_server.Ui("ADMIN"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RejectsLockedUsersImmediately()
    {
        Assert.Equal(HttpStatusCode.Created, (await Upload(_server.Ui())).StatusCode);

        var users = _server.Services.GetRequiredService<UserStore>();
        users.SetLocked(users.Find(TestServer.AdminName)!.Id, locked: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(_server.Ui())).StatusCode);
    }

    [Fact]
    public async Task RejectsPasswordsOverPlainHttpUnlessAllowed()
    {
        using var strict = new TestServer(new() { ["AllowHttp"] = "false" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(strict.Ui())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Upload(strict.Ui(scheme: "https"))).StatusCode);
    }

    [Theory]
    [InlineData("203.0.113.9", StatusCodes.Status201Created)]
    [InlineData("198.51.100.1", StatusCodes.Status401Unauthorized)]
    public async Task TrustsHttpsOnlyFromConfiguredProxies(string remoteAddress, int expected)
    {
        using var strict = new TestServer(new() { ["AllowHttp"] = "false", ["TrustedProxies"] = "203.0.113.0/24" });
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{TestServer.AdminName}:{TestServer.AdminPassword}"));

        var context = await strict.Send(request =>
        {
            request.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            request.Request.Method = "POST";
            request.Request.Scheme = "http";
            request.Request.Host = new HostString("ui.test");
            request.Request.Path = "/api/v1/projects/default/assets";
            request.Request.Headers.Authorization = "Basic " + credentials;
            request.Request.Headers["X-Forwarded-Proto"] = "https";
            request.Request.Headers["X-Forwarded-For"] = "192.0.2.10";
            request.Request.Body = new MemoryStream(TestImages.Png(2, 2));
        });

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Fact]
    public void HashesPasswordsWithPbkdf2()
    {
        var hash = PasswordHasher.Hash("secret password");

        Assert.StartsWith($"pbkdf2-sha256${PasswordHasher.Iterations}$", hash);
        Assert.True(PasswordHasher.Verify("secret password", hash));
        Assert.False(PasswordHasher.Verify("Secret password", hash));
        Assert.NotEqual(hash, PasswordHasher.Hash("secret password"));
        Assert.False(PasswordHasher.Verify("x", "not-a-hash"));
    }
}

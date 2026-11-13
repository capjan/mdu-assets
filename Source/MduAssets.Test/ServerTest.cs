using System.Net;

namespace MduAssets.Test;

public sealed class ServerTest : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task ReportsHealthyOnEveryHost()
    {
        foreach (var client in new[] { _server.Ui(user: null), _server.Public() })
        {
            var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void PreparesTheDataDirectoryOnStart()
    {
        using var client = _server.Ui();

        Assert.True(File.Exists(Path.Combine(_server.DataDirectory, "mdu-assets.db")));
        Assert.True(Directory.Exists(Path.Combine(_server.DataDirectory, "blobs")));
        Assert.True(Directory.Exists(Path.Combine(_server.DataDirectory, "tmp")));
    }

    [Fact]
    public async Task AnswersUnknownPathsWithNotFound()
    {
        using var client = _server.Ui();

        var response = await client.GetAsync("/nothing-here", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}

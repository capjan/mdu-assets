using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MduAssets.Test;

public sealed class ServerTest : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "mdu-assets-test-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public ServerTest()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("DataDirectory", _dataDirectory));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDirectory))
            Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public async Task ReportsHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PreparesTheDataDirectoryOnStart()
    {
        using var client = _factory.CreateClient();

        Assert.True(File.Exists(Path.Combine(_dataDirectory, "mdu-assets.db")));
        Assert.True(Directory.Exists(Path.Combine(_dataDirectory, "blobs")));
        Assert.True(Directory.Exists(Path.Combine(_dataDirectory, "tmp")));
    }

    [Fact]
    public async Task AnswersUnknownPathsWithNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/nothing-here", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

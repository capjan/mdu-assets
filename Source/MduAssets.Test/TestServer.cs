using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MduAssets.Test;

/// <summary>A server with its own temporary data directory, the UI on ui.test and public links on img.test.</summary>
public sealed class TestServer : IDisposable
{
    public const string AdminName = "admin";
    public const string AdminPassword = "correct-horse-battery";

    private readonly WebApplicationFactory<Program> _factory;

    public TestServer(Dictionary<string, string?>? settings = null)
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "mdu-assets-test-" + Guid.NewGuid().ToString("N"));
        var all = new Dictionary<string, string?>
        {
            ["DataDirectory"] = DataDirectory,
            ["UiBaseUrl"] = "http://ui.test",
            ["PublicBaseUrl"] = "http://img.test",
            ["AllowHttp"] = "true",
            ["ADMIN_USER"] = AdminName,
            ["ADMIN_PASSWORD"] = AdminPassword,
        };
        foreach (var (key, value) in settings ?? [])
            all[key] = value;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in all)
                builder.UseSetting(key, value);
        });
    }

    public string DataDirectory { get; }

    /// <summary>Sends a request with full control over the connection, for example its remote address.</summary>
    public Task<Microsoft.AspNetCore.Http.HttpContext> Send(Action<Microsoft.AspNetCore.Http.HttpContext> configure) =>
        _factory.Server.SendAsync(configure, TestContext.Current.CancellationToken);

    public IServiceProvider Services => _factory.Services;

    /// <summary>A client for the web interface and API host, signed in as the first admin unless told otherwise.</summary>
    public HttpClient Ui(string? user = AdminName, string? password = AdminPassword, string scheme = "http")
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"{scheme}://ui.test") });
        if (user is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        return client;
    }

    /// <summary>A client for the public link host, without credentials.</summary>
    public HttpClient Public() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://img.test") });

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataDirectory))
            Directory.Delete(DataDirectory, recursive: true);
    }
}

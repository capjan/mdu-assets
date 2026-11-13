using MduAssets.Security;
using MduAssets.Storage;

namespace MduAssets.Test;

public sealed class StartupTest
{
    [Fact]
    public void RequiresBaseUrlsOutsideDevelopment()
    {
        var error = Assert.Throws<StartupException>(() => ServerSettings.Create(new ServerOptions(), isDevelopment: false));

        Assert.Contains("MDU_ASSETS_UIBASEURL", error.Message);
    }

    [Fact]
    public void DefaultsToLocalPortsInDevelopment()
    {
        var settings = ServerSettings.Create(new ServerOptions(), isDevelopment: true);

        Assert.Equal("127.0.0.1:5180", settings.UiHost);
        Assert.Equal("127.0.0.1:5181", settings.PublicHost);
    }

    [Theory]
    [InlineData("https://assets.example.com", "https://ASSETS.example.com/")]
    [InlineData("http://127.0.0.1:5180", "http://127.0.0.1:5180/images")]
    public void RejectsTheSameHostForUiAndPublicLinks(string ui, string publicLinks)
    {
        var options = new ServerOptions { UiBaseUrl = ui, PublicBaseUrl = publicLinks };

        var error = Assert.Throws<StartupException>(() => ServerSettings.Create(options, isDevelopment: false));

        Assert.Contains("same host", error.Message);
    }

    [Theory]
    [InlineData("assets.example.com")]
    [InlineData("ftp://assets.example.com")]
    [InlineData("https://assets.example.com/?x=1")]
    public void RejectsInvalidBaseUrls(string ui)
    {
        var options = new ServerOptions { UiBaseUrl = ui, PublicBaseUrl = "https://img.example.com" };

        Assert.Throws<StartupException>(() => ServerSettings.Create(options, isDevelopment: false));
    }

    [Fact]
    public void BuildsPublicLinksWithoutDefaultPortsAndWithABasePath()
    {
        var settings = ServerSettings.Create(
            new ServerOptions { UiBaseUrl = "https://assets.example.com", PublicBaseUrl = "https://img.example.com:443/media/" },
            isDevelopment: false);

        Assert.Equal("https://img.example.com/media/i/AbCdE12345/a-b.png", settings.PublicLink("AbCdE12345", "a-b.png"));
        Assert.Equal("img.example.com", settings.PublicHost);
    }

    [Theory]
    [InlineData("10.0.0.5, 172.16.0.0/12, ::1")]
    [InlineData("")]
    public void ParsesTrustedProxies(string proxies)
    {
        var settings = ServerSettings.Create(
            new ServerOptions { UiBaseUrl = "https://a.example", PublicBaseUrl = "https://b.example", TrustedProxies = proxies },
            isDevelopment: false);

        Assert.Equal(proxies.Split(',', StringSplitOptions.RemoveEmptyEntries).Length, settings.TrustedProxies.Count);
    }

    [Fact]
    public void RejectsInvalidTrustedProxies()
    {
        var options = new ServerOptions { UiBaseUrl = "https://a.example", PublicBaseUrl = "https://b.example", TrustedProxies = "proxy.local" };

        Assert.Throws<StartupException>(() => ServerSettings.Create(options, isDevelopment: false));
    }

    [Fact]
    public void CreatesTheFirstAdminOnlyOnce()
    {
        using var database = new TemporaryDatabase();
        var users = new UserStore(database.Database);

        Assert.Equal("admin", FirstAdmin.EnsureExists(users, "admin", "a-long-password"));
        Assert.Null(FirstAdmin.EnsureExists(users, "someone-else", "another-long-password"));

        var admin = users.Find("ADMIN");
        Assert.NotNull(admin);
        Assert.True(admin.IsAdmin);
        Assert.True(PasswordHasher.Verify("a-long-password", admin.PasswordHash));
        Assert.Equal(1, users.Count());
    }

    [Theory]
    [InlineData(null, "a-long-password")]
    [InlineData("admin", null)]
    [InlineData("admin", "short")]
    public void ExplainsAMissingOrWeakFirstAdmin(string? name, string? password)
    {
        using var database = new TemporaryDatabase();

        var error = Assert.Throws<StartupException>(() => FirstAdmin.EnsureExists(new UserStore(database.Database), name, password));

        Assert.Contains("MDU_ASSETS_ADMIN_", error.Message);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly DataDirectory _directory = new(Path.Combine(Path.GetTempPath(), "mdu-assets-test-" + Guid.NewGuid().ToString("N")));

        public TemporaryDatabase()
        {
            _directory.Prepare();
            Database = new Database(_directory);
            MigrationRunner.Apply(Database, Migration.Embedded());
        }

        public Database Database { get; }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_directory.Root, recursive: true);
        }
    }
}

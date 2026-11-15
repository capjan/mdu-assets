using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MduAssets.Api;
using MduAssets.Imaging.Test;
using MduAssets.Statistics;
using MduAssets.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MduAssets.Test;

/// <summary>Request counts and referring pages (STA-01 – STA-06).</summary>
public sealed class StatisticsTest : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TestServer _server = new TestServer().KeepUploadedFormats();
    private readonly HttpClient _admin;

    public StatisticsTest()
    {
        _admin = _server.Ui();
    }

    public void Dispose() => _server.Dispose();

    private async Task<AssetResponse> Upload(string name, int width = 40)
    {
        var content = new ByteArrayContent(SampleImages.Png(width, 20));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var response = await _admin.PostAsync($"/api/v1/projects/default/assets?name={name}", content, Cancel);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(Json, Cancel))!;
    }

    private async Task<HttpResponseMessage> Request(string url, HttpMethod? method = null, string? referer = null, string? agent = null, EntityTagHeaderValue? etag = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        if (referer is not null)
            request.Headers.Referrer = new Uri(referer);
        if (agent is not null)
            request.Headers.TryAddWithoutValidation("User-Agent", agent);
        if (etag is not null)
            request.Headers.IfNoneMatch.Add(etag);
        var response = await _server.Public().SendAsync(request, Cancel);
        await response.Content.ReadAsByteArrayAsync(Cancel);
        return response;
    }

    private async Task<AssetResponse> Reload(string id)
    {
        // The counter writes in batches; the test writes the current batch at once.
        await Task.Delay(50, Cancel);
        _server.Services.GetRequiredService<UsageCounter>().Flush();
        return (await _admin.GetFromJsonAsync<AssetResponse>($"/api/v1/assets/{id}", Json, Cancel))!;
    }

    [Fact]
    public async Task CountsSuccessfulRequestsOnly()
    {
        var asset = await Upload("chart.png");

        var first = await Request(asset.Url);
        await Request(asset.Url + "?w=320");
        await Request(asset.VersionUrl);
        await Request(asset.Url, etag: first.Headers.ETag);
        await Request(asset.Url, HttpMethod.Head);
        await Request(asset.Url, agent: "Mozilla/5.0 (compatible; Googlebot/2.1)");
        await Request(asset.Url, referer: "http://ui.test/?project=default&asset=" + asset.Id);
        await Request(asset.Url.Replace(asset.Id, "AAAAAAAAAA", StringComparison.Ordinal));

        var counted = await Reload(asset.Id);

        Assert.Equal(4, counted.Requests);
        Assert.NotNull(counted.LastRequestAt);
        Assert.Equal(0, (await Reload((await Upload("other.png", 41)).Id)).Requests);
    }

    [Fact]
    public async Task RecordsReferringPagesWithoutQueryOrFragment()
    {
        var asset = await Upload("chart.png");

        await Request(asset.Url, referer: "https://docs.example.com/guide/setup.html?x=1#top");
        await Request(asset.Url, referer: "https://docs.example.com/guide/setup.html");
        await Request(asset.Url, referer: "https://wiki.example.org/");
        await Reload(asset.Id);

        var referrers = await _admin.GetFromJsonAsync<ReferrerList>($"/api/v1/assets/{asset.Id}/referrers", Json, Cancel);

        Assert.Equal([("https://docs.example.com", "/guide/setup.html", 2L), ("https://wiki.example.org", "/", 1L)],
            referrers!.Items.Select(r => (r.Origin, r.Path, r.Count)));
    }

    [Fact]
    public async Task KeepsAtMostOneHundredReferrersPerAsset()
    {
        var store = _server.Services.GetRequiredService<StatsStore>();
        var id = (await Upload("chart.png")).Id;
        var start = DateTimeOffset.UtcNow;

        store.Write([], Enumerable.Range(0, 105).Select(i => new ReferrerBatch(id, "https://site.example", $"/page-{i}", 1, start.AddSeconds(i))).ToList());

        var kept = store.Referrers(id);
        Assert.Equal(100, kept.Count);
        Assert.DoesNotContain(kept, r => r.Path is "/page-0" or "/page-4");
    }

    [Fact]
    public async Task FindsUnusedImages()
    {
        var used = await Upload("used.png");
        await Upload("unused.png", 41);
        await Request(used.Url);
        await Reload(used.Id);

        var never = await _admin.GetFromJsonAsync<AssetList>("/api/v1/projects/default/assets?unused=0", Json, Cancel);
        var notLately = await _admin.GetFromJsonAsync<AssetList>("/api/v1/projects/default/assets?unused=30", Json, Cancel);

        Assert.Equal(["unused.png"], never!.Items.Select(a => a.FileName));
        Assert.Equal(["unused.png"], notLately!.Items.Select(a => a.FileName));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/projects/default/assets?unused=-1", Cancel)).StatusCode);
    }

    [Fact]
    public async Task StatisticsNeedProjectAccess()
    {
        var asset = await Upload("chart.png");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _server.Ui(user: null).GetAsync($"/api/v1/assets/{asset.Id}/referrers", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Public().GetAsync($"/api/v1/assets/{asset.Id}/referrers", Cancel)).StatusCode);
    }
}

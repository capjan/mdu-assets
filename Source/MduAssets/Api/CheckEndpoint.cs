using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

public sealed record CheckRequest(IReadOnlyList<string>? Urls);

/// <summary>The status of one link: <c>ok</c>, <c>trash</c>, <c>deleted</c>, <c>unknown</c>, or <c>invalid</c> when it is no asset link.</summary>
public sealed record CheckResult(string Url, string Status);

public sealed record CheckResponse(IReadOnlyList<CheckResult> Results);

/// <summary>
/// <c>POST /api/v1/public/check</c> (API-06): tells for up to 100 links whether their images exist, are in the trash or are
/// gone, without signing in and without any other detail, so <c>mdu lint</c> can report broken asset links (CLI-03).
/// Rate limited per client address.
/// </summary>
public static class CheckEndpoint
{
    public const int MaxUrls = 100;
    public const string RateLimitPolicy = "public-check";

    public static Results<Ok<CheckResponse>, ProblemHttpResult> Handle(CheckRequest request, AssetStore assets)
    {
        if (request.Urls is not { Count: > 0 and <= MaxUrls } urls)
            return TypedResults.Problem($"Send 1 to {MaxUrls} links in urls.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

        return TypedResults.Ok(new CheckResponse(urls.Select(url =>
            new CheckResult(url, PublicLinkIds.ExtractWithVersion(url) is { } link ? assets.LinkStatus(link.Id, link.Version) : "invalid")).ToList()));
    }
}

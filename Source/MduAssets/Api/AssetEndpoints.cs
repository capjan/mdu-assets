using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Reading and editing assets and projects. Until roles arrive in M3, every signed-in user may do everything.</summary>
public static class AssetEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/me", Me);
        api.MapGet("/projects", Projects);
        api.MapGet("/projects/{project}/assets", List);
        api.MapPost("/projects/{project}/assets", UploadEndpoint.Handle);
        api.MapGet("/assets/{id}", Get);
        api.MapPatch("/assets/{id}", Update);
    }

    private static MeResponse Me(ClaimsPrincipal user) =>
        new(user.Identity?.Name ?? string.Empty, user.IsInRole(BasicAuthenticationHandler.AdminRole));

    private static ProjectList Projects(AssetStore assets) =>
        new(assets.ListProjects().Select(p => new ProjectResponse(p.Slug, p.Name, p.AssetCount, p.TotalSize)).ToList());

    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    /// <summary>Lists a project's assets newest first, at most <c>limit</c> at a time; <c>next</c> is the <c>after</c> value of the following page.</summary>
    private static Results<Ok<AssetList>, ProblemHttpResult> List(string project, int? limit, string? after, AssetStore assets, ServerSettings settings)
    {
        if (assets.FindProject(project) is not { } target)
            return UnknownProject(project);
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
            return TypedResults.Problem($"The limit must be between 1 and {MaxPageSize}.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid limit");
        if (after is not null && (!AssetId.IsValid(after) || !assets.Contains(target.Id, after)))
            return TypedResults.Problem($"There is no asset '{after}' in this project to continue after.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor");

        var page = assets.List(target.Id, size, after);
        var items = page.Take(size).Select(a => AssetResponse.From(a, settings)).ToList();
        return TypedResults.Ok(new AssetList(items, page.Count > size ? items[^1].Id : null));
    }

    private static Results<Ok<AssetResponse>, ProblemHttpResult> Get(string id, AssetStore assets, ServerSettings settings) =>
        AssetId.IsValid(id) && assets.FindCurrent(id) is { } asset
            ? TypedResults.Ok(AssetResponse.From(asset, settings))
            : UnknownAsset(id);

    private static Results<Ok<AssetResponse>, ProblemHttpResult> Update(string id, AssetChanges changes, AssetStore assets, ServerSettings settings)
    {
        if (!AssetId.IsValid(id) || assets.FindCurrent(id) is not { } asset)
            return UnknownAsset(id);

        // The extension always follows the stored type, so a renamed link still says what it is.
        var extension = Path.GetExtension(asset.FileName).TrimStart('.');
        var fileName = changes.FileName is null ? asset.FileName : FileNames.Normalize(changes.FileName, extension);
        var altText = changes.AltText is null ? asset.AltText : changes.AltText.Trim();
        if (altText.Length == 0)
            return TypedResults.Problem("Alt text must not be empty; describe what the image shows.", statusCode: StatusCodes.Status400BadRequest, title: "Missing alt text");

        return TypedResults.Ok(AssetResponse.From(assets.Update(id, fileName, altText)!, settings));
    }

    internal static ProblemHttpResult UnknownProject(string project) =>
        TypedResults.Problem($"There is no project '{project}'.", statusCode: StatusCodes.Status404NotFound, title: "Unknown project");

    private static ProblemHttpResult UnknownAsset(string id) =>
        TypedResults.Problem($"There is no asset '{id}'.", statusCode: StatusCodes.Status404NotFound, title: "Unknown asset");
}

using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Reading, uploading and editing assets. Viewers read, editors and owners change (section 4.2).</summary>
public static class AssetEndpoints
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{project}/assets", List);
        api.MapPost("/projects/{project}/assets", UploadEndpoint.Handle);
        api.MapGet("/assets/{id}", Get);
        api.MapPatch("/assets/{id}", Update);
    }

    /// <summary>Lists a project's assets newest first, at most <c>limit</c> at a time; <c>next</c> is the <c>after</c> value of the following page.</summary>
    private static Results<Ok<AssetList>, ProblemHttpResult> List(
        string project, int? limit, string? after, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        if (!access.TryGet(user, project, ProjectRole.Viewer, out var target, out var problem))
            return problem;
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
            return TypedResults.Problem($"The limit must be between 1 and {MaxPageSize}.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid limit");
        if (after is not null && (!AssetId.IsValid(after) || !assets.Contains(target.Id, after)))
            return TypedResults.Problem($"There is no asset '{after}' in this project to continue after.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor");

        var page = assets.List(target.Id, size, after);
        var items = page.Take(size).Select(a => AssetResponse.From(a, settings)).ToList();
        return TypedResults.Ok(new AssetList(items, page.Count > size ? items[^1].Id : null));
    }

    private static Results<Ok<AssetResponse>, ProblemHttpResult> Get(string id, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        if (!AssetId.IsValid(id) || assets.FindCurrent(id) is not { } asset)
            return ProjectAccess.UnknownAsset(id);
        if (access.CheckAsset(user, asset, ProjectRole.Viewer) is { } problem)
            return problem;
        return TypedResults.Ok(AssetResponse.From(asset, settings));
    }

    private static Results<Ok<AssetResponse>, ProblemHttpResult> Update(
        string id, AssetChanges changes, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        if (!AssetId.IsValid(id) || assets.FindCurrent(id) is not { } asset)
            return ProjectAccess.UnknownAsset(id);
        if (access.CheckAsset(user, asset, ProjectRole.Editor) is { } problem)
            return problem;

        // The extension always follows the stored type, so a renamed link still says what it is.
        var extension = Path.GetExtension(asset.FileName).TrimStart('.');
        var fileName = changes.FileName is null ? asset.FileName : FileNames.Normalize(changes.FileName, extension);
        var altText = changes.AltText is null ? asset.AltText : changes.AltText.Trim();
        if (altText.Length == 0)
            return TypedResults.Problem("Alt text must not be empty; describe what the image shows.", statusCode: StatusCodes.Status400BadRequest, title: "Missing alt text");

        return TypedResults.Ok(AssetResponse.From(assets.Update(id, fileName, altText)!, settings));
    }
}

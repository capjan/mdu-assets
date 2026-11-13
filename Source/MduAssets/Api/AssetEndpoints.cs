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

    private static Results<Ok<AssetList>, ProblemHttpResult> List(string project, AssetStore assets, ServerSettings settings)
    {
        if (assets.FindProject(project) is not { } target)
            return UnknownProject(project);
        return TypedResults.Ok(new AssetList(assets.List(target.Id).Select(a => AssetResponse.From(a, settings)).ToList()));
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

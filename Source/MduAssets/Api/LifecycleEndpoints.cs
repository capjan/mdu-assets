using System.Security.Claims;
using MduAssets.Images;
using MduAssets.Maintenance;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Versions (VER-01 – VER-04) and the trash (DEL-01 – DEL-03) of assets.</summary>
public static class LifecycleEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/assets/{id}/versions", Versions);
        api.MapPost("/assets/{id}/versions", AddVersion);
        api.MapPost("/assets/{id}/versions/{number:int}/restore", RestoreVersion);
        api.MapDelete("/assets/{id}/versions/{number:int}", DeleteVersion);
        api.MapDelete("/assets/{id}", Trash);
        api.MapPost("/assets/{id}/restore", Restore);
        api.MapGet("/projects/{project}/trash", TrashList);
        api.MapDelete("/projects/{project}/trash", EmptyTrash);
        api.MapDelete("/projects/{project}/trash/{id}", Purge);
    }

    /// <summary>Finds an asset the caller may act on with <paramref name="role"/>; trashed assets only when <paramref name="trashed"/> allows.</summary>
    private static (AssetFile? Asset, ProblemHttpResult? Problem) Find(
        string id, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ProjectRole role, bool? trashed = false)
    {
        if (!AssetId.IsValid(id) || assets.FindCurrent(id) is not { } asset)
            return (null, ProjectAccess.UnknownAsset(id));
        if (access.CheckAsset(user, asset, role) is { } problem)
            return (null, problem);
        if (trashed is false && asset.DeletedAt is not null)
            return (null, InTrash(asset));
        if (trashed is true && asset.DeletedAt is null)
            return (null, TypedResults.Problem($"{asset.FileName} is not in the trash.", statusCode: StatusCodes.Status409Conflict, title: "Not in the trash"));
        return (asset, null);
    }

    internal static ProblemHttpResult InTrash(AssetFile asset) =>
        TypedResults.Problem($"{asset.FileName} is in the trash. Restore it first.", statusCode: StatusCodes.Status409Conflict, title: "In the trash");

    private static Results<Ok<VersionList>, ProblemHttpResult> Versions(
        string id, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        var (asset, problem) = Find(id, user, access, assets, ProjectRole.Viewer, trashed: null);
        if (problem is not null)
            return problem;
        return TypedResults.Ok(new VersionList(assets.Versions(asset!.Id).Select(v => new VersionResponse(
            v.Number, settings.PublicLink(asset.Id, asset.FileName, v.Number), v.ContentType, v.Size, v.Width, v.Height,
            v.CreatedBy, v.CreatedAt, v.Number == asset.Version)).ToList()));
    }

    /// <summary>
    /// Replaces the image under the same link (VER-01). The format may change; the name in the link stays as it was and the
    /// answer says so (VER-02). Old versions stay reachable under their version links until they are deleted.
    /// </summary>
    private static async Task<Results<Created<VersionAdded>, ProblemHttpResult>> AddVersion(
        string id, HttpContext context, ProjectAccess access, AssetStore assets, ProjectStore projects, BlobStore blobs,
        ImageJobs jobs, Variants variants, BlobCleanup cleanup, ServerSettings settings, CancellationToken cancellationToken)
    {
        var (asset, problem) = Find(id, context.User, access, assets, ProjectRole.Editor);
        if (problem is not null)
            return problem;

        var (image, receiveProblem) = await UploadReceiver.ReceiveAsync(context, settings, jobs, blobs, cancellationToken);
        if (receiveProblem is not null)
            return receiveProblem;

        var project = projects.FindById(asset!.ProjectId)!;
        var removed = assets.AddVersion(asset.Id, new NewVersion(image!.Blob.Hash, image.Info.ContentType, image.Blob.Size,
            image.Info.Width, image.Info.Height, image.Animated, context.User.Id()), project.MaxVersions);
        cleanup.Collect(removed);

        await variants.ThumbnailAsync(image.Blob.Hash, cancellationToken);
        if (project.ConvertFormats)
            variants.PrepareAvif(image.Blob.Hash, image.Info.ContentType, image.Animated);

        var updated = AssetResponse.From(assets.FindCurrent(asset.Id)!, settings);
        return TypedResults.Created(updated.VersionUrl, new VersionAdded(updated, asset.ContentType != updated.ContentType, asset.ContentType));
    }

    /// <summary>Makes an old version current again by copying it into a new version (VER-03); the history stays complete.</summary>
    private static Results<Ok<AssetResponse>, ProblemHttpResult> RestoreVersion(
        string id, int number, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ProjectStore projects,
        BlobCleanup cleanup, ServerSettings settings)
    {
        var (asset, problem) = Find(id, user, access, assets, ProjectRole.Editor);
        if (problem is not null)
            return problem;
        if (assets.Versions(asset!.Id).FirstOrDefault(v => v.Number == number) is not { } version)
            return UnknownVersion(number);
        if (version.Number == asset.Version)
            return TypedResults.Problem($"Version {number} is the current one.", statusCode: StatusCodes.Status409Conflict, title: "Already current");

        var project = projects.FindById(asset.ProjectId)!;
        cleanup.Collect(assets.AddVersion(asset.Id, new NewVersion(version.BlobSha256, version.ContentType, version.Size, version.Width,
            version.Height, version.Animated, user.Id()), project.MaxVersions));
        return TypedResults.Ok(AssetResponse.From(assets.FindCurrent(asset.Id)!, settings));
    }

    /// <summary>Deletes an old version for good (VER-04, owners); its version link answers 404 afterwards.</summary>
    private static Results<NoContent, ProblemHttpResult> DeleteVersion(
        string id, int number, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, BlobCleanup cleanup)
    {
        var (asset, problem) = Find(id, user, access, assets, ProjectRole.Owner, trashed: null);
        if (problem is not null)
            return problem;
        if (number == asset!.Version)
            return TypedResults.Problem("The current version cannot be deleted; move the image to the trash instead.",
                statusCode: StatusCodes.Status409Conflict, title: "Current version");
        if (assets.DeleteVersion(asset.Id, number) is not { } blob)
            return UnknownVersion(number);
        cleanup.Collect([blob]);
        return TypedResults.NoContent();
    }

    /// <summary>Moves an asset to the trash (DEL-01); its link answers 410 until it is restored.</summary>
    private static Results<Ok<AssetResponse>, ProblemHttpResult> Trash(
        string id, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        var (asset, problem) = Find(id, user, access, assets, ProjectRole.Editor);
        if (problem is not null)
            return problem;
        assets.Trash(asset!.Id, user.Id());
        return TypedResults.Ok(AssetResponse.From(assets.FindCurrent(asset.Id)!, settings));
    }

    /// <summary>Takes an asset out of the trash; the same link works again (DEL-02).</summary>
    private static Results<Ok<AssetResponse>, ProblemHttpResult> Restore(
        string id, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        var (asset, problem) = Find(id, user, access, assets, ProjectRole.Editor, trashed: true);
        if (problem is not null)
            return problem;
        assets.Restore(asset!.Id);
        return TypedResults.Ok(AssetResponse.From(assets.FindCurrent(asset.Id)!, settings));
    }

    private static Results<Ok<TrashList>, ProblemHttpResult> TrashList(
        string project, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        var days = settings.Options.TrashRetentionDays;
        return TypedResults.Ok(new TrashList(assets.Trash(target.Id).Select(t =>
            new TrashItem(AssetResponse.From(t.Asset, settings), t.DeletedAt, t.DeletedBy, t.DeletedAt.AddDays(days))).ToList(), days));
    }

    /// <summary>Empties the trash of a project for good (DEL-03, owners and admins).</summary>
    private static Results<Ok<PurgeResult>, ProblemHttpResult> EmptyTrash(
        string project, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, BlobCleanup cleanup)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        var ids = assets.TrashedBefore(DateTimeOffset.MaxValue, target.Id);
        foreach (var id in ids)
            cleanup.Collect(assets.Purge(id));
        return TypedResults.Ok(new PurgeResult(ids.Count));
    }

    /// <summary>Deletes one asset from the trash for good (DEL-03).</summary>
    private static Results<NoContent, ProblemHttpResult> Purge(
        string project, string id, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, BlobCleanup cleanup)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        var (asset, findProblem) = Find(id, user, access, assets, ProjectRole.Owner, trashed: true);
        if (findProblem is not null)
            return findProblem;
        if (asset!.ProjectId != target.Id)
            return ProjectAccess.UnknownAsset(id);
        cleanup.Collect(assets.Purge(asset.Id));
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult UnknownVersion(int number) =>
        TypedResults.Problem($"There is no version {number}.", statusCode: StatusCodes.Status404NotFound, title: "Unknown version");
}

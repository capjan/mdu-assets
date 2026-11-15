using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Adds and removes tags on several images at once.</summary>
public sealed record TagChange(IReadOnlyList<string>? Ids, IReadOnlyList<string>? Add, IReadOnlyList<string>? Remove);

public sealed record TagChangeResult(int Changed);

/// <summary>Renames the tag <c>From</c> to <c>To</c> in a whole project; an existing <c>To</c> merges both.</summary>
public sealed record TagRename(string? From, string? To);

public sealed record TagResult(int Assets);

/// <summary>
/// Tags as the way to organize a project (ORG-03): tag several images at once, and rename, merge or delete a tag in the
/// whole project. Editors may do all of it; images keep their links.
/// </summary>
public static class TagEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/projects/{project}/assets/tags", Change);
        api.MapPatch("/projects/{project}/tags", Rename);
        api.MapDelete("/projects/{project}/tags", Delete);
    }

    private static Results<Ok<TagChangeResult>, ProblemHttpResult> Change(
        string project, TagChange change, ClaimsPrincipal user, ProjectAccess access, AssetStore assets)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        if (change.Ids is not { Count: > 0 and <= AssetEndpoints.MaxBulkCount } ids || ids.Any(id => !AssetId.IsValid(id)))
            return Invalid("Invalid selection", $"Give 1 to {AssetEndpoints.MaxBulkCount} asset ids.");
        var (add, error) = TagNames.Normalize(change.Add ?? []);
        if (error is not null)
            return Invalid("Invalid tags", error);
        var (remove, removeError) = TagNames.Normalize(change.Remove ?? []);
        if (removeError is not null)
            return Invalid("Invalid tags", removeError);
        if (add!.Count == 0 && remove!.Count == 0)
            return Invalid("Nothing to change", "Give tags to add or to remove.");

        return assets.ChangeTags(target.Id, ids, add, remove!, TagNames.MaxPerAsset) is { } changed
            ? TypedResults.Ok(new TagChangeResult(changed))
            : Invalid("Too many tags", $"An image has at most {TagNames.MaxPerAsset} tags.");
    }

    private static Results<Ok<TagResult>, ProblemHttpResult> Rename(
        string project, TagRename rename, ClaimsPrincipal user, ProjectAccess access, AssetStore assets)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        var (names, error) = TagNames.Normalize([rename.To]);
        if (error is not null)
            return Invalid("Invalid tag", error);
        if (string.IsNullOrWhiteSpace(rename.From) || names is not [var to])
            return Invalid("Invalid tag", "Give the tag to rename (from) and its new name (to).");

        var count = assets.RenameTag(target.Id, rename.From, to);
        return count > 0 ? TypedResults.Ok(new TagResult(count)) : UnknownTag(rename.From);
    }

    private static Results<Ok<TagResult>, ProblemHttpResult> Delete(
        string project, string? name, ClaimsPrincipal user, ProjectAccess access, AssetStore assets)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        if (string.IsNullOrWhiteSpace(name))
            return Invalid("Invalid tag", "Give the tag to delete with ?name=.");
        var count = assets.DeleteTag(target.Id, name);
        return count > 0 ? TypedResults.Ok(new TagResult(count)) : UnknownTag(name);
    }

    private static ProblemHttpResult UnknownTag(string name) =>
        TypedResults.Problem($"No image in this project has the tag '{name.Trim()}'.", statusCode: StatusCodes.Status404NotFound, title: "Unknown tag");

    private static ProblemHttpResult Invalid(string title, string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: title);
}

using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Nested folders of a project (ORG-01). Viewers see them; editors and owners change them.</summary>
public static class FolderEndpoints
{
    public const int MaxNameLength = 100;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{project}/folders", List);
        api.MapPost("/projects/{project}/folders", Create);
        api.MapPatch("/projects/{project}/folders/{id:long}", Rename);
        api.MapPost("/projects/{project}/folders/{id:long}/move", Move);
        api.MapDelete("/projects/{project}/folders/{id:long}", Delete);
    }

    private static Results<Ok<FolderList>, ProblemHttpResult> List(string project, ClaimsPrincipal user, ProjectAccess access, FolderStore folders)
    {
        if (!access.TryGet(user, project, ProjectRole.Viewer, out var target, out var problem))
            return problem;
        return TypedResults.Ok(ListOf(folders, target.Id));
    }

    private static Results<Created<FolderResponse>, ProblemHttpResult> Create(
        string project, NewFolder request, ClaimsPrincipal user, ProjectAccess access, FolderStore folders)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        if (ValidName(request.Name) is not { } name)
            return InvalidName();
        if (request.ParentId is { } parent && folders.Find(target.Id, parent) is null)
            return UnknownFolder(parent);

        try
        {
            var folder = folders.Create(target.Id, request.ParentId, name);
            return TypedResults.Created($"/api/v1/projects/{target.Slug}/folders/{folder.Id}", From(folder));
        }
        catch (FolderConflictException exception)
        {
            return Conflict(exception);
        }
    }

    private static Results<Ok<FolderResponse>, ProblemHttpResult> Rename(
        string project, long id, FolderChanges changes, ClaimsPrincipal user, ProjectAccess access, FolderStore folders)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        if (folders.Find(target.Id, id) is null)
            return UnknownFolder(id);
        if (ValidName(changes.Name) is not { } name)
            return InvalidName();

        try
        {
            return TypedResults.Ok(From(folders.Rename(target.Id, id, name)));
        }
        catch (FolderConflictException exception)
        {
            return Conflict(exception);
        }
    }

    private static Results<Ok<FolderResponse>, ProblemHttpResult> Move(
        string project, long id, FolderMove move, ClaimsPrincipal user, ProjectAccess access, FolderStore folders)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        if (folders.Find(target.Id, id) is null)
            return UnknownFolder(id);
        if (move.ParentId is { } parent && folders.Find(target.Id, parent) is null)
            return UnknownFolder(parent);

        try
        {
            return TypedResults.Ok(From(folders.Move(target.Id, id, move.ParentId)));
        }
        catch (FolderConflictException exception)
        {
            return Conflict(exception);
        }
    }

    /// <summary>Deletes a folder; its images and subfolders move to the parent folder.</summary>
    private static Results<NoContent, ProblemHttpResult> Delete(string project, long id, ClaimsPrincipal user, ProjectAccess access, FolderStore folders)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        if (folders.Find(target.Id, id) is null)
            return UnknownFolder(id);

        try
        {
            folders.Delete(target.Id, id);
            return TypedResults.NoContent();
        }
        catch (FolderConflictException exception)
        {
            return Conflict(exception);
        }
    }

    internal static FolderList ListOf(FolderStore folders, long projectId) => new(folders.List(projectId).Select(From).ToList());

    private static FolderResponse From(Folder folder) => new(folder.Id, folder.ParentId, folder.Name, folder.AssetCount);

    private static string? ValidName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= MaxNameLength ? trimmed : null;
    }

    private static ProblemHttpResult InvalidName() =>
        TypedResults.Problem($"A folder name has 1 to {MaxNameLength} characters.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid folder name");

    /// <summary>A folder given as text: an id of a folder in the project, or <c>none</c> for no folder.</summary>
    internal sealed record FolderParameter(long? Id, bool None, ProblemHttpResult? Problem);

    internal static FolderParameter ParseFolder(string? value, long projectId, FolderStore folders)
    {
        if (value is null)
            return new FolderParameter(null, false, null);
        if (value == "none")
            return new FolderParameter(null, true, null);
        if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
            return new FolderParameter(null, false, TypedResults.Problem(
                $"The folder is a folder id or 'none', not '{value}'.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid folder"));
        return folders.Find(projectId, id) is null
            ? new FolderParameter(null, false, UnknownFolder(id))
            : new FolderParameter(id, false, null);
    }

    internal static ProblemHttpResult UnknownFolder(long id) =>
        TypedResults.Problem($"There is no folder {id} in this project.", statusCode: StatusCodes.Status404NotFound, title: "Unknown folder");

    private static ProblemHttpResult Conflict(FolderConflictException exception) =>
        TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Folder conflict");
}

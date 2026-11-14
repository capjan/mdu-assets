using System.Globalization;
using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Reading, uploading, editing, moving and finding assets. Viewers read, editors and owners change (section 4.2).</summary>
public static class AssetEndpoints
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;
    public const int MaxDescriptionLength = 1000;
    public const int MaxMoveCount = 500;

    private static readonly Dictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png",
        ["jpeg"] = "image/jpeg",
        ["jpg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["webp"] = "image/webp",
        ["avif"] = "image/avif",
        ["bmp"] = "image/bmp",
        ["svg"] = "image/svg+xml",
    };

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{project}/assets", List);
        api.MapPost("/projects/{project}/assets", UploadEndpoint.Handle);
        api.MapPost("/projects/{project}/assets/move", Move);
        api.MapGet("/projects/{project}/facets", Facets);
        api.MapGet("/assets/{id}", Get);
        api.MapPatch("/assets/{id}", Update);
        api.MapGet("/lookup", Lookup);
    }

    /// <summary>
    /// Lists a project's assets newest first, at most <c>limit</c> at a time; <c>next</c> is the <c>after</c> value of the
    /// following page. Filters (ORG-04): <c>q</c> (words in name, alt text, description or tags), <c>folder</c> (an id or
    /// <c>none</c>), <c>subfolders</c>, <c>tag</c>, <c>type</c> (for example <c>png</c>), <c>from</c> and <c>to</c>
    /// (upload dates in UTC, inclusive), <c>fromTime</c> and <c>beforeTime</c> (ISO 8601 times with offset; the web interface
    /// sends these for the days of its own time zone; <c>beforeTime</c> is exclusive), <c>uploader</c>.
    /// </summary>
    private static Results<Ok<AssetList>, ProblemHttpResult> List(
        string project, int? limit, string? after, string? q, string? folder, bool? subfolders, string? tag, string? type,
        string? from, string? to, string? fromTime, string? beforeTime, string? uploader,
        ClaimsPrincipal user, ProjectAccess access, AssetStore assets, FolderStore folders, ServerSettings settings)
    {
        if (!access.TryGet(user, project, ProjectRole.Viewer, out var target, out var problem))
            return problem;
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
            return Invalid("Invalid limit", $"The limit must be between 1 and {MaxPageSize}.");
        if (after is not null && (!AssetId.IsValid(after) || !assets.Contains(target.Id, after)))
            return Invalid("Invalid cursor", $"There is no asset '{after}' in this project to continue after.");

        if (FolderEndpoints.ParseFolder(folder, target.Id, folders) is { Problem: { } folderProblem })
            return folderProblem;
        IReadOnlyList<long>? folderIds = null;
        var unfiled = folder is "none";
        if (!unfiled && folder is not null)
        {
            var folderId = long.Parse(folder, CultureInfo.InvariantCulture);
            folderIds = subfolders == true ? folders.Subtree(folderId) : [folderId];
        }

        string? contentType = null;
        if (type is not null && !TypeNames.TryGetValue(type, out contentType) && !TypeNames.ContainsValue(contentType = type))
            return Invalid("Invalid type", $"The type is one of {string.Join(", ", TypeNames.Keys)}.");
        if (!TryParseDate(from, out var start) || !TryParseDate(to, out var end))
            return Invalid("Invalid date", "Dates are written as yyyy-mm-dd.");
        if (!TryParseTime(fromTime, out var startTime) || !TryParseTime(beforeTime, out var endTime))
            return Invalid("Invalid time", "Times are ISO 8601 with an offset, for example 2026-11-14T00:00:00+01:00.");

        var query = new AssetQuery(q, folderIds, unfiled, tag, contentType, Later(start, startTime), Earlier(end?.AddDays(1), endTime), uploader);
        var page = assets.List(target.Id, size, after, query);
        var items = page.Take(size).Select(a => AssetResponse.From(a, settings)).ToList();
        return TypedResults.Ok(new AssetList(items, page.Count > size ? items[^1].Id : null));
    }

    private static bool TryParseTime(string? value, out DateTimeOffset? time)
    {
        time = null;
        if (string.IsNullOrEmpty(value))
            return true;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        time = parsed;
        return true;
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;

    private static DateTimeOffset? Earlier(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a < b ? a : b;

    private static bool TryParseDate(string? value, out DateTimeOffset? date)
    {
        date = null;
        if (string.IsNullOrEmpty(value))
            return true;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return false;
        date = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return true;
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
        string id, AssetChanges changes, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, FolderStore folders, ServerSettings settings)
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
            return Invalid("Missing alt text", "Alt text must not be empty; describe what the image shows.");
        var description = changes.Description is null ? asset.Description : changes.Description.Trim();
        if (description.Length > MaxDescriptionLength)
            return Invalid("Description too long", $"A description has at most {MaxDescriptionLength} characters.");
        IReadOnlyList<string>? tags = null;
        if (changes.Tags is not null)
        {
            (tags, var error) = TagNames.Normalize(changes.Tags);
            if (error is not null)
                return Invalid("Invalid tags", error);
        }

        var folder = FolderEndpoints.ParseFolder(changes.Folder, asset.ProjectId, folders);
        if (folder.Problem is { } folderProblem)
            return folderProblem;

        return TypedResults.Ok(AssetResponse.From(assets.Update(id, fileName, altText, description, tags, changes.Folder is not null, folder.Id)!, settings));
    }

    /// <summary>Moves assets of the project into a folder or out of all folders (ORG-02); their links stay the same.</summary>
    private static Results<Ok<AssetMoveResult>, ProblemHttpResult> Move(
        string project, AssetMove move, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, FolderStore folders)
    {
        if (!access.TryGet(user, project, ProjectRole.Editor, out var target, out var problem))
            return problem;
        if (move.Ids is not { Count: > 0 and <= MaxMoveCount } ids || ids.Any(id => !AssetId.IsValid(id)))
            return Invalid("Invalid selection", $"Give 1 to {MaxMoveCount} asset ids.");
        if (move.FolderId is { } folderId && folders.Find(target.Id, folderId) is null)
            return FolderEndpoints.UnknownFolder(folderId);

        return TypedResults.Ok(new AssetMoveResult(assets.Move(target.Id, ids, move.FolderId)));
    }

    private static Results<Ok<FacetsResponse>, ProblemHttpResult> Facets(string project, ClaimsPrincipal user, ProjectAccess access, AssetStore assets)
    {
        if (!access.TryGet(user, project, ProjectRole.Viewer, out var target, out var problem))
            return problem;
        var facets = assets.Facets(target.Id);
        return TypedResults.Ok(new FacetsResponse(
            facets.Tags.Select(t => new TagCountResponse(t.Name, t.Count)).ToList(), facets.Uploaders, facets.ContentTypes));
    }

    /// <summary>
    /// Finds the asset behind a public link (LNK-09). Accepts the link as copied from documentation, with or without file
    /// name, size parameter or fragment, and also a bare asset id. Answers 404 when the caller may not see the asset.
    /// </summary>
    private static Results<Ok<AssetResponse>, ProblemHttpResult> Lookup(
        string? url, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, ServerSettings settings)
    {
        if (PublicLinkIds.Extract(url) is not { } id)
            return Invalid("Not an asset link", "Paste a public link such as https://img.example.com/i/k3F9xQa2Bc/name.png or an asset id.");
        if (assets.FindCurrent(id) is not { } asset || access.CheckAsset(user, asset, ProjectRole.Viewer) is not null)
            return ProjectAccess.UnknownAsset(id);
        return TypedResults.Ok(AssetResponse.From(asset, settings));
    }

    private static ProblemHttpResult Invalid(string title, string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: title);
}

/// <summary>Reads the asset id from a public link (LNK-09).</summary>
public static class PublicLinkIds
{
    public static string? Extract(string? input)
    {
        var text = input?.Trim() ?? string.Empty;
        if (AssetId.IsValid(text))
            return text;

        // Links may start without a scheme when copied from Markdown source; only the path matters.
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            if (!Uri.TryCreate("https://host/" + text.TrimStart('/'), UriKind.Absolute, out uri))
                return null;
        }

        // Public links are <base path>/i/<id>[/<name>]; query and fragment (?w=800, #only-dark) do not matter.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < segments.Length; i++)
        {
            if (segments[i] == "i" && AssetId.IsValid(segments[i + 1]))
                return segments[i + 1];
        }

        return null;
    }
}

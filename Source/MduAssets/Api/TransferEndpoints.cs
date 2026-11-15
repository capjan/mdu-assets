using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MduAssets.Imaging;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

public sealed record ExportManifest(int Format, DateTimeOffset ExportedAt, bool AllVersions, ExportProject Project, IReadOnlyList<ExportFolder> Folders, IReadOnlyList<ExportAsset> Assets);

public sealed record ExportProject(string Slug, string Name, string Description, bool ConvertFormats, int? MaxVersions);

public sealed record ExportFolder(long Id, long? ParentId, string Name);

public sealed record ExportAsset(
    string Id, long? FolderId, string FileName, string AltText, string Description, IReadOnlyList<string> Tags,
    string? PairId, string? PairRole, string UploadedBy, DateTimeOffset CreatedAt, int CurrentVersion, IReadOnlyList<ExportVersion> Versions);

public sealed record ExportVersion(int Number, string Sha256, string ContentType, long Size, int? Width, int? Height, bool Animated, string CreatedBy, DateTimeOffset CreatedAt);

/// <summary>An asset that got a new id on import, because its id was already used on this instance (ADR-0018).</summary>
public sealed record IdChange(string OldId, string NewId);

public sealed record ImportResult(ProjectResponse Project, int Assets, IReadOnlyList<IdChange> ChangedIds);

/// <summary>
/// Moves projects between instances (OPS-06): an export is a zip with <c>mdu-assets-export.json</c> and the files under
/// <c>files/&lt;sha256&gt;</c>; an import creates a new project from it. Every imported file is checked and cleaned like an upload.
/// </summary>
public static class TransferEndpoints
{
    public const string ManifestName = "mdu-assets-export.json";
    private const int Format = 1;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{project}/export", Export);
        api.MapPost("/projects/import", Import).RequireRateLimiting(UploadLimits.Policy);
    }

    /// <summary>Exports the images in use with the current version, or all versions with <c>?versions=all</c>; the trash stays out.</summary>
    private static Results<FileStreamHttpResult, ProblemHttpResult> Export(
        string project, string? versions, ClaimsPrincipal user, ProjectAccess access, AssetStore assets, FolderStore folders,
        BlobStore blobs, DataDirectory data)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        var all = versions == "all";

        var exported = new List<ExportAsset>();
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets.List(target.Id, int.MaxValue - 1))
        {
            var kept = assets.Versions(asset.Id).Where(v => all || v.Number == asset.Version).OrderBy(v => v.Number).ToList();
            files.UnionWith(kept.Select(v => v.BlobSha256));
            exported.Add(new ExportAsset(asset.Id, asset.FolderId, asset.FileName, asset.AltText, asset.Description, asset.Tags,
                asset.PairId, asset.PairRole, asset.UploadedBy, asset.CreatedAt, asset.Version,
                kept.Select(v => new ExportVersion(v.Number, v.BlobSha256, v.ContentType, v.Size, v.Width, v.Height, v.Animated, v.CreatedBy, v.CreatedAt)).ToList()));
        }

        var manifest = new ExportManifest(Format, DateTimeOffset.UtcNow, all,
            new ExportProject(target.Slug, target.Name, target.Description, target.ConvertFormats, target.MaxVersions),
            folders.List(target.Id).Select(f => new ExportFolder(f.Id, f.ParentId, f.Name)).ToList(), exported);

        // The zip goes to a temporary file first: the archive is written synchronously, which Kestrel does not allow on the response.
        var path = Path.Combine(data.TempPath, $"export-{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var entry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal).Open())
                JsonSerializer.Serialize(entry, manifest, AppJsonContext.Default.ExportManifest);
            foreach (var hash in files)
                zip.CreateEntryFromFile(blobs.PathOf(hash), $"files/{hash}", CompressionLevel.NoCompression);
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.DeleteOnClose);
        return TypedResults.Stream(stream, "application/zip", $"{target.Slug}{(all ? "-all-versions" : "")}.zip");
    }

    /// <summary>
    /// Creates a project from an export (<c>?slug=</c> chooses its short name, default the exported one). Asset ids are kept where
    /// they are free; the answer lists those that had to change.
    /// </summary>
    private static async Task<Results<Created<ImportResult>, ProblemHttpResult>> Import(
        string? slug, HttpContext context, ProjectStore projects, AssetStore assets, FolderStore folders, BlobStore blobs,
        SettingsStore settingsStore, DataDirectory data, ServerSettings settings, CancellationToken cancellationToken)
    {
        var user = context.User;
        if (user.ReadOnlyToken() || user.TokenProject() is not null || (!user.IsAdmin() && !settingsStore.GetFlag(SettingsStore.UsersCanCreateProjects)))
            return Problem(StatusCodes.Status403Forbidden, "Not allowed", "Importing creates a project, which needs the right to create projects.");

        var path = Path.Combine(data.TempPath, $"import-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await context.Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if ((total += read) > settings.Options.MaxImportBytes)
                        return Problem(StatusCodes.Status413PayloadTooLarge, "Export too large", $"Imports may have at most {settings.Options.MaxImportBytes:N0} bytes.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            return await ImportFile(path, slug, user, projects, assets, folders, blobs, settings, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "Not a project export", exception.Message);
        }
        catch (JsonException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, "Not a project export", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<Results<Created<ImportResult>, ProblemHttpResult>> ImportFile(
        string path, string? slug, ClaimsPrincipal user, ProjectStore projects, AssetStore assets, FolderStore folders, BlobStore blobs,
        ServerSettings settings, CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifestEntry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException($"The zip has no {ManifestName}.");
        ExportManifest manifest;
        await using (var stream = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.ExportManifest, cancellationToken)
                ?? throw new InvalidDataException("The manifest is empty.");
        if (manifest.Format != Format)
            return Problem(StatusCodes.Status400BadRequest, "Unknown export format", $"This server reads export format {Format}, not {manifest.Format}.");

        var newSlug = (slug ?? manifest.Project.Slug).Trim();
        if (!ProjectSlugs.IsValid(newSlug))
            return Problem(StatusCodes.Status400BadRequest, "Invalid short name", ProjectSlugs.Rule);
        if (projects.Find(newSlug) is not null)
            return Problem(StatusCodes.Status409Conflict, "Short name taken", $"A project with the short name '{newSlug}' already exists. Choose another one with ?slug=.");

        // First every file is checked and stored, as on upload, so a bad export fails before the project exists.
        var stored = new Dictionary<string, (string Hash, ImageInfo Info, long Size)>(StringComparer.Ordinal);
        foreach (var hash in manifest.Assets.SelectMany(a => a.Versions).Select(v => v.Sha256).Distinct(StringComparer.Ordinal))
        {
            var entry = zip.GetEntry($"files/{hash}") ?? throw new InvalidDataException($"The zip has no files/{hash}.");
            if (entry.Length > settings.Options.MaxUploadBytes)
                return Problem(StatusCodes.Status413PayloadTooLarge, "File too large", $"files/{hash} is larger than the upload limit.");
            byte[] content;
            await using (var stream = entry.Open())
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory, cancellationToken);
                content = memory.ToArray();
            }

            if (Convert.ToHexStringLower(SHA256.HashData(content)) != hash)
                throw new InvalidDataException($"files/{hash} does not match its hash.");
            ImageInfo info;
            if (ImageDetector.DetectRaster(content) is { } raster)
                info = raster;
            else if (SvgSanitizer.Sanitize(content) is { } svg)
                (info, content) = (svg.Info, svg.Content);
            else
                throw new InvalidDataException($"files/{hash} is no supported image.");
            if (info.Pixels > settings.Options.MaxPixels)
                return Problem(StatusCodes.Status422UnprocessableEntity, "Image too large", $"files/{hash} has more pixels than allowed.");
            var blob = await blobs.PutAsync(new MemoryStream(content, writable: false), cancellationToken);
            stored[hash] = (blob.Hash, info, blob.Size);
        }

        var name = string.IsNullOrWhiteSpace(manifest.Project.Name) ? newSlug : manifest.Project.Name.Trim();
        var project = projects.Create(newSlug, name.Length > 100 ? name[..100] : name, manifest.Project.Description ?? string.Empty, user.Id());
        projects.Update(project.Id, project.Name, project.Description, manifest.Project.ConvertFormats, manifest.Project.MaxVersions, null, null);

        // Folders parent first; the export lists them flat with their parent ids.
        var folderIds = new Dictionary<long, long>();
        var pending = manifest.Folders.ToList();
        while (pending.Count > 0)
        {
            var ready = pending.Where(f => f.ParentId is null || folderIds.ContainsKey(f.ParentId.Value)).ToList();
            if (ready.Count == 0)
                throw new InvalidDataException("The folders of the export do not form a tree.");
            foreach (var folder in ready)
                folderIds[folder.Id] = folders.Create(project.Id, folder.ParentId is { } parent ? folderIds[parent] : null, folder.Name).Id;
            pending.RemoveAll(ready.Contains);
        }

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in manifest.Assets)
        {
            var versions = asset.Versions.Select(v =>
            {
                var file = stored[v.Sha256];
                return new ImportedVersion(v.Number, file.Hash, file.Info.ContentType, file.Size, file.Info.Width, file.Info.Height, v.Animated, v.CreatedAt);
            }).ToList();
            var current = versions.Any(v => v.Number == asset.CurrentVersion) ? asset.CurrentVersion : versions.Max(v => v.Number);
            ids[asset.Id] = assets.Import(asset.Id, new ImportedAsset(project.Id,
                asset.FolderId is { } folderId && folderIds.TryGetValue(folderId, out var mapped) ? mapped : null,
                asset.FileName, asset.AltText, asset.Description, asset.Tags, current, versions, user.Id(), asset.CreatedAt));
        }

        foreach (var asset in manifest.Assets.Where(a => a is { PairId: not null, PairRole: "light" }))
            if (ids.TryGetValue(asset.PairId!, out var partner))
                assets.SetPair(ids[asset.Id], "light", partner);

        var changed = ids.Where(pair => pair.Key != pair.Value).Select(pair => new IdChange(pair.Key, pair.Value)).ToList();
        var summary = ProjectResponse.From(projects.List(user.Id(), all: true, project.Id).Single(), user.IsAdmin());
        return TypedResults.Created($"/api/v1/projects/{project.Slug}", new ImportResult(summary, ids.Count, changed));
    }

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(detail, statusCode: status, title: title);
}

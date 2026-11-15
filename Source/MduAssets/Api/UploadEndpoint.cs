using MduAssets.Images;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>
/// <c>POST /api/v1/projects/{project}/assets</c>: uploads one image (API-04, see <see cref="UploadReceiver"/>).
/// <c>?folder=</c> puts it into a folder, <c>?duplicates=reject</c> reports content that is already in the project (UP-09).
/// </summary>
public static class UploadEndpoint
{
    public static async Task<Results<Created<AssetResponse>, Conflict<DuplicateResponse>, ProblemHttpResult>> Handle(
        string project,
        HttpContext context,
        AssetStore assets,
        BlobStore blobs,
        ProjectAccess access,
        FolderStore folders,
        ImageJobs jobs,
        Variants variants,
        ProjectStore projects,
        Maintenance.BlobCleanup cleanup,
        ServerSettings settings,
        CancellationToken cancellationToken)
    {
        if (!access.TryGet(context.User, project, ProjectRole.Editor, out var target, out var problem))
            return problem;

        // ?folder=<id> files the upload into a folder of the project (ORG-01).
        var folderText = context.Request.Query["folder"].ToString();
        var folder = FolderEndpoints.ParseFolder(folderText.Length == 0 ? null : folderText, target.Id, folders);
        if (folder.Problem is { } folderProblem)
            return folderProblem;

        var (image, receiveProblem) = await UploadReceiver.ReceiveAsync(context, settings, jobs, blobs, cancellationToken, target.MaxFileBytes);
        if (receiveProblem is not null)
            return receiveProblem;
        var (blob, info) = (image!.Blob, image.Info);
        if (Quotas.Check(target, projects, blob.Size, blob.Hash, cleanup) is { } quotaProblem)
            return quotaProblem;

        if (context.Request.Query["duplicates"] == "reject" && assets.FindByContent(target.Id, blob.Hash) is { } existingId)
        {
            var existing = AssetResponse.From(assets.FindCurrent(existingId)!, settings);
            return TypedResults.Conflict(new DuplicateResponse("Already uploaded",
                $"This image is already in the project as {existing.FileName}.", existing));
        }

        var fileName = FileNames.Normalize(image.Name, info.Extension);
        var altText = string.IsNullOrWhiteSpace(image.Alt) ? FileNames.SuggestAltText(image.Name) : image.Alt.Trim();
        var asset = assets.Create(new NewAsset(target.Id, folder.Id, fileName, altText, blob.Hash, info.ContentType, blob.Size,
            info.Width, info.Height, context.User.Id(), image.Animated));

        // The preview is ready when the interface shows the upload (DLV-06); the AVIF copy follows in the background.
        await variants.ThumbnailAsync(blob.Hash, cancellationToken);
        if (target.ConvertFormats)
            variants.PrepareAvif(blob.Hash, info.ContentType, image.Animated);
        var response = AssetResponse.From(asset, settings);
        return TypedResults.Created(response.Url, response);
    }
}

using MduAssets.Storage;

namespace MduAssets.Maintenance;

/// <summary>
/// Removes blobs and their variants that no version refers to any more, after versions or trashed assets were deleted for
/// good. Blobs are shared by content, so a blob stays as long as any version of any asset still uses it.
/// </summary>
public sealed class BlobCleanup(AssetStore assets, BlobStore blobs, VariantStore variants, ILogger<BlobCleanup> logger)
{
    public void Collect(IEnumerable<string> blobSha256s)
    {
        foreach (var hash in blobSha256s.Distinct(StringComparer.Ordinal))
        {
            if (assets.IsBlobUsed(hash))
                continue;
            try
            {
                blobs.Delete(hash);
                variants.DeleteAll(hash);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Could not remove the unused blob {Blob}", hash);
            }
        }
    }
}

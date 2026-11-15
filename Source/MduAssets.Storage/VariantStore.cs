namespace MduAssets.Storage;

/// <summary>
/// Derived files of a blob, such as previews, smaller widths and WebP or AVIF copies, under
/// <c>variants/ab/cd/&lt;hash&gt;/&lt;name&gt;</c>. It is a cache: deleting the folder only costs the time to create them again.
/// </summary>
public sealed class VariantStore(DataDirectory dataDirectory)
{

    public string PathOf(string blobSha256, string name) =>
        Path.Combine(dataDirectory.VariantsPath, blobSha256[..2], blobSha256[2..4], blobSha256, name);

    /// <summary>The path of a stored variant, or <c>null</c>.</summary>
    public string? Find(string blobSha256, string name)
    {
        var path = PathOf(blobSha256, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Writes a variant through a temporary file, so readers never see a half-written one.</summary>
    public string Put(string blobSha256, string name, byte[] content)
    {
        var path = PathOf(blobSha256, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = Path.Combine(dataDirectory.TempPath, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(temp, content);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Removes all variants of a blob, when the blob itself goes.</summary>
    public void DeleteAll(string blobSha256)
    {
        var folder = Path.GetDirectoryName(PathOf(blobSha256, "x"))!;
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}

namespace MduAssets.Storage;

/// <summary>
/// Derived files of a blob, such as previews, smaller widths and WebP or AVIF copies, under
/// <c>variants/ab/cd/&lt;hash&gt;/&lt;name&gt;</c>. It is a cache: deleting the folder only costs the time to create them again.
/// </summary>
public sealed class VariantStore(DataDirectory dataDirectory)
{
    private const string LargerSuffix = ".larger";

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

    /// <summary>Remembers that a converted variant would be larger than its source, so it is not tried again.</summary>
    public void MarkLarger(string blobSha256, string name)
    {
        var path = PathOf(blobSha256, name) + LargerSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
    }

    public bool IsLarger(string blobSha256, string name) => File.Exists(PathOf(blobSha256, name) + LargerSuffix);
}

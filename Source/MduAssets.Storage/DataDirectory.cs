namespace MduAssets.Storage;

/// <summary>
/// The folder that holds all state of an instance: the database, the blobs, cached variants and uploads in progress.
/// Everything else is configuration.
/// </summary>
public sealed class DataDirectory
{
    public DataDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The data directory must not be empty.", nameof(path));

        Root = Path.GetFullPath(path);
    }

    public string Root { get; }

    public string DatabasePath => Path.Combine(Root, "mdu-assets.db");

    public string BlobsPath => Path.Combine(Root, "blobs");

    /// <summary>Uploads in progress. On the same file system as <see cref="BlobsPath"/>, so finished files can be renamed atomically.</summary>
    public string TempPath => Path.Combine(Root, "tmp");

    /// <summary>Creates the folders and removes leftovers of interrupted uploads.</summary>
    public void Prepare()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BlobsPath);
        Directory.CreateDirectory(TempPath);

        foreach (var file in Directory.EnumerateFiles(TempPath))
            File.Delete(file);
    }
}

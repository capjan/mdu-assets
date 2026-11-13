using System.Security.Cryptography;

namespace MduAssets.Storage;

/// <summary>
/// Stores file content once per SHA-256 hash under <c>blobs/ab/cd/&lt;hash&gt;</c>.
/// Blobs are written once and never changed; the layout must stay stable because versions refer to blobs by hash.
/// </summary>
public sealed class BlobStore
{
    private readonly DataDirectory _dataDirectory;

    public BlobStore(DataDirectory dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Writes the content to a temporary file, hashes it on the way and moves it into place atomically.
    /// Storing content that already exists keeps the existing blob.
    /// </summary>
    /// <returns>The SHA-256 hash in lowercase hex and the size in bytes.</returns>
    public async Task<StoredBlob> PutAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(_dataDirectory.TempPath, Guid.NewGuid().ToString("N"));
        try
        {
            string hash;
            long size;
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
                size = file.Length;
                hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            var path = PathOf(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                File.Move(tempPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Same content stored before or at the same time: the existing blob is identical.
            }

            return new StoredBlob(hash, size);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    public bool Exists(string hash) => File.Exists(PathOf(hash));

    /// <summary>Opens a blob for reading. Throws <see cref="FileNotFoundException"/> when it does not exist.</summary>
    public FileStream OpenRead(string hash) =>
        new(PathOf(hash), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);

    /// <summary>Deletes a blob. Callers make sure no version refers to it any more.</summary>
    public void Delete(string hash) => File.Delete(PathOf(hash));

    /// <summary>The file path of a blob: <c>blobs/ab/cd/abcd…</c>.</summary>
    public string PathOf(string hash)
    {
        if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigitLower))
            throw new ArgumentException("A blob hash is 64 lowercase hex characters.", nameof(hash));

        return Path.Combine(_dataDirectory.BlobsPath, hash[..2], hash[2..4], hash);
    }
}

public readonly record struct StoredBlob(string Hash, long Size);

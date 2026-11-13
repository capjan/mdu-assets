using MduAssets.Storage;

namespace MduAssets.Storage.Test;

/// <summary>A prepared data directory in the system temp folder, deleted after the test.</summary>
internal sealed class TempDataDirectory : IDisposable
{
    public TempDataDirectory()
    {
        Directory = new DataDirectory(Path.Combine(Path.GetTempPath(), "mdu-assets-test-" + Guid.NewGuid().ToString("N")));
        Directory.Prepare();
    }

    public DataDirectory Directory { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        System.IO.Directory.Delete(Directory.Root, recursive: true);
    }
}

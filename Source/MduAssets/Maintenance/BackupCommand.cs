using MduAssets.Storage;
using Microsoft.Data.Sqlite;

namespace MduAssets.Maintenance;

/// <summary>
/// <c>mdu-assets backup &lt;target&gt;</c> (NF-06): writes a consistent copy of an instance to a folder, also while the server runs.
/// The database is copied with SQLite's online backup, which sees one moment in time; then every blob that copy refers to is
/// copied. Blobs are written before the database refers to them and never change, so the copy is complete. Variants, the
/// temporary folder and the statistics batch in memory are left out. Where the copy goes next is the operator's choice.
/// </summary>
public static class BackupCommand
{
    public static int Run(string dataDirectory, string target)
    {
        var source = new DataDirectory(dataDirectory);
        if (!File.Exists(source.DatabasePath))
        {
            Console.Error.WriteLine($"mdu-assets backup: there is no database in {source.Root}.");
            return 1;
        }

        var copy = new DataDirectory(target);
        if (Directory.Exists(copy.Root) && Directory.EnumerateFileSystemEntries(copy.Root).Any())
        {
            Console.Error.WriteLine($"mdu-assets backup: {copy.Root} is not empty. Back up into a new or empty folder.");
            return 1;
        }

        Directory.CreateDirectory(copy.Root);
        Directory.CreateDirectory(copy.BlobsPath);

        using (var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy.DatabasePath, Pooling = false }.ToString()))
        {
            from.Open();
            to.Open();
            from.BackupDatabase(to);
            // The copy is one self-contained file; the server switches it back to WAL when it opens it.
            using var single = to.CreateCommand();
            single.CommandText = "PRAGMA journal_mode = DELETE;";
            single.ExecuteNonQuery();
        }

        var blobs = new BlobStore(source);
        var copies = new BlobStore(copy);
        long count = 0, bytes = 0;
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT blob_sha256 FROM asset_versions UNION SELECT original_sha256 FROM asset_versions WHERE original_sha256 IS NOT NULL;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var hash = reader.GetString(0);
                var destination = copies.PathOf(hash);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(blobs.PathOf(hash), destination);
                count++;
                bytes += new FileInfo(destination).Length;
            }
        }

        // The data protection keys belong to the instance as well.
        if (Directory.Exists(source.KeysPath))
        {
            Directory.CreateDirectory(copy.KeysPath);
            foreach (var key in Directory.EnumerateFiles(source.KeysPath))
                File.Copy(key, Path.Combine(copy.KeysPath, Path.GetFileName(key)));
        }

        Console.WriteLine($"Backed up the database and {count:N0} files ({bytes:N0} bytes) to {copy.Root}.");
        return 0;
    }
}

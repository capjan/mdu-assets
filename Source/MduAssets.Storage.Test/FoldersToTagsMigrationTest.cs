using Microsoft.Data.Sqlite;

namespace MduAssets.Storage.Test;

/// <summary>Migration 0011 turns folders into tags (ADR-0020).</summary>
public sealed class FoldersToTagsMigrationTest : IDisposable
{
    private readonly TempDataDirectory _temp = new();
    private readonly Database _database;

    public FoldersToTagsMigrationTest()
    {
        _database = new Database(_temp.Directory);
    }

    public void Dispose() => _temp.Dispose();

    private void Execute(string sql)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private List<(string Asset, string Key, string Name)> Tags()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT asset_id, key, name FROM tags ORDER BY asset_id, key;";
        using var reader = command.ExecuteReader();
        var tags = new List<(string, string, string)>();
        while (reader.Read())
            tags.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return tags;
    }

    [Fact]
    public void GivesImagesTheirFolderAndTheFoldersAboveAsTags()
    {
        var all = Migration.Embedded();
        MigrationRunner.Apply(_database, all.Where(m => m.Version < 11).ToList());
        Execute("""
            INSERT INTO users (id, name, password_hash, created_at) VALUES (1, 'admin', 'x', '2026-01-01');
            INSERT INTO folders (id, project_id, parent_id, name, created_at) VALUES
                (1, 1, NULL, 'Guides', '2026-01-01'), (2, 1, 1, 'Übersicht', '2026-01-01'), (3, 1, NULL, 'Screens', '2026-01-01');
            INSERT INTO assets (id, project_id, folder_id, file_name, alt_text, current_version, created_by, created_at) VALUES
                ('AAAAAAAAAA', 1, 2, 'deep.png', 'Deep', 1, 1, '2026-01-01'),
                ('BBBBBBBBBB', 1, 3, 'shot.png', 'Shot', 1, 1, '2026-01-01'),
                ('CCCCCCCCCC', 1, NULL, 'loose.png', 'Loose', 1, 1, '2026-01-01');
            -- An existing tag keeps its spelling; the same tag from a folder does not change it.
            INSERT INTO tags (asset_id, key, name) VALUES ('CCCCCCCCCC', 'screens', 'SCREENS'), ('AAAAAAAAAA', 'guides', 'guides');
            """);

        Assert.Equal([11], MigrationRunner.Apply(_database, all));

        Assert.Equal(
            [
                ("AAAAAAAAAA", "guides", "guides"), ("AAAAAAAAAA", "übersicht", "Übersicht"),
                ("BBBBBBBBBB", "screens", "SCREENS"), ("CCCCCCCCCC", "screens", "SCREENS"),
            ],
            Tags());
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT count(*) FROM folders), (SELECT count(*) FROM assets WHERE folder_id IS NOT NULL);";
        using var reader = command.ExecuteReader();
        reader.Read();
        Assert.Equal((0L, 0L), (reader.GetInt64(0), reader.GetInt64(1)));
    }
}

using System.Text;
using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

/// <summary>An asset with its current version and how it is organized.</summary>
public sealed record AssetFile(
    string Id,
    long ProjectId,
    string ProjectSlug,
    long? FolderId,
    string FileName,
    string AltText,
    string Description,
    IReadOnlyList<string> Tags,
    int Version,
    string BlobSha256,
    string ContentType,
    long Size,
    int? Width,
    int? Height,
    string UploadedBy,
    DateTimeOffset CreatedAt);

/// <summary>What public delivery needs to serve an asset, read without joins on the hot path.</summary>
public sealed record DeliveryFile(string BlobSha256, string ContentType, DateTimeOffset CreatedAt);

public sealed record NewAsset(
    long ProjectId,
    long? FolderId,
    string FileName,
    string AltText,
    string BlobSha256,
    string ContentType,
    long Size,
    int? Width,
    int? Height,
    long CreatedBy);

/// <summary>
/// Search and filters for the asset list (ORG-04). <c>FolderIds</c> limits to these folders; <c>Unfiled</c> to assets in no folder.
/// Dates are UTC; <c>Before</c> is exclusive.
/// </summary>
public sealed record AssetQuery(
    string? Text = null,
    IReadOnlyList<long>? FolderIds = null,
    bool Unfiled = false,
    string? Tag = null,
    string? ContentType = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Before = null,
    string? UploadedBy = null);

public sealed record TagCount(string Name, int Count);

/// <summary>The values the filters of a project offer (ORG-03, ORG-04).</summary>
public sealed record AssetFacets(IReadOnlyList<TagCount> Tags, IReadOnlyList<string> Uploaders, IReadOnlyList<string> ContentTypes);

/// <summary>Reads and writes assets.</summary>
public sealed class AssetStore(Database database)
{
    private const char TagSeparator = '\u001f';

    /// <summary>
    /// Creates an asset with version 1. A new random id is drawn until it is free; ids are never reused,
    /// because deleted assets keep their row.
    /// </summary>
    public AssetFile Create(NewAsset asset)
    {
        using var connection = database.Open();
        for (var attempt = 0; ; attempt++)
        {
            var id = AssetId.New();
            var now = Clock.Now();
            using var transaction = connection.BeginTransaction();
            try
            {
                using (var insertAsset = connection.CreateCommand())
                {
                    insertAsset.Transaction = transaction;
                    insertAsset.CommandText = """
                        INSERT INTO assets (id, project_id, folder_id, file_name, alt_text, current_version, created_by, created_at)
                        VALUES ($id, $project, $folder, $fileName, $alt, 1, $user, $now);
                        """;
                    insertAsset.Parameters.AddWithValue("$id", id);
                    insertAsset.Parameters.AddWithValue("$project", asset.ProjectId);
                    insertAsset.Parameters.AddWithValue("$folder", (object?)asset.FolderId ?? DBNull.Value);
                    insertAsset.Parameters.AddWithValue("$fileName", asset.FileName);
                    insertAsset.Parameters.AddWithValue("$alt", asset.AltText);
                    insertAsset.Parameters.AddWithValue("$user", asset.CreatedBy);
                    insertAsset.Parameters.AddWithValue("$now", now);
                    insertAsset.ExecuteNonQuery();
                }

                using (var insertVersion = connection.CreateCommand())
                {
                    insertVersion.Transaction = transaction;
                    insertVersion.CommandText = """
                        INSERT INTO asset_versions (asset_id, number, blob_sha256, content_type, size, width, height, created_by, created_at)
                        VALUES ($id, 1, $blob, $type, $size, $width, $height, $user, $now);
                        """;
                    insertVersion.Parameters.AddWithValue("$id", id);
                    insertVersion.Parameters.AddWithValue("$blob", asset.BlobSha256);
                    insertVersion.Parameters.AddWithValue("$type", asset.ContentType);
                    insertVersion.Parameters.AddWithValue("$size", asset.Size);
                    insertVersion.Parameters.AddWithValue("$width", (object?)asset.Width ?? DBNull.Value);
                    insertVersion.Parameters.AddWithValue("$height", (object?)asset.Height ?? DBNull.Value);
                    insertVersion.Parameters.AddWithValue("$user", asset.CreatedBy);
                    insertVersion.Parameters.AddWithValue("$now", now);
                    insertVersion.ExecuteNonQuery();
                }

                transaction.Commit();
                return FindCurrent(id)!;
            }
            catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 1555 && attempt < 5) // SQLITE_CONSTRAINT_PRIMARYKEY
            {
                transaction.Rollback();
            }
        }
    }

    /// <summary>
    /// One page of a project's assets that match <paramref name="query"/>, newest first. <paramref name="after"/> is the id of the
    /// last asset of the previous page; the result has one asset more than <paramref name="limit"/> when another page follows.
    /// </summary>
    public IReadOnlyList<AssetFile> List(long projectId, int limit, string? after = null, AssetQuery? query = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        var sql = new StringBuilder(SelectCurrent).Append(" WHERE a.project_id = $project");
        command.Parameters.AddWithValue("$project", projectId);
        if (after is not null)
        {
            sql.Append(" AND (a.created_at, a.id) < (SELECT created_at, id FROM assets WHERE id = $after)");
            command.Parameters.AddWithValue("$after", after);
        }

        if (query is not null)
            AppendFilters(sql, command, query);

        sql.Append(" ORDER BY a.created_at DESC, a.id DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", limit + 1);
        command.CommandText = sql.ToString();
        using var reader = command.ExecuteReader();
        var assets = new List<AssetFile>();
        while (reader.Read())
            assets.Add(ReadAsset(reader));
        return assets;
    }

    private static void AppendFilters(StringBuilder sql, SqliteCommand command, AssetQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            // Every word must appear in the name, alt text, description or a tag.
            var words = query.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < words.Length; i++)
            {
                var parameter = $"$word{i}";
                sql.Append($"""
                     AND (a.file_name LIKE {parameter} ESCAPE '\' OR a.alt_text LIKE {parameter} ESCAPE '\'
                          OR a.description LIKE {parameter} ESCAPE '\'
                          OR EXISTS (SELECT 1 FROM tags t WHERE t.asset_id = a.id AND t.name LIKE {parameter} ESCAPE '\'))
                    """);
                command.Parameters.AddWithValue(parameter, "%" + EscapeLike(words[i]) + "%");
            }
        }

        if (query.Unfiled)
            sql.Append(" AND a.folder_id IS NULL");
        else if (query.FolderIds is { Count: > 0 } folders)
        {
            var names = folders.Select((_, i) => $"$folder{i}").ToList();
            sql.Append(" AND a.folder_id IN (").Append(string.Join(", ", names)).Append(')');
            for (var i = 0; i < folders.Count; i++)
                command.Parameters.AddWithValue(names[i], folders[i]);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            sql.Append(" AND EXISTS (SELECT 1 FROM tags t WHERE t.asset_id = a.id AND t.name = $tag)");
            command.Parameters.AddWithValue("$tag", query.Tag.Trim());
        }

        if (query.ContentType is not null)
        {
            sql.Append(" AND v.content_type = $type");
            command.Parameters.AddWithValue("$type", query.ContentType);
        }

        if (query.From is { } from)
        {
            sql.Append(" AND a.created_at >= $from");
            command.Parameters.AddWithValue("$from", Clock.Format(from));
        }

        if (query.Before is { } before)
        {
            sql.Append(" AND a.created_at < $before");
            command.Parameters.AddWithValue("$before", Clock.Format(before));
        }

        if (!string.IsNullOrWhiteSpace(query.UploadedBy))
        {
            sql.Append(" AND u.name = $uploader");
            command.Parameters.AddWithValue("$uploader", query.UploadedBy.Trim());
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>Whether an asset belongs to the project.</summary>
    public bool Contains(long projectId, string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM assets WHERE id = $id AND project_id = $project;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$project", projectId);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Finds the current version of an asset, or <c>null</c> for an unknown id.</summary>
    public AssetFile? FindCurrent(string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectCurrent + " WHERE a.id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAsset(reader) : null;
    }

    /// <summary>Finds what public delivery needs for an asset, or <c>null</c> for an unknown id.</summary>
    public DeliveryFile? FindForDelivery(string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.blob_sha256, v.content_type, v.created_at
            FROM assets a JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
            WHERE a.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new DeliveryFile(reader.GetString(0), reader.GetString(1), Clock.Parse(reader.GetString(2))) : null;
    }

    /// <summary>
    /// Changes the descriptive fields. <paramref name="tags"/> replaces all tags when given. The public link stays valid,
    /// because only the id is used for lookups.
    /// </summary>
    /// <returns>The updated asset, or <c>null</c> for an unknown id.</returns>
    public AssetFile? Update(string id, string fileName, string altText, string description, IReadOnlyCollection<string>? tags)
    {
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE assets SET file_name = $fileName, alt_text = $alt, description = $description WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$fileName", fileName);
                command.Parameters.AddWithValue("$alt", altText);
                command.Parameters.AddWithValue("$description", description);
                if (command.ExecuteNonQuery() == 0)
                    return null;
            }

            if (tags is not null)
            {
                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM tags WHERE asset_id = $id;";
                    delete.Parameters.AddWithValue("$id", id);
                    delete.ExecuteNonQuery();
                }

                foreach (var tag in tags)
                {
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT OR IGNORE INTO tags (asset_id, name) VALUES ($id, $name);";
                    insert.Parameters.AddWithValue("$id", id);
                    insert.Parameters.AddWithValue("$name", tag);
                    insert.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        return FindCurrent(id);
    }

    /// <summary>
    /// Moves assets of a project into a folder of the same project, or out of all folders for <c>null</c> (ORG-02).
    /// Assets of other projects are left alone. Returns the number of moved assets.
    /// </summary>
    public int Move(long projectId, IReadOnlyCollection<string> ids, long? folderId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var moved = 0;
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE assets SET folder_id = $folder WHERE id = $id AND project_id = $project;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$folder", (object?)folderId ?? DBNull.Value);
            moved += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return moved;
    }

    /// <summary>Tags with their use, uploaders and file types of a project, for suggestions and filters.</summary>
    public AssetFacets Facets(long projectId)
    {
        using var connection = database.Open();

        List<T> Query<T>(string sql, Func<SqliteDataReader, T> read)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$project", projectId);
            using var reader = command.ExecuteReader();
            var items = new List<T>();
            while (reader.Read())
                items.Add(read(reader));
            return items;
        }

        return new AssetFacets(
            Query("""
                SELECT t.name, count(*) FROM tags t JOIN assets a ON a.id = t.asset_id
                WHERE a.project_id = $project GROUP BY t.name ORDER BY count(*) DESC, t.name;
                """, r => new TagCount(r.GetString(0), r.GetInt32(1))),
            Query("""
                SELECT DISTINCT u.name FROM assets a JOIN users u ON u.id = a.created_by
                WHERE a.project_id = $project ORDER BY u.name COLLATE NOCASE;
                """, r => r.GetString(0)),
            Query("""
                SELECT DISTINCT v.content_type FROM assets a JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
                WHERE a.project_id = $project ORDER BY v.content_type;
                """, r => r.GetString(0)));
    }

    /// <summary>Bytes on disk for all versions; versions with the same content share one blob and count once.</summary>
    public long StorageUsed()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT coalesce(sum(size), 0) FROM (SELECT DISTINCT blob_sha256, size FROM asset_versions);";
        return (long)command.ExecuteScalar()!;
    }

    private const string SelectCurrent = """
        SELECT a.id, a.project_id, p.slug, a.folder_id, a.file_name, a.alt_text, a.description,
               (SELECT group_concat(t.name, char(31)) FROM (SELECT name FROM tags WHERE asset_id = a.id ORDER BY name) t),
               v.number, v.blob_sha256, v.content_type, v.size, v.width, v.height, u.name, v.created_at
        FROM assets a
        JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
        JOIN projects p ON p.id = a.project_id
        JOIN users u ON u.id = a.created_by
        """;

    private static AssetFile ReadAsset(SqliteDataReader reader) =>
        new(
            reader.GetString(0), reader.GetInt64(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? [] : reader.GetString(7).Split(TagSeparator),
            reader.GetInt32(8), reader.GetString(9), reader.GetString(10), reader.GetInt64(11),
            reader.IsDBNull(12) ? null : reader.GetInt32(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.GetString(14),
            Clock.Parse(reader.GetString(15)));
}

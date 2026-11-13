using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

public sealed record Project(long Id, string Slug, string Name);

/// <summary>The current version of an asset, as needed to serve it.</summary>
public sealed record AssetFile(
    string Id,
    string FileName,
    string AltText,
    int Version,
    string BlobSha256,
    string ContentType,
    long Size,
    int? Width,
    int? Height,
    DateTimeOffset CreatedAt);

public sealed record NewAsset(
    long ProjectId,
    string FileName,
    string AltText,
    string BlobSha256,
    string ContentType,
    long Size,
    int? Width,
    int? Height,
    long CreatedBy);

/// <summary>Reads and writes projects and assets.</summary>
public sealed class AssetStore(Database database)
{
    public Project? FindProject(string slug)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, slug, name FROM projects WHERE slug = $slug;";
        command.Parameters.AddWithValue("$slug", slug);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new Project(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)) : null;
    }

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
                        INSERT INTO assets (id, project_id, file_name, alt_text, current_version, created_by, created_at)
                        VALUES ($id, $project, $fileName, $alt, 1, $user, $now);
                        """;
                    insertAsset.Parameters.AddWithValue("$id", id);
                    insertAsset.Parameters.AddWithValue("$project", asset.ProjectId);
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
                return new AssetFile(id, asset.FileName, asset.AltText, 1, asset.BlobSha256, asset.ContentType,
                    asset.Size, asset.Width, asset.Height, Clock.Parse(now));
            }
            catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 1555 && attempt < 5) // SQLITE_CONSTRAINT_PRIMARYKEY
            {
                transaction.Rollback();
            }
        }
    }

    /// <summary>All projects with the number and total size of their assets, ordered by name.</summary>
    public IReadOnlyList<ProjectSummary> ListProjects()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.slug, p.name, count(a.id), coalesce(sum(v.size), 0)
            FROM projects p
            LEFT JOIN assets a ON a.project_id = p.id
            LEFT JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
            GROUP BY p.id
            ORDER BY p.name COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var projects = new List<ProjectSummary>();
        while (reader.Read())
            projects.Add(new ProjectSummary(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3)));
        return projects;
    }

    /// <summary>
    /// One page of the current versions of a project's assets, newest first. <paramref name="after"/> is the id of the
    /// last asset of the previous page; the result has one asset more than <paramref name="limit"/> when another page follows.
    /// </summary>
    public IReadOnlyList<AssetFile> List(long projectId, int limit, string? after = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectCurrent + " WHERE a.project_id = $project" +
            (after is null ? "" : " AND (a.created_at, a.id) < (SELECT created_at, id FROM assets WHERE id = $after)") +
            " ORDER BY a.created_at DESC, a.id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$limit", limit + 1);
        if (after is not null)
            command.Parameters.AddWithValue("$after", after);
        using var reader = command.ExecuteReader();
        var assets = new List<AssetFile>();
        while (reader.Read())
            assets.Add(ReadAsset(reader));
        return assets;
    }

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

    /// <summary>Changes the file name and alt text. The public link stays valid, because only the id is used for lookups.</summary>
    /// <returns>The updated asset, or <c>null</c> for an unknown id.</returns>
    public AssetFile? Update(string id, string fileName, string altText)
    {
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE assets SET file_name = $fileName, alt_text = $alt WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$fileName", fileName);
            command.Parameters.AddWithValue("$alt", altText);
            if (command.ExecuteNonQuery() == 0)
                return null;
        }

        return FindCurrent(id);
    }

    private const string SelectCurrent = """
        SELECT a.id, a.file_name, a.alt_text, v.number, v.blob_sha256, v.content_type, v.size, v.width, v.height, v.created_at
        FROM assets a
        JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
        """;

    private static AssetFile ReadAsset(SqliteDataReader reader) =>
        new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4),
            reader.GetString(5), reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Clock.Parse(reader.GetString(9)));
}

public sealed record ProjectSummary(string Slug, string Name, int AssetCount, long TotalSize);

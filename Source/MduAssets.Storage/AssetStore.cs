using System.Text;
using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

/// <summary>An asset with its current version and how it is organized.</summary>
public sealed record AssetFile(
    string Id,
    long ProjectId,
    string ProjectSlug,
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
    DateTimeOffset CreatedAt,
    bool Animated = false,
    string? PairId = null,
    string? PairRole = null,
    string? PairFileName = null,
    DateTimeOffset? DeletedAt = null,
    long Requests = 0,
    DateTimeOffset? LastRequestAt = null,
    VersionOriginal? Original = null);

/// <summary>
/// The upload a version was converted from (UP-11): its type and size. <c>BlobSha256</c> is set only for versions from the
/// time the upload itself was kept; such files are never served publicly.
/// </summary>
public sealed record VersionOriginal(string? BlobSha256, string ContentType, long Size);

/// <summary>What public delivery needs to serve an asset, read without joins on the hot path.</summary>
public sealed record DeliveryFile(string BlobSha256, string ContentType, DateTimeOffset CreatedAt, int? Width, bool Animated);

/// <summary>What a public link points at: a file, an asset in the trash or deleted for good, or nothing.</summary>
public enum DeliveryStatus
{
    Found,
    Gone,
    Unknown,
}

/// <summary>A version of an asset (VER-03).</summary>
public sealed record AssetVersion(int Number, string BlobSha256, string ContentType, long Size, int? Width, int? Height, bool Animated, string CreatedBy, DateTimeOffset CreatedAt, VersionOriginal? Original = null);

/// <summary>The content of a new version.</summary>
public sealed record NewVersion(string BlobSha256, string ContentType, long Size, int? Width, int? Height, bool Animated, long CreatedBy, VersionOriginal? Original = null);

/// <summary>An asset read from a project export, with its blobs already stored on this instance.</summary>
public sealed record ImportedAsset(
    long ProjectId, string FileName, string AltText, string Description, IReadOnlyList<string> Tags,
    int CurrentVersion, IReadOnlyList<ImportedVersion> Versions, long ImportedBy, DateTimeOffset CreatedAt);

public sealed record ImportedVersion(int Number, string BlobSha256, string ContentType, long Size, int? Width, int? Height, bool Animated, DateTimeOffset CreatedAt, VersionOriginal? Original = null);

/// <summary>An asset in the trash, for the trash list (DEL-02).</summary>
public sealed record TrashedAsset(AssetFile Asset, DateTimeOffset DeletedAt, string? DeletedBy);

public sealed record NewAsset(
    long ProjectId,
    string FileName,
    string AltText,
    string BlobSha256,
    string ContentType,
    long Size,
    int? Width,
    int? Height,
    long CreatedBy,
    bool Animated = false,
    VersionOriginal? Original = null);

/// <summary>
/// Search and filters for the asset list (ORG-04). <c>Tags</c> must all be on an asset; <c>Untagged</c> finds assets without tags.
/// Dates are UTC; <c>Before</c> is exclusive.
/// </summary>
public sealed record AssetQuery(
    string? Text = null,
    IReadOnlyList<string>? Tags = null,
    string? ContentType = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Before = null,
    string? UploadedBy = null,
    int? NotRequestedForDays = null,
    DateTimeOffset? Now = null,
    bool Untagged = false);

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
            if (IsRetired(connection, transaction, id))
                continue;
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
                        INSERT INTO asset_versions (asset_id, number, blob_sha256, content_type, size, width, height, animated, created_by, created_at, original_sha256, original_content_type, original_size)
                        VALUES ($id, 1, $blob, $type, $size, $width, $height, $animated, $user, $now, $original, $originalType, $originalSize);
                        """;
                    insertVersion.Parameters.AddWithValue("$id", id);
                    insertVersion.Parameters.AddWithValue("$blob", asset.BlobSha256);
                    insertVersion.Parameters.AddWithValue("$type", asset.ContentType);
                    insertVersion.Parameters.AddWithValue("$size", asset.Size);
                    insertVersion.Parameters.AddWithValue("$width", (object?)asset.Width ?? DBNull.Value);
                    insertVersion.Parameters.AddWithValue("$height", (object?)asset.Height ?? DBNull.Value);
                    insertVersion.Parameters.AddWithValue("$animated", asset.Animated);
                    insertVersion.Parameters.AddWithValue("$user", asset.CreatedBy);
                    insertVersion.Parameters.AddWithValue("$now", now);
                    AddOriginal(insertVersion, asset.Original);
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
        var sql = new StringBuilder(SelectCurrent).Append(" WHERE a.project_id = $project AND a.deleted_at IS NULL");
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
                     AND (unicode_lower(a.file_name) LIKE {parameter} ESCAPE '\' OR unicode_lower(a.alt_text) LIKE {parameter} ESCAPE '\'
                          OR unicode_lower(a.description) LIKE {parameter} ESCAPE '\'
                          OR EXISTS (SELECT 1 FROM tags t WHERE t.asset_id = a.id AND t.key LIKE {parameter} ESCAPE '\'))
                    """);
                command.Parameters.AddWithValue(parameter, "%" + EscapeLike(TagKey(words[i])) + "%");
            }
        }

        // Every chosen tag must be on the image (ORG-04): more tags narrow the list down.
        var tagKeys = (query.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => TagKey(t.Trim())).Distinct().ToList();
        for (var i = 0; i < tagKeys.Count; i++)
        {
            sql.Append($" AND EXISTS (SELECT 1 FROM tags t WHERE t.asset_id = a.id AND t.key = $tag{i})");
            command.Parameters.AddWithValue($"$tag{i}", tagKeys[i]);
        }

        if (query.Untagged)
            sql.Append(" AND NOT EXISTS (SELECT 1 FROM tags t WHERE t.asset_id = a.id)");

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

        // Unused images (ORG-04): 0 days means never requested; otherwise no request within that many days.
        if (query.NotRequestedForDays is { } days)
        {
            if (days == 0)
                sql.Append(" AND s.hit_count IS NULL");
            else
            {
                sql.Append(" AND (s.last_hit_at IS NULL OR s.last_hit_at < $unusedSince)");
                command.Parameters.AddWithValue("$unusedSince", Clock.Format((query.Now ?? DateTimeOffset.UtcNow).AddDays(-days)));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.UploadedBy))
        {
            sql.Append(" AND u.name = $uploader");
            command.Parameters.AddWithValue("$uploader", query.UploadedBy.Trim());
        }
    }

    /// <summary>How tags and search words compare: lower case for every alphabet, not only A to Z.</summary>
    public static string TagKey(string value) => value.ToLowerInvariant();

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>Whether an asset belongs to the project.</summary>
    public bool Contains(long projectId, string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM assets WHERE id = $id AND project_id = $project AND deleted_at IS NULL;";
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

    /// <summary>
    /// Finds what public delivery needs for the current version of an asset, or for version <paramref name="version"/>
    /// (DLV-02). Assets in the trash or deleted for good are <see cref="DeliveryStatus.Gone"/> (DLV-07).
    /// </summary>
    public (DeliveryStatus Status, DeliveryFile? File) FindForDelivery(string id, int? version = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.deleted_at, v.blob_sha256, v.content_type, v.created_at, v.width, v.animated
            FROM assets a
            LEFT JOIN asset_versions v ON v.asset_id = a.id AND v.number = coalesce($version, a.current_version)
            WHERE a.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return (DeliveryStatus.Unknown, null);
        if (!reader.IsDBNull(0))
            return (DeliveryStatus.Gone, null);
        if (reader.IsDBNull(1))
            return (DeliveryStatus.Unknown, null);
        return (DeliveryStatus.Found, new DeliveryFile(reader.GetString(1), reader.GetString(2), Clock.Parse(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetBoolean(5)));
    }

    /// <summary>
    /// The status of a link for the public check (API-06): <c>ok</c>, <c>trash</c>, <c>deleted</c> (for good) or <c>unknown</c>,
    /// also for a version that does not exist. Nothing else about the asset is read.
    /// </summary>
    public string LinkStatus(string id, int? version)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN a.purged_at IS NOT NULL THEN 'deleted' WHEN a.deleted_at IS NOT NULL THEN 'trash'
                        WHEN v.number IS NULL THEN 'unknown' ELSE 'ok' END
            FROM assets a LEFT JOIN asset_versions v ON v.asset_id = a.id AND v.number = coalesce($version, a.current_version)
            WHERE a.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        return command.ExecuteScalar() as string ?? "unknown";
    }

    /// <summary>All versions of an asset, newest first (VER-03).</summary>
    public IReadOnlyList<AssetVersion> Versions(string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.number, v.blob_sha256, v.content_type, v.size, v.width, v.height, v.animated, u.name, v.created_at,
                   v.original_sha256, v.original_content_type, v.original_size
            FROM asset_versions v JOIN users u ON u.id = v.created_by
            WHERE v.asset_id = $id ORDER BY v.number DESC;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var versions = new List<AssetVersion>();
        while (reader.Read())
            versions.Add(new AssetVersion(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetBoolean(6), reader.GetString(7), Clock.Parse(reader.GetString(8)), ReadOriginal(reader, 9)));
        return versions;
    }

    /// <summary>
    /// Adds a version and makes it current (VER-01); the link stays the same. With <paramref name="keep"/>, older versions
    /// beyond that number are removed (VER-04). <paramref name="fileName"/> renames the asset in the same step, for a version in
    /// another format (VER-02). Returns the blobs of removed versions, which may now be unused.
    /// </summary>
    public IReadOnlyList<string> AddVersion(string id, NewVersion version, int? keep = null, string? fileName = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO asset_versions (asset_id, number, blob_sha256, content_type, size, width, height, animated, created_by, created_at, original_sha256, original_content_type, original_size)
                VALUES ($id, (SELECT max(number) + 1 FROM asset_versions WHERE asset_id = $id), $blob, $type, $size, $width, $height, $animated, $user, $now,
                        $original, $originalType, $originalSize);
                UPDATE assets SET current_version = (SELECT max(number) FROM asset_versions WHERE asset_id = $id),
                    file_name = coalesce($fileName, file_name)
                WHERE id = $id;
                """;
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$blob", version.BlobSha256);
            insert.Parameters.AddWithValue("$type", version.ContentType);
            insert.Parameters.AddWithValue("$size", version.Size);
            insert.Parameters.AddWithValue("$width", (object?)version.Width ?? DBNull.Value);
            insert.Parameters.AddWithValue("$height", (object?)version.Height ?? DBNull.Value);
            insert.Parameters.AddWithValue("$animated", version.Animated);
            insert.Parameters.AddWithValue("$user", version.CreatedBy);
            insert.Parameters.AddWithValue("$now", Clock.Now());
            insert.Parameters.AddWithValue("$fileName", (object?)fileName ?? DBNull.Value);
            AddOriginal(insert, version.Original);
            insert.ExecuteNonQuery();
        }

        var removed = new List<string>();
        if (keep is { } limit)
        {
            using var prune = connection.CreateCommand();
            prune.Transaction = transaction;
            prune.CommandText = """
                DELETE FROM asset_versions WHERE asset_id = $id AND number IN
                    (SELECT number FROM asset_versions WHERE asset_id = $id ORDER BY number DESC LIMIT -1 OFFSET $keep)
                RETURNING blob_sha256, original_sha256;
                """;
            prune.Parameters.AddWithValue("$id", id);
            prune.Parameters.AddWithValue("$keep", limit);
            using var reader = prune.ExecuteReader();
            ReadBlobs(reader, removed);
        }

        transaction.Commit();
        return removed;
    }

    /// <summary>
    /// Deletes an old version for good (VER-04). The current version cannot be deleted; returns its blobs (with the original),
    /// or none when nothing was deleted.
    /// </summary>
    public IReadOnlyList<string> DeleteVersion(string id, int number)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM asset_versions WHERE asset_id = $id AND number = $number
                AND number <> (SELECT current_version FROM assets WHERE id = $id)
            RETURNING blob_sha256, original_sha256;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$number", number);
        using var reader = command.ExecuteReader();
        var blobs = new List<string>();
        ReadBlobs(reader, blobs);
        return blobs;
    }

    /// <summary>Moves an asset to the trash (DEL-01). Its pair is dissolved, because the partner stays in use.</summary>
    public bool Trash(string id, long userId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE assets SET pair_asset_id = NULL, pair_role = NULL WHERE pair_asset_id = $id;
            UPDATE assets SET deleted_at = $now, deleted_by = $user, pair_asset_id = NULL, pair_role = NULL
            WHERE id = $id AND deleted_at IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$now", Clock.Now());
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Takes an asset out of the trash; its link works again (DEL-02).</summary>
    public bool Restore(string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE assets SET deleted_at = NULL, deleted_by = NULL WHERE id = $id AND deleted_at IS NOT NULL AND purged_at IS NULL;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>The assets of a project in the trash, recently deleted first.</summary>
    public IReadOnlyList<TrashedAsset> Trash(long projectId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectCurrent.Replace("LEFT JOIN assets pa", "LEFT JOIN users du ON du.id = a.deleted_by\n        LEFT JOIN assets pa", StringComparison.Ordinal)
            .Replace("v.original_size\n", "v.original_size, du.name\n", StringComparison.Ordinal) +
            " WHERE a.project_id = $project AND a.deleted_at IS NOT NULL AND a.purged_at IS NULL ORDER BY a.deleted_at DESC;";
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var assets = new List<TrashedAsset>();
        while (reader.Read())
        {
            var asset = ReadAsset(reader);
            assets.Add(new TrashedAsset(asset, asset.DeletedAt!.Value, reader.IsDBNull(25) ? null : reader.GetString(25)));
        }

        return assets;
    }

    /// <summary>Ids of trashed assets deleted before <paramref name="cutoff"/>, for the final deletion (DEL-02).</summary>
    public IReadOnlyList<string> TrashedBefore(DateTimeOffset cutoff, long? projectId = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id FROM assets WHERE deleted_at IS NOT NULL AND purged_at IS NULL AND deleted_at < $cutoff
                AND ($project IS NULL OR project_id = $project);
            """;
        command.Parameters.AddWithValue("$cutoff", Clock.Format(cutoff));
        command.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>
    /// Deletes a trashed asset for good (DEL-02, DEL-03): versions and tags go, the row stays as a tombstone, so the id is never
    /// reused and the link keeps answering 410. Returns the blobs of the removed versions, which may now be unused.
    /// </summary>
    public IReadOnlyList<string> Purge(string id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var blobs = new List<string>();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM asset_versions WHERE asset_id = $id
                    AND EXISTS (SELECT 1 FROM assets WHERE id = $id AND deleted_at IS NOT NULL AND purged_at IS NULL)
                RETURNING blob_sha256, original_sha256;
                """;
            delete.Parameters.AddWithValue("$id", id);
            using var reader = delete.ExecuteReader();
            ReadBlobs(reader, blobs);
        }

        using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = """
                DELETE FROM tags WHERE asset_id = $id;
                DELETE FROM asset_referrers WHERE asset_id = $id;
                UPDATE assets SET purged_at = $now WHERE id = $id AND deleted_at IS NOT NULL;
                """;
            mark.Parameters.AddWithValue("$id", id);
            mark.Parameters.AddWithValue("$now", Clock.Now());
            mark.ExecuteNonQuery();
        }

        transaction.Commit();
        return blobs;
    }

    /// <summary>
    /// Inserts an asset from a project export with all its versions (OPS-06). It keeps <paramref name="preferredId"/> when that id
    /// was never used on this instance, otherwise it gets a new one ([ADR-0018](Architecture-Decisions/0018-Import-Id-Collisions.md)).
    /// Returns the id the asset has now.
    /// </summary>
    public string Import(string preferredId, ImportedAsset asset)
    {
        using var connection = database.Open();
        for (var attempt = 0; ; attempt++)
        {
            var id = attempt == 0 && AssetId.IsValid(preferredId) ? preferredId : AssetId.New();
            using var transaction = connection.BeginTransaction();
            using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT 1 FROM assets WHERE id = $id UNION ALL SELECT 1 FROM retired_asset_ids WHERE id = $id;";
                check.Parameters.AddWithValue("$id", id);
                if (check.ExecuteScalar() is not null)
                    continue;
            }

            void Execute(string sql, params (string Name, object? Value)[] parameters)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                command.ExecuteNonQuery();
            }

            Execute("""
                INSERT INTO assets (id, project_id, file_name, alt_text, description, current_version, created_by, created_at)
                VALUES ($id, $project, $fileName, $alt, $description, $current, $user, $createdAt);
                """,
                ("$id", id), ("$project", asset.ProjectId), ("$fileName", asset.FileName), ("$alt", asset.AltText),
                ("$description", asset.Description), ("$current", asset.CurrentVersion), ("$user", asset.ImportedBy), ("$createdAt", Clock.Format(asset.CreatedAt)));
            foreach (var version in asset.Versions)
                Execute("""
                    INSERT INTO asset_versions (asset_id, number, blob_sha256, content_type, size, width, height, animated, created_by, created_at, original_sha256, original_content_type, original_size)
                    VALUES ($id, $number, $blob, $type, $size, $width, $height, $animated, $user, $createdAt, $original, $originalType, $originalSize);
                    """,
                    ("$id", id), ("$number", version.Number), ("$blob", version.BlobSha256), ("$type", version.ContentType), ("$size", version.Size),
                    ("$width", version.Width), ("$height", version.Height), ("$animated", version.Animated), ("$user", asset.ImportedBy),
                    ("$createdAt", Clock.Format(version.CreatedAt)), ("$original", version.Original?.BlobSha256),
                    ("$originalType", version.Original?.ContentType), ("$originalSize", version.Original?.Size));
            foreach (var tag in asset.Tags)
                Execute("INSERT OR IGNORE INTO tags (asset_id, key, name) VALUES ($id, $key, $name);", ("$id", id), ("$key", TagKey(tag)), ("$name", tag));
            transaction.Commit();
            return id;
        }
    }

    /// <summary>Whether any version still refers to a blob; unused blobs and their variants can go.</summary>
    public bool IsBlobUsed(string blobSha256)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM asset_versions WHERE blob_sha256 = $blob OR original_sha256 = $blob LIMIT 1;";
        command.Parameters.AddWithValue("$blob", blobSha256);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Whether an id was given to an asset that no longer has a row; such ids are never handed out again.</summary>
    private static bool IsRetired(SqliteConnection connection, SqliteTransaction transaction, string id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM retired_asset_ids WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>
    /// Changes the descriptive fields in one transaction.
    /// <paramref name="tags"/> replaces all tags when given. The public link stays valid,
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
                command.CommandText = """
                    UPDATE assets SET file_name = $fileName, alt_text = $alt, description = $description WHERE id = $id;
                    """;
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
                    insert.CommandText = "INSERT OR IGNORE INTO tags (asset_id, key, name) VALUES ($id, $key, $name);";
                    insert.Parameters.AddWithValue("$id", id);
                    insert.Parameters.AddWithValue("$key", TagKey(tag));
                    insert.Parameters.AddWithValue("$name", tag);
                    insert.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        return FindCurrent(id);
    }

    /// <summary>
    /// Adds and removes tags on several assets of a project at once (ORG-03). A tag that the project already uses keeps its
    /// spelling. Assets of other projects and assets in the trash are left alone. Returns the number of assets whose tags
    /// changed, or <c>null</c> when an asset would get more than <paramref name="maxPerAsset"/> tags; then nothing changes.
    /// </summary>
    public int? ChangeTags(long projectId, IReadOnlyCollection<string> ids, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, int maxPerAsset)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var distinct = ids.Distinct(StringComparer.Ordinal).ToList();
        var targets = new List<string>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            var names = distinct.Select((_, i) => $"$id{i}").ToList();
            select.CommandText = $"SELECT id FROM assets WHERE project_id = $project AND deleted_at IS NULL AND id IN ({string.Join(", ", names)});";
            select.Parameters.AddWithValue("$project", projectId);
            for (var i = 0; i < distinct.Count; i++)
                select.Parameters.AddWithValue(names[i], distinct[i]);
            using var reader = select.ExecuteReader();
            while (reader.Read())
                targets.Add(reader.GetString(0));
        }

        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in targets)
        {
            foreach (var tag in remove)
            {
                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM tags WHERE asset_id = $id AND key = $key;";
                delete.Parameters.AddWithValue("$id", id);
                delete.Parameters.AddWithValue("$key", TagKey(tag.Trim()));
                if (delete.ExecuteNonQuery() > 0)
                    changed.Add(id);
            }

            foreach (var tag in add)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT OR IGNORE INTO tags (asset_id, key, name)
                    VALUES ($id, $key, coalesce((SELECT t.name FROM tags t JOIN assets a ON a.id = t.asset_id
                                                 WHERE a.project_id = $project AND t.key = $key LIMIT 1), $name));
                    """;
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$key", TagKey(tag));
                insert.Parameters.AddWithValue("$name", tag);
                insert.Parameters.AddWithValue("$project", projectId);
                if (insert.ExecuteNonQuery() > 0)
                    changed.Add(id);
            }

            using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT count(*) FROM tags WHERE asset_id = $id;";
            count.Parameters.AddWithValue("$id", id);
            if ((long)count.ExecuteScalar()! > maxPerAsset)
                return null;
        }

        transaction.Commit();
        return changed.Count;
    }

    /// <summary>
    /// Renames a tag in all assets of a project, also in the trash (ORG-03). When <paramref name="newName"/> is a tag the
    /// project already uses, the two are merged. Returns the number of assets that had the tag, 0 when none had it.
    /// </summary>
    public int RenameTag(long projectId, string oldName, string newName)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, out int rows)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$old", TagKey(oldName.Trim()));
            command.Parameters.AddWithValue("$new", TagKey(newName));
            command.Parameters.AddWithValue("$name", newName);
            rows = command.ExecuteNonQuery();
        }

        Execute("""
            INSERT OR IGNORE INTO tags (asset_id, key, name)
            SELECT t.asset_id, $new, $name FROM tags t JOIN assets a ON a.id = t.asset_id WHERE a.project_id = $project AND t.key = $old;
            """, out _);
        Execute("""
            DELETE FROM tags WHERE key = $old AND $old <> $new AND asset_id IN (SELECT id FROM assets WHERE project_id = $project);
            """, out var removed);
        // The new spelling applies everywhere, also where the target tag existed before.
        Execute("""
            UPDATE tags SET name = $name WHERE key = $new AND asset_id IN (SELECT id FROM assets WHERE project_id = $project);
            """, out var renamed);
        transaction.Commit();
        return TagKey(oldName.Trim()) == TagKey(newName) ? renamed : removed;
    }

    /// <summary>Removes a tag from all assets of a project, also in the trash. Returns the number of assets that had it.</summary>
    public int DeleteTag(long projectId, string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tags WHERE key = $key AND asset_id IN (SELECT id FROM assets WHERE project_id = $project);";
        command.Parameters.AddWithValue("$key", TagKey(name.Trim()));
        command.Parameters.AddWithValue("$project", projectId);
        return command.ExecuteNonQuery();
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
                SELECT min(t.name), count(*) FROM tags t JOIN assets a ON a.id = t.asset_id
                WHERE a.project_id = $project AND a.deleted_at IS NULL GROUP BY t.key ORDER BY count(*) DESC, t.key;
                """, r => new TagCount(r.GetString(0), r.GetInt32(1))),
            Query("""
                SELECT DISTINCT u.name FROM assets a JOIN users u ON u.id = a.created_by
                WHERE a.project_id = $project AND a.deleted_at IS NULL ORDER BY u.name COLLATE NOCASE;
                """, r => r.GetString(0)),
            Query("""
                SELECT DISTINCT v.content_type FROM assets a JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
                WHERE a.project_id = $project AND a.deleted_at IS NULL ORDER BY v.content_type;
                """, r => r.GetString(0)));
    }

    /// <summary>The first asset of a project whose current version has this content (UP-09), or <c>null</c>.</summary>
    public string? FindByContent(long projectId, string blobSha256)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id FROM assets a JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
            WHERE a.project_id = $project AND a.deleted_at IS NULL AND v.blob_sha256 = $blob ORDER BY a.created_at LIMIT 1;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$blob", blobSha256);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Links two assets of a project as light and dark variant (LNK-06). Earlier partners of either asset lose their pair,
    /// so a pair is always two assets that point at each other.
    /// </summary>
    public void SetPair(string id, string role, string partnerId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        const string clear = """
            UPDATE assets SET pair_asset_id = NULL, pair_role = NULL
            WHERE id IN ($a, $b) OR pair_asset_id IN ($a, $b);
            """;
        Execute(clear, ("$a", id), ("$b", partnerId));
        const string set = "UPDATE assets SET pair_asset_id = $partner, pair_role = $role WHERE id = $id;";
        Execute(set, ("$id", id), ("$partner", partnerId), ("$role", role));
        Execute(set, ("$id", partnerId), ("$partner", id), ("$role", role == "light" ? "dark" : "light"));
        transaction.Commit();
    }

    /// <summary>Dissolves the pair of an asset on both sides.</summary>
    public void ClearPair(string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE assets SET pair_asset_id = NULL, pair_role = NULL WHERE id = $id OR pair_asset_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Bytes on disk for all versions and their originals; the same content shares one blob and counts once.</summary>
    public long StorageUsed()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT coalesce(sum(size), 0) FROM (
                SELECT blob_sha256, size FROM asset_versions
                UNION SELECT original_sha256, original_size FROM asset_versions WHERE original_sha256 IS NOT NULL);
            """;
        return (long)command.ExecuteScalar()!;
    }

    private const string SelectCurrent = """
        SELECT a.id, a.project_id, p.slug, a.file_name, a.alt_text, a.description,
               (SELECT group_concat(t.name, char(31)) FROM (SELECT name FROM tags WHERE asset_id = a.id ORDER BY key) t),
               v.number, v.blob_sha256, v.content_type, v.size, v.width, v.height, u.name, v.created_at,
               v.animated, a.pair_asset_id, a.pair_role, pa.file_name, s.hit_count, s.last_hit_at, a.deleted_at,
               v.original_sha256, v.original_content_type, v.original_size
        FROM assets a
        JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
        JOIN projects p ON p.id = a.project_id
        JOIN users u ON u.id = a.created_by
        LEFT JOIN assets pa ON pa.id = a.pair_asset_id
        LEFT JOIN asset_stats s ON s.asset_id = a.id
        """;

    private static AssetFile ReadAsset(SqliteDataReader reader) =>
        new(
            reader.GetString(0), reader.GetInt64(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? [] : reader.GetString(6).Split(TagSeparator),
            reader.GetInt32(7), reader.GetString(8), reader.GetString(9), reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetInt32(12),
            reader.GetString(13),
            Clock.Parse(reader.GetString(14)),
            reader.GetBoolean(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(21) ? null : Clock.Parse(reader.GetString(21)),
            reader.IsDBNull(19) ? 0 : reader.GetInt64(19),
            reader.IsDBNull(20) ? null : Clock.Parse(reader.GetString(20)),
            ReadOriginal(reader, 22));

    private static VersionOriginal? ReadOriginal(SqliteDataReader reader, int first) =>
        reader.IsDBNull(first + 1) ? null
            : new VersionOriginal(reader.IsDBNull(first) ? null : reader.GetString(first), reader.GetString(first + 1), reader.GetInt64(first + 2));

    private static void AddOriginal(SqliteCommand command, VersionOriginal? original)
    {
        command.Parameters.AddWithValue("$original", (object?)original?.BlobSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$originalType", (object?)original?.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("$originalSize", (object?)original?.Size ?? DBNull.Value);
    }

    /// <summary>Reads <c>RETURNING blob_sha256, original_sha256</c>: every blob a deleted version used.</summary>
    internal static void ReadBlobs(SqliteDataReader reader, List<string> blobs)
    {
        while (reader.Read())
        {
            blobs.Add(reader.GetString(0));
            if (!reader.IsDBNull(1))
                blobs.Add(reader.GetString(1));
        }
    }
}

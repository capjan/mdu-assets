using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

public sealed record Folder(long Id, long ProjectId, long? ParentId, string Name, int AssetCount);

/// <summary>A folder change that the folder tree does not allow: a name taken by a sibling, or a move into itself.</summary>
public sealed class FolderConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Nested folders of a project (ORG-01, ORG-02). Moving assets or folders never changes a link.</summary>
public sealed class FolderStore(Database database)
{
    /// <summary>All folders of a project with the number of assets directly inside, ordered by name.</summary>
    public IReadOnlyList<Folder> List(long projectId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.id, f.project_id, f.parent_id, f.name, (SELECT count(*) FROM assets a WHERE a.folder_id = f.id)
            FROM folders f WHERE f.project_id = $project
            ORDER BY f.name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var folders = new List<Folder>();
        while (reader.Read())
            folders.Add(Read(reader));
        return folders;
    }

    public Folder? Find(long projectId, long id)
    {
        using var connection = database.Open();
        return Find(connection, null, projectId, id);
    }

    /// <exception cref="FolderConflictException">A sibling has the same name.</exception>
    public Folder Create(long projectId, long? parentId, string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO folders (project_id, parent_id, name, created_at) VALUES ($project, $parent, $name, $now) RETURNING id;";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", Clock.Now());
        try
        {
            return new Folder((long)command.ExecuteScalar()!, projectId, parentId, name, 0);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067) // SQLITE_CONSTRAINT_UNIQUE
        {
            throw NameTaken(name, exception);
        }
    }

    /// <exception cref="FolderConflictException">A sibling has the same name.</exception>
    public Folder Rename(long projectId, long id, string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE folders SET name = $name WHERE id = $id AND project_id = $project;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$name", name);
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            throw NameTaken(name, exception);
        }

        return Find(connection, null, projectId, id)!;
    }

    /// <summary>Moves a folder under another folder of the project, or to the top level for <c>null</c>.</summary>
    /// <exception cref="FolderConflictException">The target is the folder itself or inside it, or a sibling has the same name.</exception>
    public Folder Move(long projectId, long id, long? parentId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        if (parentId is { } target && SubtreeOf(connection, transaction, id).Contains(target))
            throw new FolderConflictException("A folder cannot be moved into itself or one of its subfolders.");

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE folders SET parent_id = $parent WHERE id = $id AND project_id = $project;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
            try
            {
                command.ExecuteNonQuery();
            }
            catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
            {
                throw new FolderConflictException("The target already has a folder with this name. Rename one of them first.");
            }
        }

        transaction.Commit();
        return Find(connection, null, projectId, id)!;
    }

    /// <summary>
    /// Deletes a folder. Its images and subfolders move to the parent folder (ORG-01), so nothing is lost and no link changes.
    /// </summary>
    /// <exception cref="FolderConflictException">A subfolder has the name of a folder in the parent.</exception>
    public void Delete(long projectId, long id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var folder = Find(connection, transaction, projectId, id);
        if (folder is null)
            return;

        void Execute(string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$parent", (object?)folder.ParentId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        Execute("UPDATE assets SET folder_id = $parent WHERE folder_id = $id;");
        try
        {
            Execute("UPDATE folders SET parent_id = $parent WHERE parent_id = $id;");
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            throw new FolderConflictException(
                "A subfolder has the same name as a folder it would move next to. Rename or move it first.");
        }

        Execute("DELETE FROM folders WHERE id = $id;");
        transaction.Commit();
    }

    /// <summary>The folder and all folders below it.</summary>
    public IReadOnlyList<long> Subtree(long id)
    {
        using var connection = database.Open();
        return SubtreeOf(connection, null, id);
    }

    private static List<long> SubtreeOf(SqliteConnection connection, SqliteTransaction? transaction, long id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE tree(id) AS (SELECT $id UNION SELECT f.id FROM folders f JOIN tree t ON f.parent_id = t.id)
            SELECT id FROM tree;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));
        return ids;
    }

    private static Folder? Find(SqliteConnection connection, SqliteTransaction? transaction, long projectId, long id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT f.id, f.project_id, f.parent_id, f.name, (SELECT count(*) FROM assets a WHERE a.folder_id = f.id)
            FROM folders f WHERE f.id = $id AND f.project_id = $project;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static Folder Read(SqliteDataReader reader) =>
        new(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4));

    private static FolderConflictException NameTaken(string name, Exception inner) =>
        new($"There is already a folder named '{name}' here.", inner);
}

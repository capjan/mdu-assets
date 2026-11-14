using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

/// <summary>Project roles from section 4.2 of the requirements; a higher value includes the rights of the lower ones.</summary>
public enum ProjectRole
{
    Viewer = 1,
    Editor = 2,
    Owner = 3,
}

public static class ProjectRoles
{
    public static string ToText(this ProjectRole role) => role switch
    {
        ProjectRole.Viewer => "viewer",
        ProjectRole.Editor => "editor",
        ProjectRole.Owner => "owner",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static ProjectRole? Parse(string? text) => text?.ToLowerInvariant() switch
    {
        "viewer" => ProjectRole.Viewer,
        "editor" => ProjectRole.Editor,
        "owner" => ProjectRole.Owner,
        _ => null,
    };
}

public sealed record Project(long Id, string Slug, string Name, string Description);

/// <summary>A project with its usage and the role of the user who asked; <c>Role</c> is <c>null</c> when an admin is no member.</summary>
public sealed record ProjectSummary(
    string Slug,
    string Name,
    string Description,
    int AssetCount,
    long TotalSize,
    DateTimeOffset? LastUpload,
    ProjectRole? Role);

public sealed record Member(string UserName, ProjectRole Role, bool IsLocked);

/// <summary>A change that would leave a project without an owner.</summary>
public sealed class LastOwnerException(string message) : Exception(message);

/// <summary>Reads and writes projects and their members.</summary>
public sealed class ProjectStore(Database database)
{
    public Project? Find(string slug)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, slug, name, description FROM projects WHERE slug = $slug;";
        command.Parameters.AddWithValue("$slug", slug);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new Project(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)) : null;
    }

    /// <summary>
    /// The projects a user is a member of, or all projects when <paramref name="all"/> is set (admins),
    /// with the number and size of their assets, ordered by name.
    /// </summary>
    public IReadOnlyList<ProjectSummary> List(long userId, bool all, long? projectId = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT p.slug, p.name, p.description,
                   (SELECT count(*) FROM assets a WHERE a.project_id = p.id),
                   (SELECT coalesce(sum(v.size), 0) FROM assets a
                    JOIN asset_versions v ON v.asset_id = a.id AND v.number = a.current_version
                    WHERE a.project_id = p.id),
                   (SELECT max(a.created_at) FROM assets a WHERE a.project_id = p.id),
                   m.role
            FROM projects p
            {(all ? "LEFT JOIN" : "JOIN")} memberships m ON m.project_id = p.id AND m.user_id = $user
            {(projectId is null ? "" : "WHERE p.id = $project")}
            ORDER BY p.name COLLATE NOCASE;
            """;
        if (projectId is not null)
            command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$user", userId);
        using var reader = command.ExecuteReader();
        var projects = new List<ProjectSummary>();
        while (reader.Read())
            projects.Add(new ProjectSummary(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt64(4),
                reader.IsDBNull(5) ? null : Clock.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : ProjectRoles.Parse(reader.GetString(6))));
        return projects;
    }

    /// <summary>Creates a project with its creator as owner (PRJ-01).</summary>
    /// <exception cref="InvalidOperationException">The slug is taken.</exception>
    public Project Create(string slug, string name, string description, long ownerId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var now = Clock.Now();
        long id;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO projects (slug, name, description, created_at) VALUES ($slug, $name, $description, $now) RETURNING id;";
            insert.Parameters.AddWithValue("$slug", slug);
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$description", description);
            insert.Parameters.AddWithValue("$now", now);
            try
            {
                id = (long)insert.ExecuteScalar()!;
            }
            catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067) // SQLITE_CONSTRAINT_UNIQUE
            {
                throw new InvalidOperationException($"A project with the short name '{slug}' already exists.", exception);
            }
        }

        SetRole(connection, transaction, id, ownerId, ProjectRole.Owner);
        transaction.Commit();
        return new Project(id, slug, name, description);
    }

    /// <summary>Changes name and description. The slug and all asset links stay the same (PRJ-04).</summary>
    public Project Update(long id, string name, string description)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE projects SET name = $name, description = $description WHERE id = $id RETURNING id, slug, name, description;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$description", description);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new Project(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    /// <summary>Deletes a project without assets. Returns <c>false</c> when it still has assets.</summary>
    public bool DeleteEmpty(long id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM projects WHERE id = $id AND NOT EXISTS (SELECT 1 FROM assets WHERE project_id = $id);";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>The role of a user in a project, or <c>null</c> for non-members.</summary>
    public ProjectRole? RoleOf(long projectId, long userId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT role FROM memberships WHERE project_id = $project AND user_id = $user;";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$user", userId);
        return command.ExecuteScalar() is string role ? ProjectRoles.Parse(role) : null;
    }

    /// <summary>The members of a project: owners first, then by name.</summary>
    public IReadOnlyList<Member> Members(long projectId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.name, m.role, u.is_locked
            FROM memberships m JOIN users u ON u.id = m.user_id
            WHERE m.project_id = $project
            ORDER BY CASE m.role WHEN 'owner' THEN 0 WHEN 'editor' THEN 1 ELSE 2 END, u.name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var members = new List<Member>();
        while (reader.Read())
            members.Add(new Member(reader.GetString(0), ProjectRoles.Parse(reader.GetString(1))!.Value, reader.GetBoolean(2)));
        return members;
    }

    /// <summary>Adds a member or changes the role (PRJ-03). Throws <see cref="LastOwnerException"/> when no owner would be left.</summary>
    public void SetRole(long projectId, long userId, ProjectRole role)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        SetRole(connection, transaction, projectId, userId, role);
        EnsureOwner(connection, transaction, projectId);
        transaction.Commit();
    }

    /// <summary>Removes a member. Returns <c>false</c> for non-members; throws <see cref="LastOwnerException"/> for the last owner.</summary>
    public bool RemoveMember(long projectId, long userId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        int removed;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM memberships WHERE project_id = $project AND user_id = $user;";
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$user", userId);
            removed = command.ExecuteNonQuery();
        }

        EnsureOwner(connection, transaction, projectId);
        transaction.Commit();
        return removed > 0;
    }

    /// <summary>Makes a user owner of every project that has no owner yet, for example the first admin of a new instance.</summary>
    public void AdoptOwnerlessProjects(long userId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO memberships (project_id, user_id, role, created_at)
            SELECT p.id, $user, 'owner', $now FROM projects p
            WHERE NOT EXISTS (SELECT 1 FROM memberships m WHERE m.project_id = p.id AND m.role = 'owner')
            ON CONFLICT (project_id, user_id) DO UPDATE SET role = 'owner';
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$now", Clock.Now());
        command.ExecuteNonQuery();
    }

    private static void SetRole(SqliteConnection connection, SqliteTransaction transaction, long projectId, long userId, ProjectRole role)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO memberships (project_id, user_id, role, created_at) VALUES ($project, $user, $role, $now)
            ON CONFLICT (project_id, user_id) DO UPDATE SET role = excluded.role;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$role", role.ToText());
        command.Parameters.AddWithValue("$now", Clock.Now());
        command.ExecuteNonQuery();
    }

    private static void EnsureOwner(SqliteConnection connection, SqliteTransaction transaction, long projectId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM memberships WHERE project_id = $project AND role = 'owner';";
        command.Parameters.AddWithValue("$project", projectId);
        if ((long)command.ExecuteScalar()! == 0)
            throw new LastOwnerException("A project needs at least one owner. Make another member an owner first.");
    }
}

/// <summary>Instance settings that admins change at run time.</summary>
public sealed class SettingsStore(Database database)
{
    public const string UsersCanCreateProjects = "users_can_create_projects";

    public bool GetFlag(string key)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string == "true";
    }

    public void SetFlag(string key, bool value)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value ? "true" : "false");
        command.ExecuteNonQuery();
    }
}

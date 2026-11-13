using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

public sealed record User(long Id, string Name, string PasswordHash, bool IsAdmin, bool IsLocked);

/// <summary>Reads and writes users.</summary>
public sealed class UserStore(Database database)
{
    public long Count()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM users;";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Finds a user by name, ignoring case.</summary>
    public User? Find(string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, password_hash, is_admin, is_locked FROM users WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new User(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4))
            : null;
    }

    public User Create(string name, string passwordHash, bool isAdmin)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (name, password_hash, is_admin, created_at)
            VALUES ($name, $hash, $admin, $now)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$hash", passwordHash);
        command.Parameters.AddWithValue("$admin", isAdmin);
        command.Parameters.AddWithValue("$now", Clock.Now());
        try
        {
            var id = (long)command.ExecuteScalar()!;
            return new User(id, name, passwordHash, isAdmin, IsLocked: false);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067) // SQLITE_CONSTRAINT_UNIQUE
        {
            throw new InvalidOperationException($"A user named '{name}' already exists.", exception);
        }
    }

    public void SetLocked(long id, bool locked)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET is_locked = $locked WHERE id = $id;";
        command.Parameters.AddWithValue("$locked", locked);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}

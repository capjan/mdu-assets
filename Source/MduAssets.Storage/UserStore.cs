using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

public sealed record User(long Id, string Name, string PasswordHash, bool IsAdmin, bool IsLocked, bool MustChangePassword, DateTimeOffset CreatedAt);

/// <summary>A change that would leave the instance without an active admin.</summary>
public sealed class LastAdminException(string message) : Exception(message);

/// <summary>Reads and writes users.</summary>
public sealed class UserStore(Database database)
{
    private const string Select = "SELECT id, name, password_hash, is_admin, is_locked, must_change_password, created_at FROM users";

    public long Count()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM users;";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>All users, ordered by name.</summary>
    public IReadOnlyList<User> List()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Select + " ORDER BY name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        var users = new List<User>();
        while (reader.Read())
            users.Add(Read(reader));
        return users;
    }

    /// <summary>Finds a user by name, ignoring case.</summary>
    public User? Find(string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Select + " WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public User Create(string name, string passwordHash, bool isAdmin, bool mustChangePassword = false)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (name, password_hash, is_admin, must_change_password, created_at)
            VALUES ($name, $hash, $admin, $mustChange, $now)
            RETURNING id;
            """;
        var now = Clock.Now();
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$hash", passwordHash);
        command.Parameters.AddWithValue("$admin", isAdmin);
        command.Parameters.AddWithValue("$mustChange", mustChangePassword);
        command.Parameters.AddWithValue("$now", now);
        try
        {
            var id = (long)command.ExecuteScalar()!;
            return new User(id, name, passwordHash, isAdmin, IsLocked: false, mustChangePassword, Clock.Parse(now));
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067) // SQLITE_CONSTRAINT_UNIQUE
        {
            throw new InvalidOperationException($"A user named '{name}' already exists.", exception);
        }
    }

    /// <summary>
    /// Changes the admin flag and the lock of a user. Throws <see cref="LastAdminException"/> when no unlocked admin would be left.
    /// </summary>
    public User Update(long id, bool isAdmin, bool isLocked)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE users SET is_admin = $admin, is_locked = $locked WHERE id = $id;";
            command.Parameters.AddWithValue("$admin", isAdmin);
            command.Parameters.AddWithValue("$locked", isLocked);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT count(*) FROM users WHERE is_admin = 1 AND is_locked = 0;";
            if ((long)count.ExecuteScalar()! == 0)
                throw new LastAdminException("The last active admin cannot lose the admin role or be locked. Make another user an admin first.");
        }

        transaction.Commit();
        return FindById(connection, id)!;
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

    /// <summary>Sets a new password hash; <paramref name="mustChange"/> marks it as a start password.</summary>
    public void SetPassword(long id, string passwordHash, bool mustChange)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET password_hash = $hash, must_change_password = $mustChange WHERE id = $id;";
        command.Parameters.AddWithValue("$hash", passwordHash);
        command.Parameters.AddWithValue("$mustChange", mustChange);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static User? FindById(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Select + " WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static User Read(SqliteDataReader reader) =>
        new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4),
            reader.GetBoolean(5), Clock.Parse(reader.GetString(6)));
}

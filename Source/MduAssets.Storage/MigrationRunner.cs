using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

/// <summary>Brings the database schema up to date when the server starts.</summary>
public static class MigrationRunner
{
    /// <summary>
    /// Applies all migrations that are not applied yet, each in its own transaction, in version order.
    /// Fails when an applied migration was changed or is missing, or when the database is newer than this build.
    /// </summary>
    /// <returns>The versions applied by this call.</returns>
    public static IReadOnlyList<int> Apply(Database database, IReadOnlyList<Migration> migrations)
    {
        CheckSequence(migrations);

        using var connection = database.Open();
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    INTEGER PRIMARY KEY,
                name       TEXT NOT NULL,
                checksum   TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            """);

        var applied = ReadApplied(connection);
        var known = migrations.ToDictionary(m => m.Version);

        foreach (var (version, checksum) in applied)
        {
            if (!known.TryGetValue(version, out var migration))
                throw new InvalidOperationException(
                    $"The database has migration {version}, which this build does not know. Use a newer version of the server.");
            if (migration.Checksum != checksum)
                throw new InvalidOperationException(
                    $"Migration {version} ({migration.Name}) was changed after it was applied. Add a new migration instead.");
        }

        var newlyApplied = new List<int>();
        foreach (var migration in migrations.Where(m => !applied.ContainsKey(m.Version)))
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, migration.Sql);

            using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO schema_migrations (version, name, checksum, applied_at) VALUES ($version, $name, $checksum, $appliedAt);";
            record.Parameters.AddWithValue("$version", migration.Version);
            record.Parameters.AddWithValue("$name", migration.Name);
            record.Parameters.AddWithValue("$checksum", migration.Checksum);
            record.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            record.ExecuteNonQuery();

            transaction.Commit();
            newlyApplied.Add(migration.Version);
        }

        return newlyApplied;
    }

    private static void CheckSequence(IReadOnlyList<Migration> migrations)
    {
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
                throw new InvalidOperationException(
                    $"Migrations must be numbered 1, 2, 3, … without gaps or duplicates; found {migrations[i].Version} at position {i + 1}.");
        }
    }

    private static Dictionary<int, string> ReadApplied(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, checksum FROM schema_migrations;";
        using var reader = command.ExecuteReader();
        var applied = new Dictionary<int, string>();
        while (reader.Read())
            applied[reader.GetInt32(0)] = reader.GetString(1);
        return applied;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

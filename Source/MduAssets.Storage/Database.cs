using Microsoft.Data.Sqlite;

namespace MduAssets.Storage;

/// <summary>Opens connections to the SQLite database of an instance.</summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = 30,
        }.ToString();
    }

    public Database(DataDirectory dataDirectory)
        : this(dataDirectory.DatabasePath)
    {
    }

    /// <summary>Opens a connection in WAL mode with a busy timeout, so readers never block the writer.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        // SQLite's own lower() and NOCASE fold only A to Z; search compares text lower-cased by .NET instead.
        connection.CreateFunction("unicode_lower", (string? value) => value?.ToLowerInvariant(), isDeterministic: true);
        return connection;
    }
}

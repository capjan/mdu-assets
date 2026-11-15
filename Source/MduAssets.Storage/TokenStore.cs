using System.Security.Cryptography;
using System.Text;

namespace MduAssets.Storage;

public sealed record ApiToken(
    long Id, long UserId, string Name, string Prefix, long? ProjectId, string? ProjectSlug, string Scope,
    DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset CreatedAt);

/// <summary>A token found by its secret, with what the server needs to authenticate the request.</summary>
public sealed record TokenOwner(ApiToken Token, User User);

/// <summary>Personal API tokens (AUTH-06, AUTH-07). Only a hash of the secret is stored; the secret is shown once.</summary>
public sealed class TokenStore(Database database)
{
    public const string Marker = "mdua_";
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// A new secret: <c>mdua_</c> and 40 random base62 characters (about 238 bits). That much randomness makes a plain SHA-256
    /// hash enough; a slow password hash is only needed for secrets people choose.
    /// </summary>
    public static string NewSecret() => Marker + RandomNumberGenerator.GetString(Alphabet, 40);

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public ApiToken Create(long userId, string name, string secret, long? projectId, string scope, DateTimeOffset? expiresAt)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO api_tokens (user_id, name, prefix, hash, project_id, scope, expires_at, created_at)
            VALUES ($user, $name, $prefix, $hash, $project, $scope, $expires, $now) RETURNING id;
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$prefix", secret[..(Marker.Length + 6)]);
        command.Parameters.AddWithValue("$hash", Hash(secret));
        command.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$expires", expiresAt is { } e ? Clock.Format(e) : DBNull.Value);
        command.Parameters.AddWithValue("$now", Clock.Now());
        var id = (long)command.ExecuteScalar()!;
        return List(userId).Single(t => t.Id == id);
    }

    /// <summary>The tokens of a user, newest first.</summary>
    public IReadOnlyList<ApiToken> List(long userId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Select + " WHERE t.user_id = $user ORDER BY t.created_at DESC, t.id DESC;";
        command.Parameters.AddWithValue("$user", userId);
        using var reader = command.ExecuteReader();
        var tokens = new List<ApiToken>();
        while (reader.Read())
            tokens.Add(Read(reader));
        return tokens;
    }

    public bool Delete(long userId, long id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM api_tokens WHERE id = $id AND user_id = $user;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$user", userId);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Finds the token and its user for a secret, or <c>null</c>. Expiry and locks are checked by the caller.</summary>
    public TokenOwner? Find(string secret)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Select.Replace("FROM api_tokens t", "FROM api_tokens t JOIN users u ON u.id = t.user_id", StringComparison.Ordinal)
            .Replace("SELECT ", "SELECT u.name, u.password_hash, u.is_admin, u.is_locked, u.must_change_password, u.created_at, ", StringComparison.Ordinal) +
            " WHERE t.hash = $hash;";
        command.Parameters.AddWithValue("$hash", Hash(secret));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        var token = Read(reader, offset: 6);
        var user = new User(token.UserId, reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3),
            reader.GetBoolean(4), Clock.Parse(reader.GetString(5)));
        return new TokenOwner(token, user);
    }

    /// <summary>Notes the use of a token, at most once a minute, so authentication rarely writes.</summary>
    public void Touch(long id, DateTimeOffset now)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE api_tokens SET last_used_at = $now WHERE id = $id AND (last_used_at IS NULL OR last_used_at < $before);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$now", Clock.Format(now));
        command.Parameters.AddWithValue("$before", Clock.Format(now.AddMinutes(-1)));
        command.ExecuteNonQuery();
    }

    private const string Select = """
        SELECT t.id, t.user_id, t.name, t.prefix, t.project_id, p.slug, t.scope, t.expires_at, t.last_used_at, t.created_at
        FROM api_tokens t LEFT JOIN projects p ON p.id = t.project_id
        """;

    private static ApiToken Read(Microsoft.Data.Sqlite.SqliteDataReader reader, int offset = 0) =>
        new(reader.GetInt64(offset), reader.GetInt64(offset + 1), reader.GetString(offset + 2), reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : reader.GetInt64(offset + 4),
            reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5),
            reader.GetString(offset + 6),
            reader.IsDBNull(offset + 7) ? null : Clock.Parse(reader.GetString(offset + 7)),
            reader.IsDBNull(offset + 8) ? null : Clock.Parse(reader.GetString(offset + 8)),
            Clock.Parse(reader.GetString(offset + 9)));
}

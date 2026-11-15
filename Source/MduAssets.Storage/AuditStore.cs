namespace MduAssets.Storage;

public sealed record AuditEntry(
    long Id, DateTimeOffset At, string? UserName, string? ProjectSlug, string Action, string? Target, string? Details, string? Ip);

public sealed record NewAuditEntry(long? UserId, string? UserName, long? ProjectId, string? ProjectSlug, string Action, string? Target, string? Details, string? Ip);

/// <summary>The audit log (OPS-03): who changed what, when and from where.</summary>
public sealed class AuditStore(Database database)
{
    public void Write(NewAuditEntry entry)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audit_log (at, user_id, user_name, project_id, project_slug, action, target, details, ip)
            VALUES ($at, $user, $userName, $project, $slug, $action, $target, $details, $ip);
            """;
        command.Parameters.AddWithValue("$at", Clock.Now());
        command.Parameters.AddWithValue("$user", (object?)entry.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$userName", (object?)entry.UserName ?? DBNull.Value);
        command.Parameters.AddWithValue("$project", (object?)entry.ProjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$slug", (object?)entry.ProjectSlug ?? DBNull.Value);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$target", (object?)entry.Target ?? DBNull.Value);
        command.Parameters.AddWithValue("$details", (object?)entry.Details ?? DBNull.Value);
        command.Parameters.AddWithValue("$ip", (object?)entry.Ip ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>Entries newest first, all or of one project, older than <paramref name="before"/> when given (paging by id).</summary>
    public IReadOnlyList<AuditEntry> List(long? projectId, int limit, long? before = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, at, user_name, project_slug, action, target, details, ip FROM audit_log
            WHERE ($project IS NULL OR project_id = $project) AND ($before IS NULL OR id < $before)
            ORDER BY id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$before", (object?)before ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var entries = new List<AuditEntry>();
        while (reader.Read())
            entries.Add(new AuditEntry(reader.GetInt64(0), Clock.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        return entries;
    }

    /// <summary>Deletes entries older than <paramref name="cutoff"/>; returns how many.</summary>
    public int DeleteBefore(DateTimeOffset cutoff)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM audit_log WHERE at < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", Clock.Format(cutoff));
        return command.ExecuteNonQuery();
    }
}

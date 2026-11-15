namespace MduAssets.Storage;

/// <summary>Requests counted for one asset since the last write.</summary>
public sealed record HitBatch(string AssetId, long Count, DateTimeOffset LastHit);

/// <summary>Requests counted for one referring page since the last write.</summary>
public sealed record ReferrerBatch(string AssetId, string Origin, string Path, long Count, DateTimeOffset LastHit);

public sealed record Referrer(string Origin, string Path, long Count, DateTimeOffset LastHit);

/// <summary>Usage statistics (STA-01 – STA-06), written in batches by the counter in the server.</summary>
public sealed class StatsStore(Database database)
{
    public const int MaxReferrersPerAsset = 100;

    /// <summary>Adds counted requests and referring pages in one transaction; each asset keeps its 100 most recent referrers.</summary>
    public void Write(IReadOnlyCollection<HitBatch> hits, IReadOnlyCollection<ReferrerBatch> referrers)
    {
        if (hits.Count == 0 && referrers.Count == 0)
            return;
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var hit in hits)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Assets deleted for good in the meantime are skipped.
            command.CommandText = """
                INSERT INTO asset_stats (asset_id, hit_count, last_hit_at)
                SELECT $id, $count, $last WHERE EXISTS (SELECT 1 FROM assets WHERE id = $id)
                ON CONFLICT (asset_id) DO UPDATE SET hit_count = hit_count + excluded.hit_count,
                    last_hit_at = max(last_hit_at, excluded.last_hit_at);
                """;
            command.Parameters.AddWithValue("$id", hit.AssetId);
            command.Parameters.AddWithValue("$count", hit.Count);
            command.Parameters.AddWithValue("$last", Clock.Format(hit.LastHit));
            command.ExecuteNonQuery();
        }

        foreach (var referrer in referrers)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_referrers (asset_id, origin, path, hit_count, last_hit_at)
                SELECT $id, $origin, $path, $count, $last WHERE EXISTS (SELECT 1 FROM assets WHERE id = $id)
                ON CONFLICT (asset_id, origin, path) DO UPDATE SET hit_count = hit_count + excluded.hit_count,
                    last_hit_at = max(last_hit_at, excluded.last_hit_at);
                """;
            command.Parameters.AddWithValue("$id", referrer.AssetId);
            command.Parameters.AddWithValue("$origin", referrer.Origin);
            command.Parameters.AddWithValue("$path", referrer.Path);
            command.Parameters.AddWithValue("$count", referrer.Count);
            command.Parameters.AddWithValue("$last", Clock.Format(referrer.LastHit));
            command.ExecuteNonQuery();
        }

        foreach (var assetId in referrers.Select(r => r.AssetId).Distinct(StringComparer.Ordinal))
        {
            using var trim = connection.CreateCommand();
            trim.Transaction = transaction;
            trim.CommandText = """
                DELETE FROM asset_referrers WHERE asset_id = $id AND rowid NOT IN
                    (SELECT rowid FROM asset_referrers WHERE asset_id = $id ORDER BY last_hit_at DESC, hit_count DESC LIMIT $max);
                """;
            trim.Parameters.AddWithValue("$id", assetId);
            trim.Parameters.AddWithValue("$max", MaxReferrersPerAsset);
            trim.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>The referring pages of an asset, most requests first.</summary>
    public IReadOnlyList<Referrer> Referrers(string assetId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT origin, path, hit_count, last_hit_at FROM asset_referrers WHERE asset_id = $id
            ORDER BY hit_count DESC, last_hit_at DESC;
            """;
        command.Parameters.AddWithValue("$id", assetId);
        using var reader = command.ExecuteReader();
        var referrers = new List<Referrer>();
        while (reader.Read())
            referrers.Add(new Referrer(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), Clock.Parse(reader.GetString(3))));
        return referrers;
    }
}

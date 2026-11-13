using System.Security.Cryptography;
using System.Text;

namespace MduAssets.Storage;

/// <summary>One numbered schema change. Released migrations are never edited; a new one follows instead.</summary>
public sealed record Migration(int Version, string Name, string Sql)
{
    /// <summary>SHA-256 of the SQL, stored when applied, so an edited migration is detected on start.</summary>
    public string Checksum { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Sql.ReplaceLineEndings("\n"))));

    /// <summary>
    /// Reads the migrations embedded in this assembly. File names follow <c>NNNN_name.sql</c>,
    /// for example <c>0001_users.sql</c>.
    /// </summary>
    public static IReadOnlyList<Migration> Embedded()
    {
        var assembly = typeof(Migration).Assembly;
        const string prefix = "Migrations.";
        var migrations = new List<Migration>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(prefix, StringComparison.Ordinal) || !resource.EndsWith(".sql", StringComparison.Ordinal))
                continue;

            var fileName = resource[prefix.Length..^".sql".Length];
            var separator = fileName.IndexOf('_');
            if (separator <= 0 || !int.TryParse(fileName[..separator], out var version))
                throw new InvalidOperationException($"Migration '{resource}' must be named NNNN_name.sql.");

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            migrations.Add(new Migration(version, fileName[(separator + 1)..], reader.ReadToEnd()));
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }
}

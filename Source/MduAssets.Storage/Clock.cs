using System.Globalization;

namespace MduAssets.Storage;

internal static class Clock
{
    /// <summary>UTC timestamp in the format SQLite's <c>strftime('%Y-%m-%dT%H:%M:%fZ')</c> writes.</summary>
    public static string Now() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

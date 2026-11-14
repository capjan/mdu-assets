using System.Globalization;

namespace MduAssets.Storage;

internal static class Clock
{
    private const string Pattern = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>UTC timestamp in the format SQLite's <c>strftime('%Y-%m-%dT%H:%M:%fZ')</c> writes.</summary>
    public static string Now() => Format(DateTimeOffset.UtcNow);

    /// <summary>The stored form of a time, so stored values compare as text in time order.</summary>
    public static string Format(DateTimeOffset value) => value.UtcDateTime.ToString(Pattern, CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}

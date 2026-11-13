using System.Security.Cryptography;

namespace MduAssets.Storage;

/// <summary>
/// Public asset ids: 10 characters base62 from a cryptographic random generator, about 59 bits.
/// See ADR-0003.
/// </summary>
public static class AssetId
{
    public const int Length = 10;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Checks the form only; whether the asset exists is up to the store.</summary>
    public static bool IsValid(string? value) =>
        value is { Length: Length } && value.All(char.IsAsciiLetterOrDigit);
}

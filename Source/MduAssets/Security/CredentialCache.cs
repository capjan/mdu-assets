using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MduAssets.Security;

/// <summary>
/// Remembers recently verified passwords for a few minutes. Basic Auth sends the password with every request,
/// and PBKDF2 with 600,000 iterations would otherwise cost a few hundred milliseconds per request.
/// Entries are keyed by a hash of user, stored hash and password, so a changed password never matches an old entry.
/// </summary>
public sealed class CredentialCache(TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int MaxEntries = 1000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _verified = new(StringComparer.Ordinal);

    public bool Verify(long userId, string storedHash, string password)
    {
        var key = Key(userId, storedHash, password);
        var now = time.GetUtcNow();
        if (_verified.TryGetValue(key, out var expires) && expires > now)
            return true;

        if (!PasswordHasher.Verify(password, storedHash))
            return false;

        if (_verified.Count >= MaxEntries)
            _verified.Clear();
        _verified[key] = now + Lifetime;
        return true;
    }

    private static string Key(long userId, string storedHash, string password) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{storedHash}\n{password}")));
}

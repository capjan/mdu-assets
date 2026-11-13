using MduAssets.Storage;

namespace MduAssets.Security;

/// <summary>Creates the first administrator when the instance has no users yet (AUTH-03).</summary>
public static class FirstAdmin
{
    /// <returns>The name of the created admin, or <c>null</c> when users already exist.</returns>
    public static string? EnsureExists(UserStore users, string? name, string? password)
    {
        if (users.Count() > 0)
            return null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(password))
            throw new StartupException(
                "The instance has no users yet. Set MDU_ASSETS_ADMIN_USER and MDU_ASSETS_ADMIN_PASSWORD " +
                "to create the first administrator; both are ignored once users exist.");

        if (password.Length < PasswordHasher.MinimumLength)
            throw new StartupException($"MDU_ASSETS_ADMIN_PASSWORD must have at least {PasswordHasher.MinimumLength} characters.");

        users.Create(name.Trim(), PasswordHasher.Hash(password), isAdmin: true);
        return name.Trim();
    }
}

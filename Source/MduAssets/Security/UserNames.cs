using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MduAssets.Security;

/// <summary>User names work in Basic Auth, URLs and logs: no colon, no spaces.</summary>
public static partial class UserNames
{
    public const string Rule = "A user name has 1 to 64 letters, digits, dots, hyphens, underscores or @.";

    public static bool IsValid(string name) => Pattern().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9._@-]{1,64}$")]
    private static partial Regex Pattern();
}

/// <summary>Start passwords that admins hand out; the user replaces them at the first sign-in (AUTH-04, AUTH-05).</summary>
public static class StartPasswords
{
    // No 0/O, 1/l/I, so the password survives being read aloud or typed from a screenshot.
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New() => RandomNumberGenerator.GetString(Alphabet, 16);
}

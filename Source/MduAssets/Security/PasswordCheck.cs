using MduAssets.Storage;

namespace MduAssets.Security;

/// <summary>The outcome of checking a user name and password.</summary>
public enum PasswordCheckResult
{
    Valid,
    Invalid,
    Throttled,
}

/// <summary>
/// Checks a user name and password for the sign-in page and for HTTP Basic credentials of tools (AUTH-01, AUTH-08): too many
/// failed tries per address or name are refused without checking, and failures and sign-ins go to the audit log.
/// </summary>
public sealed class PasswordCheck(UserStore users, CredentialCache credentials, SignInThrottle throttle, AuditStore audit)
{
    /// <param name="everyRequest">
    /// <c>true</c> for Basic credentials, which come with every request: only a check that was not answered from the cache
    /// counts as a sign-in in the audit log.
    /// </param>
    public PasswordCheckResult Check(string name, string password, string? ip, bool everyRequest, out User? user)
    {
        user = null;
        if (throttle.IsBlocked(ip, name))
            return PasswordCheckResult.Throttled;

        var found = users.Find(name);
        var checkedNow = true;
        if (found is null || found.IsLocked || !credentials.Verify(found.Id, found.PasswordHash, password, out checkedNow))
        {
            throttle.Failed(ip, name);
            audit.Write(new NewAuditEntry(found?.Id, name.Length > 100 ? name[..100] : name, null, null, "sign-in.failed", null,
                found is null ? "unknown user" : found.IsLocked ? "locked" : "wrong password", ip));
            return PasswordCheckResult.Invalid;
        }

        if (checkedNow || !everyRequest)
            audit.Write(new NewAuditEntry(found.Id, found.Name, null, null, "sign-in", null, null, ip));
        user = found;
        return PasswordCheckResult.Valid;
    }
}

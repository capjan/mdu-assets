using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using MduAssets.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MduAssets.Security;

/// <summary>
/// HTTP Basic authentication against the users table (AUTH-01). Passwords are accepted only over HTTPS,
/// directly or through a trusted reverse proxy, unless <see cref="ServerOptions.AllowHttp"/> is set.
/// </summary>
public sealed class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    UserStore users,
    TokenStore tokens,
    CredentialCache credentials,
    SignInThrottle throttle,
    AuditStore audit,
    ServerSettings settings,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Basic";

    public const string AdminRole = "admin";

    private const string ThrottledKey = "mdu:sign-in-throttled";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header))
            return Task.FromResult(AuthenticateResult.NoResult());
        var bearer = "Bearer".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase);
        if (!bearer && !SchemeName.Equals(header.Scheme, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.IsHttps && !settings.Options.AllowHttp)
            return Task.FromResult(AuthenticateResult.Fail("Passwords and tokens are only accepted over HTTPS."));
        if (bearer)
            return Task.FromResult(AuthenticateToken(header.Parameter ?? string.Empty));

        string name, password;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter ?? string.Empty));
            var separator = decoded.IndexOf(':');
            if (separator <= 0)
                return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials."));
            name = decoded[..separator];
            password = decoded[(separator + 1)..];
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials."));
        }

        // Too many failed tries from this address or for this name: refuse without checking the password (AUTH-08).
        var ip = Context.Connection.RemoteIpAddress?.ToString();
        if (throttle.IsBlocked(ip, name))
        {
            Context.Items[ThrottledKey] = true;
            return Task.FromResult(AuthenticateResult.Fail("Too many failed sign-ins; try again later."));
        }

        var user = users.Find(name);
        var checkedNow = false;
        if (user is null || user.IsLocked || !credentials.Verify(user.Id, user.PasswordHash, password, out checkedNow))
        {
            throttle.Failed(ip, name);
            audit.Write(new NewAuditEntry(user?.Id, name.Length > 100 ? name[..100] : name, null, null, "sign-in.failed", null,
                user is null ? "unknown user" : user.IsLocked ? "locked" : "wrong password", ip));
            return Task.FromResult(AuthenticateResult.Fail("Invalid user name or password."));
        }

        // Basic Auth sends the password with every request; a sign-in is when it was checked rather than taken from the cache.
        if (checkedNow)
            audit.Write(new NewAuditEntry(user.Id, user.Name, null, null, "sign-in", null, null, ip));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Name),
        };
        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        if (user.MustChangePassword)
            claims.Add(new Claim(Caller.MustChangePasswordClaim, "true"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    /// <summary>
    /// A personal API token (AUTH-07). It acts for its user with at most the user's rights: limited to its project and, for
    /// read tokens, to reading. Tokens of locked users and expired tokens are rejected on the next request.
    /// </summary>
    private AuthenticateResult AuthenticateToken(string secret)
    {
        var ip = Context.Connection.RemoteIpAddress?.ToString();
        if (throttle.IsBlocked(ip, null))
        {
            Context.Items[ThrottledKey] = true;
            return AuthenticateResult.Fail("Too many failed sign-ins; try again later.");
        }

        if (!secret.StartsWith(TokenStore.Marker, StringComparison.Ordinal) || tokens.Find(secret) is not { } owner)
        {
            throttle.Failed(ip, null);
            return AuthenticateResult.Fail("Unknown API token.");
        }
        var (token, user) = (owner.Token, owner.User);
        var now = time.GetUtcNow();
        if (user.IsLocked)
            return AuthenticateResult.Fail("The user of this token is locked.");
        if (token.ExpiresAt is { } expires && expires <= now)
            return AuthenticateResult.Fail("The API token has expired.");
        tokens.Touch(token.Id, now);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Name),
            new(Caller.TokenClaim, token.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(Caller.TokenScopeClaim, token.Scope),
        };
        if (token.ProjectId is { } project)
            claims.Add(new Claim(Caller.TokenProjectClaim, project.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        // An admin's token reaches every project only when it is neither limited to a project nor to reading.
        if (user.IsAdmin && token.Scope == "write" && token.ProjectId is null)
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        if (user.MustChangePassword)
            claims.Add(new Claim(Caller.MustChangePasswordClaim, "true"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // A blocked sign-in gets 429 without a password prompt, so the browser does not invite the next guess.
        if (Context.Items.ContainsKey(ThrottledKey))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = ((int)throttle.Window.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        }

        Response.Headers.WWWAuthenticate = "Basic realm=\"MDU Asset Server\", charset=\"UTF-8\"";
        return base.HandleChallengeAsync(properties);
    }
}

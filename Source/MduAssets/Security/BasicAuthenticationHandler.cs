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
    CredentialCache credentials,
    ServerSettings settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Basic";

    public const string AdminRole = "admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
            !SchemeName.Equals(header.Scheme, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.IsHttps && !settings.Options.AllowHttp)
            return Task.FromResult(AuthenticateResult.Fail("Passwords are only accepted over HTTPS."));

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

        var user = users.Find(name);
        if (user is null || user.IsLocked || !credentials.Verify(user.Id, user.PasswordHash, password))
            return Task.FromResult(AuthenticateResult.Fail("Invalid user name or password."));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Name),
        };
        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"MDU Asset Server\", charset=\"UTF-8\"";
        return base.HandleChallengeAsync(properties);
    }
}

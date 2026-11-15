using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MduAssets.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Security;

/// <summary>Name and password from the sign-in page; <c>Remember</c> keeps the session across browser restarts.</summary>
public sealed record SignInRequest(string? Name, string? Password, bool Remember = false);

/// <summary>
/// Sign-in with the server's own page (AUTH-01, ADR-0021): a session cookie that only the server can read (data protection,
/// keys in the data directory), HttpOnly, SameSite=Strict and Secure. Every request checks the user again: a locked user, a
/// deleted user or a changed password ends all of that user's sessions at once. Requests with an Authorization header use
/// Basic credentials or API tokens instead (<see cref="BasicAuthenticationHandler"/>).
/// </summary>
public static class Sessions
{
    public const string SchemeName = "Session";

    /// <summary>Picks the session cookie or the Authorization header for each request.</summary>
    public const string SelectorScheme = "SessionOrHeader";

    private const string StampClaim = "mdu:stamp";

    /// <summary>How long a remembered session lasts without use; each use extends it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public static AuthenticationBuilder AddSessions(this AuthenticationBuilder builder, ServerSettings settings) =>
        builder
            .AddPolicyScheme(SelectorScheme, null, options => options.ForwardDefaultSelector = context =>
                UsesHeader(context.Request) ? BasicAuthenticationHandler.SchemeName : SchemeName)
            .AddCookie(SchemeName, options =>
            {
                // The __Host- prefix makes browsers accept the cookie only from this host over HTTPS, without a domain.
                options.Cookie.Name = settings.Options.AllowHttp ? "mdu-assets-session" : "__Host-mdu-assets-session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = settings.Options.AllowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = Lifetime;
                options.SlidingExpiration = true;
                // The API answers with a status; pages send the browser to the sign-in page themselves.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = Validate;
            });

    /// <summary>
    /// Whether a request signs in with its Authorization header. Browsers keep Basic credentials from the time before the
    /// sign-in page and send them on their own, which would make signing out impossible; they always send
    /// <c>Sec-Fetch-Site</c>, tools such as curl do not. So Basic credentials count only from tools, API tokens from anybody.
    /// </summary>
    private static bool UsesHeader(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.Length == 0)
            return false;
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || request.Headers["Sec-Fetch-Site"].Count == 0;
    }

    /// <summary>A fingerprint of the stored password hash: a new password makes older sessions invalid.</summary>
    private static string Stamp(User user) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)))[..16];

    private static ClaimsPrincipal Principal(User user)
    {
        var claims = BasicAuthenticationHandler.Claims(user);
        claims.Add(new Claim(StampClaim, Stamp(user)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
    }

    /// <summary>Reads the user again for every request, so admin rights, locks and password changes apply at once.</summary>
    private static async Task Validate(CookieValidatePrincipalContext context)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserStore>();
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) ? users.FindById(userId) : null;
        if (user is null || user.IsLocked || context.Principal?.FindFirstValue(StampClaim) != Stamp(user))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(SchemeName);
            return;
        }

        context.ReplacePrincipal(Principal(user));
    }

    /// <summary>Starts a session for a user, also again after a password change, so the own session stays valid.</summary>
    public static Task SignInAsync(HttpContext context, User user, bool remember) =>
        context.SignInAsync(SchemeName, Principal(user), new AuthenticationProperties { IsPersistent = remember, AllowRefresh = true });

    /// <summary><c>POST /api/v1/session</c> and <c>DELETE /api/v1/session</c> on the UI host, without being signed in.</summary>
    public static void Map(IEndpointRouteBuilder app, ServerSettings settings)
    {
        app.MapPost("/api/v1/session", SignIn).RequireHost(settings.UiHost).AllowAnonymous().AllowWithStartPassword();
        app.MapDelete("/api/v1/session", SignOut).RequireHost(settings.UiHost).AllowAnonymous().AllowWithStartPassword();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SignIn(
        SignInRequest request, HttpContext context, PasswordCheck passwords, SignInThrottle throttle, ServerSettings settings)
    {
        if (!context.Request.IsHttps && !settings.Options.AllowHttp)
            return TypedResults.Problem("Passwords are only accepted over HTTPS.", statusCode: StatusCodes.Status400BadRequest, title: "HTTPS required");
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrEmpty(request.Password))
            return TypedResults.Problem("Enter your user name and password.", statusCode: StatusCodes.Status400BadRequest, title: "Missing sign-in");

        var result = passwords.Check(request.Name.Trim(), request.Password, context.Connection.RemoteIpAddress?.ToString(), everyRequest: false, out var user);
        if (result == PasswordCheckResult.Throttled)
        {
            context.Response.Headers.RetryAfter = ((int)throttle.Window.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return TypedResults.Problem($"Too many failed sign-ins. Try again in {(int)throttle.Window.TotalMinutes} minutes.",
                statusCode: StatusCodes.Status429TooManyRequests, title: "Too many tries");
        }
        if (user is null)
            return TypedResults.Problem("The user name or the password is wrong.", statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed");

        await SignInAsync(context, user, request.Remember);
        return TypedResults.NoContent();
    }

    // The cancellation token keeps this from being read as a plain request delegate, which would drop the result.
    private static async Task<NoContent> SignOut(HttpContext context, CancellationToken cancellationToken)
    {
        await context.SignOutAsync(SchemeName);
        return TypedResults.NoContent();
    }
}

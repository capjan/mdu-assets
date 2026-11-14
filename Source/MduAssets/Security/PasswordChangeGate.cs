namespace MduAssets.Security;

/// <summary>
/// Users with a start password may only read who they are and change the password (AUTH-05);
/// every other API request answers 403 with the type <see cref="ProblemType"/>, which the web interface recognizes.
/// </summary>
public static class PasswordChangeGate
{
    public const string ProblemType = "urn:mdu-assets:problem:password-change-required";

    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api/v1") || context.User.Identity?.IsAuthenticated != true ||
                !context.User.MustChangePassword() ||
                path.Equals("/api/v1/me", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/v1/me/password", StringComparison.OrdinalIgnoreCase))
                return next(context);

            return TypedResults.Problem(
                "Change your start password first: POST /api/v1/me/password.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Password change required",
                type: ProblemType).ExecuteAsync(context);
        });
}

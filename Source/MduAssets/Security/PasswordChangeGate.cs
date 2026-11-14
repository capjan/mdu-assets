namespace MduAssets.Security;

/// <summary>Marks an endpoint that users with a start password may call; see <see cref="PasswordChangeGate"/>.</summary>
public sealed class AllowedWithStartPassword
{
    public static readonly AllowedWithStartPassword Instance = new();

    private AllowedWithStartPassword()
    {
    }
}

/// <summary>
/// Users with a start password may only call endpoints marked with <see cref="AllowedWithStartPassword"/>, which are
/// reading who they are and changing the password (AUTH-05). Every other API request answers 403 with the type
/// <see cref="ProblemType"/>, which the web interface recognizes.
/// </summary>
public static class PasswordChangeGate
{
    public const string ProblemType = "urn:mdu-assets:problem:password-change-required";

    public static TBuilder AllowWithStartPassword<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(AllowedWithStartPassword.Instance);

    /// <summary>Runs after routing and authentication, so the matched endpoint and the user are known.</summary>
    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") || context.User.Identity?.IsAuthenticated != true ||
                !context.User.MustChangePassword() ||
                context.GetEndpoint()?.Metadata.GetMetadata<AllowedWithStartPassword>() is not null)
                return next(context);

            return TypedResults.Problem(
                "Change your start password first: POST /api/v1/me/password.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Password change required",
                type: ProblemType).ExecuteAsync(context);
        });
}

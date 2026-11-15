namespace MduAssets.Security;

/// <summary>
/// Marks endpoints that a request with an API token may not call: managing tokens, changing the password and the admin area.
/// A leaked token can therefore never create more tokens or take over the account (AUTH-07).
/// </summary>
public sealed class PasswordOnly
{
    public static readonly PasswordOnly Instance = new();

    private PasswordOnly()
    {
    }
}

public static class PasswordOnlyGate
{
    public static TBuilder RequirePassword<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(PasswordOnly.Instance);

    /// <summary>Runs after routing and authentication, so the endpoint and the kind of credentials are known.</summary>
    public static IApplicationBuilder UsePasswordOnlyGate(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (!context.User.UsesToken() || context.GetEndpoint()?.Metadata.GetMetadata<PasswordOnly>() is null)
                return next(context);
            return TypedResults.Problem(
                "API tokens cannot manage tokens, change passwords or use the admin area. Sign in with your password.",
                statusCode: StatusCodes.Status403Forbidden, title: "Password needed").ExecuteAsync(context);
        });
}

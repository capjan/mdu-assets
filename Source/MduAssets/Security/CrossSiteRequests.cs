namespace MduAssets.Security;

/// <summary>
/// Rejects changing requests that a browser sends on behalf of another site. Browsers attach cached Basic Auth
/// credentials to any request to the UI host, so without this check a foreign page could upload or edit images
/// with a plain form post. Browsers mark such requests with <c>Sec-Fetch-Site</c> and <c>Origin</c>; tools such as
/// curl send neither and are not affected.
/// </summary>
public static class CrossSiteRequests
{
    public static IApplicationBuilder UseCrossSiteRequestProtection(this IApplicationBuilder app, ServerSettings settings)
    {
        var uiOrigin = settings.UiBaseUrl.GetLeftPart(UriPartial.Authority);
        return app.Use((context, next) =>
        {
            var request = context.Request;
            if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
                return next(context);

            var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
            var origin = request.Headers.Origin.ToString();
            var foreign = (fetchSite.Length > 0 && fetchSite is not ("same-origin" or "none")) ||
                          (origin.Length > 0 && !string.Equals(origin, uiOrigin, StringComparison.OrdinalIgnoreCase));
            if (!foreign)
                return next(context);

            return TypedResults.Problem(
                "Changes are only accepted from the web interface itself or from tools outside a browser.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Cross-site request").ExecuteAsync(context);
        });
    }
}

using MduAssets.Security;
using MduAssets.Storage;

namespace MduAssets.Maintenance;

/// <summary>
/// Writes an audit entry for every successful change made through the API (OPS-03): uploads, versions, tag changes, renames,
/// metadata, trash and restore, members, tokens, users and settings. Recording all changes in one place means no endpoint can
/// forget it; sign-ins are written by the authentication handler.
/// </summary>
public static class AuditLog
{
    // Readable names for the routes; others are logged as "<method> <route>".
    private static readonly Dictionary<string, string> Actions = new(StringComparer.Ordinal)
    {
        ["POST /api/v1/projects/{project}/assets"] = "asset.upload",
        ["PATCH /api/v1/assets/{id}"] = "asset.update",
        ["PUT /api/v1/assets/{id}/pair"] = "asset.pair",
        ["DELETE /api/v1/assets/{id}/pair"] = "asset.unpair",
        ["POST /api/v1/assets/{id}/versions"] = "asset.version",
        ["POST /api/v1/assets/{id}/versions/{number:int}/restore"] = "asset.version.restore",
        ["DELETE /api/v1/assets/{id}/versions/{number:int}"] = "asset.version.delete",
        ["DELETE /api/v1/assets/{id}"] = "asset.trash",
        ["POST /api/v1/assets/{id}/restore"] = "asset.restore",
        ["DELETE /api/v1/projects/{project}/trash"] = "trash.empty",
        ["DELETE /api/v1/projects/{project}/trash/{id}"] = "asset.purge",
        ["POST /api/v1/projects/{project}/assets/tags"] = "asset.tags",
        ["PATCH /api/v1/projects/{project}/tags"] = "tag.rename",
        ["DELETE /api/v1/projects/{project}/tags"] = "tag.delete",
        ["POST /api/v1/projects"] = "project.create",
        ["POST /api/v1/projects/import"] = "project.import",
        ["PATCH /api/v1/projects/{project}"] = "project.update",
        ["DELETE /api/v1/projects/{project}"] = "project.delete",
        ["PUT /api/v1/projects/{project}/members/{userName}"] = "member.set",
        ["DELETE /api/v1/projects/{project}/members/{userName}"] = "member.remove",
        ["POST /api/v1/me/password"] = "password.change",
        ["POST /api/v1/me/tokens"] = "token.create",
        ["DELETE /api/v1/me/tokens/{id:long}"] = "token.delete",
        ["POST /api/v1/users"] = "user.create",
        ["PATCH /api/v1/users/{name}"] = "user.update",
        ["POST /api/v1/users/{name}/password-reset"] = "user.password-reset",
        ["PATCH /api/v1/settings"] = "settings.update",
    };

    public static IApplicationBuilder UseAuditLog(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next(context);

            var request = context.Request;
            if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method) ||
                !request.Path.StartsWithSegments("/api/v1") || context.Response.StatusCode is < 200 or >= 300 ||
                context.User.Identity?.IsAuthenticated != true || context.GetEndpoint() is not RouteEndpoint endpoint)
                return;

            var route = $"{request.Method} {endpoint.RoutePattern.RawText}";
            var values = request.RouteValues;
            string? Value(string key) => values.TryGetValue(key, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;

            var services = context.RequestServices;
            long? projectId = null;
            var projectSlug = Value("project");
            if (projectSlug is not null)
                projectId = services.GetRequiredService<ProjectStore>().Find(projectSlug)?.Id;
            else if (Value("id") is { } assetId && endpoint.RoutePattern.RawText?.StartsWith("/api/v1/assets", StringComparison.Ordinal) == true &&
                     services.GetRequiredService<AssetStore>().FindCurrent(assetId) is { } asset)
                (projectId, projectSlug) = (asset.ProjectId, asset.ProjectSlug);

            // Created things are named by their new link: an asset id, or the last part of an API path.
            var location = context.Response.Headers.Location.ToString();
            var target = Value("id") ?? Value("userName") ?? Value("name") ??
                (location.Length > 0 ? MduAssets.Api.PublicLinkIds.Extract(location) ?? location.TrimEnd('/').Split('/')[^1] : null);
            var details = new List<string>();
            if (Value("number") is { } number)
                details.Add($"version {number}");
            if (location.Length > 0)
                details.Add(location);
            if (request.Query["name"].ToString() is { Length: > 0 } name)
                details.Add($"name {(name.Length > 200 ? name[..200] : name)}");

            var user = context.User;
            services.GetRequiredService<AuditStore>().Write(new NewAuditEntry(
                user.Id(), user.Identity?.Name, projectId, projectSlug, Actions.GetValueOrDefault(route, route), target,
                details.Count > 0 ? string.Join("; ", details) : null, context.Connection.RemoteIpAddress?.ToString()));
        });
}

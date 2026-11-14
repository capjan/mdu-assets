using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Security;

/// <summary>Who is calling, read from the claims the authentication handler sets.</summary>
public static class Caller
{
    public const string MustChangePasswordClaim = "mdu:must-change-password";

    public static long Id(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(BasicAuthenticationHandler.AdminRole);

    public static bool MustChangePassword(this ClaimsPrincipal user) => user.HasClaim(MustChangePasswordClaim, "true");
}

/// <summary>
/// The one place that decides what a caller may do in a project (section 4.2 of the requirements).
/// Admins act as owners everywhere. Projects a caller cannot see answer 404, so their names do not leak;
/// a visible project with too little rights answers 403.
/// </summary>
public sealed class ProjectAccess(ProjectStore projects)
{
    /// <summary>The caller's role in a project: their membership, or owner for admins, or <c>null</c>.</summary>
    public ProjectRole? RoleOf(ClaimsPrincipal user, long projectId) =>
        user.IsAdmin() ? ProjectRole.Owner : projects.RoleOf(projectId, user.Id());

    /// <summary>Finds a project by slug and checks that the caller has at least <paramref name="needed"/>.</summary>
    public bool TryGet(ClaimsPrincipal user, string slug, ProjectRole needed,
        [NotNullWhen(true)] out Project? project, [NotNullWhen(false)] out ProblemHttpResult? problem)
    {
        project = projects.Find(slug);
        var role = project is null ? null : RoleOf(user, project.Id);
        problem = Check(role, needed, () => UnknownProject(slug));
        if (problem is not null)
            project = null;
        return problem is null;
    }

    /// <summary>Checks that the caller has at least <paramref name="needed"/> in the project of an asset.</summary>
    public ProblemHttpResult? CheckAsset(ClaimsPrincipal user, AssetFile asset, ProjectRole needed) =>
        Check(RoleOf(user, asset.ProjectId), needed, () => UnknownAsset(asset.Id));

    private static ProblemHttpResult? Check(ProjectRole? role, ProjectRole needed, Func<ProblemHttpResult> notFound)
    {
        if (role is null)
            return notFound();
        if (role < needed)
            return TypedResults.Problem(
                $"This needs the {needed.ToText()} role in the project; you are {role.Value.ToText()}.",
                statusCode: StatusCodes.Status403Forbidden, title: "Not allowed");
        return null;
    }

    public static ProblemHttpResult UnknownProject(string project) =>
        TypedResults.Problem($"There is no project '{project}'.", statusCode: StatusCodes.Status404NotFound, title: "Unknown project");

    public static ProblemHttpResult UnknownAsset(string id) =>
        TypedResults.Problem($"There is no asset '{id}'.", statusCode: StatusCodes.Status404NotFound, title: "Unknown asset");
}

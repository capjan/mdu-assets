using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>Projects and their members (PRJ-01 – PRJ-05).</summary>
public static class ProjectEndpoints
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects", List);
        api.MapPost("/projects", Create);
        api.MapGet("/projects/{project}", Get);
        api.MapPatch("/projects/{project}", Update);
        api.MapDelete("/projects/{project}", Delete);
        api.MapGet("/projects/{project}/members", Members);
        api.MapPut("/projects/{project}/members/{userName}", SetMember);
        api.MapDelete("/projects/{project}/members/{userName}", RemoveMember);
    }

    /// <summary>The caller's projects; admins see all (PRJ-02).</summary>
    private static ProjectList List(ClaimsPrincipal user, ProjectStore projects) =>
        new(projects.List(user.Id(), all: user.IsAdmin()).Select(p => ProjectResponse.From(p, user.IsAdmin())).ToList());

    private static Results<Ok<ProjectResponse>, ProblemHttpResult> Get(string project, ClaimsPrincipal user, ProjectAccess access, ProjectStore projects)
    {
        if (!access.TryGet(user, project, ProjectRole.Viewer, out var target, out var problem))
            return problem;
        return TypedResults.Ok(Summary(user, projects, target.Id));
    }

    /// <summary>Admins create projects; other users only when an admin allows it (PRJ-01, ADR-0014). The creator becomes owner.</summary>
    private static Results<Created<ProjectResponse>, ProblemHttpResult> Create(
        NewProject request, ClaimsPrincipal user, ProjectStore projects, SettingsStore settings)
    {
        if (!user.IsAdmin() && !settings.GetFlag(SettingsStore.UsersCanCreateProjects))
            return TypedResults.Problem("Only admins create projects on this instance. Ask an admin to create it or to allow it.",
                statusCode: StatusCodes.Status403Forbidden, title: "Not allowed");

        var slug = request.Slug?.Trim() ?? string.Empty;
        if (!ProjectSlugs.IsValid(slug))
            return Invalid("Invalid short name", ProjectSlugs.Rule);
        if (Validate(request.Name, request.Description ?? string.Empty) is { } invalid)
            return invalid;

        Project created;
        try
        {
            created = projects.Create(slug, request.Name!.Trim(), (request.Description ?? string.Empty).Trim(), user.Id());
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Short name taken");
        }

        return TypedResults.Created($"/api/v1/projects/{slug}", Summary(user, projects, created.Id));
    }

    /// <summary>Renames a project or changes its description; links do not change (PRJ-04).</summary>
    private static Results<Ok<ProjectResponse>, ProblemHttpResult> Update(
        string project, ProjectChanges changes, ClaimsPrincipal user, ProjectAccess access, ProjectStore projects)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        var name = changes.Name ?? target.Name;
        var description = changes.Description ?? target.Description;
        if (Validate(name, description) is { } invalid)
            return invalid;

        if (projects.Update(target.Id, name.Trim(), description.Trim()) is null)
            return ProjectAccess.UnknownProject(project);
        return TypedResults.Ok(Summary(user, projects, target.Id));
    }

    /// <summary>
    /// Deletes an empty project (PRJ-05). Projects with images can be deleted once the trash exists (M6),
    /// because their images must stay recoverable.
    /// </summary>
    private static Results<NoContent, ProblemHttpResult> Delete(string project, ClaimsPrincipal user, ProjectAccess access, ProjectStore projects)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        if (!projects.DeleteEmpty(target.Id))
            return TypedResults.Problem("The project still has images. Only empty projects can be deleted for now.",
                statusCode: StatusCodes.Status409Conflict, title: "Project not empty");
        return TypedResults.NoContent();
    }

    private static Results<Ok<MemberList>, ProblemHttpResult> Members(string project, ClaimsPrincipal user, ProjectAccess access, ProjectStore projects)
    {
        if (!access.TryGet(user, project, ProjectRole.Viewer, out var target, out var problem))
            return problem;
        return TypedResults.Ok(MemberListOf(projects, target.Id));
    }

    /// <summary>Adds a member or changes the role (PRJ-03). A project always keeps at least one owner.</summary>
    private static Results<Ok<MemberList>, ProblemHttpResult> SetMember(
        string project, string userName, MemberChange change, ClaimsPrincipal user, ProjectAccess access, ProjectStore projects, UserStore users)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        if (ProjectRoles.Parse(change.Role) is not { } role)
            return Invalid("Invalid role", "The role is viewer, editor or owner.");
        if (users.Find(userName) is not { } member)
            return UnknownUser(userName);

        try
        {
            projects.SetRole(target.Id, member.Id, role);
        }
        catch (LastOwnerException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Last owner");
        }

        return TypedResults.Ok(MemberListOf(projects, target.Id));
    }

    private static Results<Ok<MemberList>, ProblemHttpResult> RemoveMember(
        string project, string userName, ClaimsPrincipal user, ProjectAccess access, ProjectStore projects, UserStore users)
    {
        if (!access.TryGet(user, project, ProjectRole.Owner, out var target, out var problem))
            return problem;
        if (users.Find(userName) is not { } member)
            return UnknownUser(userName);

        try
        {
            if (!projects.RemoveMember(target.Id, member.Id))
                return TypedResults.Problem($"'{member.Name}' is no member of this project.", statusCode: StatusCodes.Status404NotFound, title: "No member");
        }
        catch (LastOwnerException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Last owner");
        }

        return TypedResults.Ok(MemberListOf(projects, target.Id));
    }

    private static MemberList MemberListOf(ProjectStore projects, long projectId) =>
        new(projects.Members(projectId).Select(m => new MemberResponse(m.UserName, m.Role.ToText(), m.IsLocked)).ToList());

    private static ProjectResponse Summary(ClaimsPrincipal user, ProjectStore projects, long projectId) =>
        ProjectResponse.From(projects.List(user.Id(), all: true, projectId).Single(), user.IsAdmin());

    private static ProblemHttpResult? Validate(string? name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            return Invalid("Invalid name", $"A project name has 1 to {MaxNameLength} characters.");
        if (description.Trim().Length > MaxDescriptionLength)
            return Invalid("Invalid description", $"A description has at most {MaxDescriptionLength} characters.");
        return null;
    }

    private static ProblemHttpResult Invalid(string title, string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: title);

    internal static ProblemHttpResult UnknownUser(string name) =>
        TypedResults.Problem($"There is no user '{name}'.", statusCode: StatusCodes.Status404NotFound, title: "Unknown user");
}

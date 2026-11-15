using System.Security.Claims;
using MduAssets.Security;
using MduAssets.Storage;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MduAssets.Api;

/// <summary>The signed-in user's account (AUTH-05) and, for admins, users, settings and the overview (AUTH-04, OPS-04).</summary>
public static class UserEndpoints
{
    public static void Map(RouteGroupBuilder api, RouteGroupBuilder admin)
    {
        api.MapGet("/me", Me).AllowWithStartPassword();
        api.MapPost("/me/password", ChangePassword).AllowWithStartPassword().RequirePassword();
        api.MapGet("/me/tokens", Tokens).RequirePassword();
        api.MapPost("/me/tokens", CreateToken).RequirePassword();
        api.MapDelete("/me/tokens/{id:long}", DeleteToken).RequirePassword();

        admin.MapGet("/users", List);
        admin.MapPost("/users", Create);
        admin.MapGet("/users/{name}", Get);
        admin.MapPatch("/users/{name}", Update);
        admin.MapPost("/users/{name}/password-reset", ResetPassword);
        admin.MapGet("/settings", Settings);
        admin.MapPatch("/settings", ChangeSettings);
        admin.MapGet("/admin/overview", Overview);
    }

    private static MeResponse Me(ClaimsPrincipal user, SettingsStore settings, ServerSettings server) =>
        new(user.Identity?.Name ?? string.Empty, user.IsAdmin(), user.MustChangePassword(),
            user.IsAdmin() || settings.GetFlag(SettingsStore.UsersCanCreateProjects), server.Widths, server.Options.MaxUploadBytes);

    /// <summary>Replaces the caller's password; the current one must be given again, also for start passwords.</summary>
    private static Results<NoContent, ProblemHttpResult> ChangePassword(PasswordChange change, ClaimsPrincipal user, UserStore users)
    {
        var account = users.Find(user.Identity!.Name!)!;
        if (change.CurrentPassword is null || !PasswordHasher.Verify(change.CurrentPassword, account.PasswordHash))
            return TypedResults.Problem("The current password is wrong.", statusCode: StatusCodes.Status400BadRequest, title: "Wrong password");
        if (change.NewPassword is null || change.NewPassword.Length < PasswordHasher.MinimumLength)
            return TypedResults.Problem($"The new password needs at least {PasswordHasher.MinimumLength} characters.",
                statusCode: StatusCodes.Status400BadRequest, title: "Password too short");
        if (change.NewPassword == change.CurrentPassword)
            return TypedResults.Problem("The new password must differ from the current one.", statusCode: StatusCodes.Status400BadRequest, title: "Same password");

        users.SetPassword(account.Id, PasswordHasher.Hash(change.NewPassword), mustChange: false);
        return TypedResults.NoContent();
    }

    private static TokenList Tokens(ClaimsPrincipal user, TokenStore tokens) => new(tokens.List(user.Id()).Select(TokenResponse.From).ToList());

    /// <summary>
    /// Creates a personal API token (AUTH-06): named, valid for 90 days unless <c>expires</c> says otherwise (a number of days or
    /// <c>never</c>), optionally limited to one project and to reading. The secret is in this answer only.
    /// </summary>
    private static Results<Created<TokenCreated>, ProblemHttpResult> CreateToken(
        NewToken request, ClaimsPrincipal user, TokenStore tokens, ProjectAccess access, TimeProvider time)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
            return Invalid("Invalid name", "A token name has 1 to 100 characters, for example ShareX on my laptop.");
        var scope = request.Scope ?? "write";
        if (scope is not ("read" or "write"))
            return Invalid("Invalid scope", "The scope is read or write.");
        DateTimeOffset? expires = time.GetUtcNow().AddDays(90);
        if (request.Expires is "never")
            expires = null;
        else if (request.Expires is { } days)
        {
            if (!int.TryParse(days, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) || count is < 1 or > 3650)
                return Invalid("Invalid expiry", "Expires is a number of days from 1 to 3650, or never.");
            expires = time.GetUtcNow().AddDays(count);
        }

        long? projectId = null;
        if (!string.IsNullOrEmpty(request.Project))
        {
            if (!access.TryGet(user, request.Project, ProjectRole.Viewer, out var project, out var problem))
                return problem;
            projectId = project.Id;
        }

        var secret = TokenStore.NewSecret();
        var token = tokens.Create(user.Id(), name, secret, projectId, scope, expires);
        return TypedResults.Created($"/api/v1/me/tokens/{token.Id}", new TokenCreated(TokenResponse.From(token), secret));
    }

    private static Results<NoContent, ProblemHttpResult> DeleteToken(long id, ClaimsPrincipal user, TokenStore tokens) =>
        tokens.Delete(user.Id(), id)
            ? TypedResults.NoContent()
            : TypedResults.Problem($"There is no token {id}.", statusCode: StatusCodes.Status404NotFound, title: "Unknown token");

    private static ProblemHttpResult Invalid(string title, string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: title);

    private static UserList List(UserStore users) => new(users.List().Select(UserResponse.From).ToList());

    private static Results<Ok<UserResponse>, ProblemHttpResult> Get(string name, UserStore users) =>
        users.Find(name) is { } user ? TypedResults.Ok(UserResponse.From(user)) : ProjectEndpoints.UnknownUser(name);

    /// <summary>Creates a user with a start password that is shown once and must be changed at the first sign-in.</summary>
    private static Results<Created<StartPasswordResponse>, ProblemHttpResult> Create(NewUser request, UserStore users)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (!UserNames.IsValid(name))
            return TypedResults.Problem(UserNames.Rule, statusCode: StatusCodes.Status400BadRequest, title: "Invalid user name");

        var password = StartPasswords.New();
        try
        {
            var created = users.Create(name, PasswordHasher.Hash(password), request.IsAdmin, mustChangePassword: true);
            return TypedResults.Created($"/api/v1/users/{Uri.EscapeDataString(created.Name)}",
                new StartPasswordResponse(UserResponse.From(created), password));
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "User exists");
        }
    }

    /// <summary>Grants or removes the admin role and locks or unlocks; the last active admin stays.</summary>
    private static Results<Ok<UserResponse>, ProblemHttpResult> Update(string name, UserChanges changes, UserStore users)
    {
        if (users.Find(name) is not { } user)
            return ProjectEndpoints.UnknownUser(name);
        try
        {
            return TypedResults.Ok(UserResponse.From(users.Update(user.Id, changes.IsAdmin ?? user.IsAdmin, changes.IsLocked ?? user.IsLocked)));
        }
        catch (LastAdminException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Last admin");
        }
    }

    /// <summary>Sets a new start password; the old one stops working at once.</summary>
    private static Results<Ok<StartPasswordResponse>, ProblemHttpResult> ResetPassword(string name, UserStore users)
    {
        if (users.Find(name) is not { } user)
            return ProjectEndpoints.UnknownUser(name);
        var password = StartPasswords.New();
        users.SetPassword(user.Id, PasswordHasher.Hash(password), mustChange: true);
        return TypedResults.Ok(new StartPasswordResponse(UserResponse.From(users.Find(name)!), password));
    }

    private static SettingsResponse Settings(SettingsStore settings) =>
        new(settings.GetFlag(SettingsStore.UsersCanCreateProjects));

    private static SettingsResponse ChangeSettings(SettingsChanges changes, SettingsStore settings)
    {
        if (changes.UsersCanCreateProjects is { } allowed)
            settings.SetFlag(SettingsStore.UsersCanCreateProjects, allowed);
        return Settings(settings);
    }

    private static OverviewResponse Overview(
        ClaimsPrincipal user, UserStore users, ProjectStore projects, AssetStore assets, SettingsStore settings, ServerSettings server, DataDirectory data)
    {
        var allUsers = users.List();
        var allProjects = projects.List(user.Id(), all: true)
            .OrderByDescending(p => p.TotalSize).Select(p => ProjectResponse.From(p, isAdmin: true)).ToList();
        return new OverviewResponse(
            allUsers.Count,
            allUsers.Count(u => u.IsAdmin && !u.IsLocked),
            allUsers.Count(u => u.IsLocked),
            allProjects.Count,
            allProjects.Sum(p => p.AssetCount),
            assets.StorageUsed(),
            allProjects,
            Settings(settings),
            new ServerInfo(server.UiBaseUrl.ToString(), server.PublicBaseUrl.ToString(), server.Options.MaxUploadBytes,
                server.Options.MaxPixels, data.Root));
    }
}

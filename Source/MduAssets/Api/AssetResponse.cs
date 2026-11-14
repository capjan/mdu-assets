using System.Text.Json;
using System.Text.Json.Serialization;
using MduAssets.Storage;
using Microsoft.AspNetCore.Mvc;

namespace MduAssets.Api;

/// <summary>What the API returns for an asset (API-05).</summary>
public sealed record AssetResponse(
    string Id,
    string Url,
    string FileName,
    string AltText,
    string Markdown,
    string Html,
    int? Width,
    int? Height,
    long Size,
    string ContentType,
    int Version,
    DateTimeOffset CreatedAt)
{
    public static AssetResponse From(AssetFile asset, ServerSettings settings)
    {
        var url = settings.PublicLink(asset.Id, asset.FileName);
        return new AssetResponse(
            asset.Id, url, asset.FileName, asset.AltText,
            Snippets.Markdown(asset.AltText, url), Snippets.Html(asset.AltText, url, asset.Width, asset.Height),
            asset.Width, asset.Height, asset.Size, asset.ContentType, asset.Version, asset.CreatedAt);
    }
}

/// <summary>One page of assets; <c>Next</c> is set when another page follows.</summary>
public sealed record AssetList(IReadOnlyList<AssetResponse> Items, string? Next);

/// <summary>Changes to an asset; properties left out stay as they are.</summary>
public sealed record AssetChanges(string? FileName, string? AltText);

/// <summary>A project with its usage. <c>Role</c> is what the caller may do: their membership, or <c>owner</c> for admins.</summary>
public sealed record ProjectResponse(
    string Slug,
    string Name,
    string Description,
    int AssetCount,
    long TotalSize,
    DateTimeOffset? LastUpload,
    string Role,
    bool IsMember)
{
    public static ProjectResponse From(ProjectSummary project, bool isAdmin) =>
        new(project.Slug, project.Name, project.Description, project.AssetCount, project.TotalSize, project.LastUpload,
            (project.Role ?? (isAdmin ? ProjectRole.Owner : ProjectRole.Viewer)).ToText(), project.Role is not null);
}

public sealed record ProjectList(IReadOnlyList<ProjectResponse> Items);

public sealed record NewProject(string? Slug, string? Name, string? Description);

/// <summary>Changes to a project; the slug never changes, so tool configurations keep working.</summary>
public sealed record ProjectChanges(string? Name, string? Description);

public sealed record MemberResponse(string UserName, string Role, bool IsLocked);

public sealed record MemberList(IReadOnlyList<MemberResponse> Items);

public sealed record MemberChange(string? Role);

public sealed record MeResponse(string Name, bool IsAdmin, bool MustChangePassword, bool CanCreateProjects);

public sealed record PasswordChange(string? CurrentPassword, string? NewPassword);

public sealed record UserResponse(string Name, bool IsAdmin, bool IsLocked, bool MustChangePassword, DateTimeOffset CreatedAt)
{
    public static UserResponse From(User user) => new(user.Name, user.IsAdmin, user.IsLocked, user.MustChangePassword, user.CreatedAt);
}

public sealed record UserList(IReadOnlyList<UserResponse> Items);

public sealed record NewUser(string? Name, bool IsAdmin);

/// <summary>A start password is shown once, right after it was created; the server keeps only its hash.</summary>
public sealed record StartPasswordResponse(UserResponse User, string StartPassword);

public sealed record UserChanges(bool? IsAdmin, bool? IsLocked);

public sealed record SettingsResponse(bool UsersCanCreateProjects);

public sealed record SettingsChanges(bool? UsersCanCreateProjects);

/// <summary>Everything an admin needs at a glance (OPS-04).</summary>
public sealed record OverviewResponse(
    int Users,
    int Admins,
    int LockedUsers,
    int Projects,
    int Assets,
    long StorageUsed,
    IReadOnlyList<ProjectResponse> ProjectUsage,
    SettingsResponse Settings,
    ServerInfo Server);

public sealed record ServerInfo(string UiBaseUrl, string PublicBaseUrl, long MaxUploadBytes, long MaxPixels, string DataDirectory);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AssetResponse))]
[JsonSerializable(typeof(AssetList))]
[JsonSerializable(typeof(AssetChanges))]
[JsonSerializable(typeof(ProjectResponse))]
[JsonSerializable(typeof(ProjectList))]
[JsonSerializable(typeof(NewProject))]
[JsonSerializable(typeof(ProjectChanges))]
[JsonSerializable(typeof(MemberList))]
[JsonSerializable(typeof(MemberChange))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(PasswordChange))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(UserList))]
[JsonSerializable(typeof(NewUser))]
[JsonSerializable(typeof(StartPasswordResponse))]
[JsonSerializable(typeof(UserChanges))]
[JsonSerializable(typeof(SettingsResponse))]
[JsonSerializable(typeof(SettingsChanges))]
[JsonSerializable(typeof(OverviewResponse))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using MduAssets.Storage;
using Microsoft.AspNetCore.Mvc;

namespace MduAssets.Api;

/// <summary>
/// What the API returns for an asset (API-05). <c>OriginalContentType</c> and <c>OriginalSize</c> describe the upload when it was
/// converted to WebP (UP-11); the original is kept but not served.
/// </summary>
public sealed record AssetResponse(
    string Id,
    string Project,
    string Url,
    string FileName,
    string AltText,
    string Description,
    IReadOnlyList<string> Tags,
    string Markdown,
    string Html,
    int? Width,
    int? Height,
    long Size,
    string ContentType,
    string Sha256,
    int Version,
    string UploadedBy,
    DateTimeOffset CreatedAt,
    string ThumbnailUrl,
    bool Animated,
    AssetPair? Pair,
    string VersionUrl,
    DateTimeOffset? DeletedAt,
    long Requests,
    DateTimeOffset? LastRequestAt,
    string? OriginalContentType = null,
    long? OriginalSize = null)
{
    public static AssetResponse From(AssetFile asset, ServerSettings settings)
    {
        var url = settings.PublicLink(asset.Id, asset.FileName);
        AssetPair? pair = null;
        if (asset is { PairId: { } pairId, PairRole: { } role, PairFileName: { } pairName })
        {
            // Both lines carry this asset's alt text, as MDU expects for a light and dark pair (LNK-06).
            var partnerUrl = settings.PublicLink(pairId, pairName);
            var (light, dark) = role == "light" ? (url, partnerUrl) : (partnerUrl, url);
            pair = new AssetPair(pairId, role, partnerUrl, Snippets.Markdown(asset.AltText, light + "#only-light") + "\n" + Snippets.Markdown(asset.AltText, dark + "#only-dark"));
        }

        return new AssetResponse(
            asset.Id, asset.ProjectSlug, url, asset.FileName, asset.AltText, asset.Description, asset.Tags,
            Snippets.Markdown(asset.AltText, url), Snippets.Html(asset.AltText, url, asset.Width, asset.Height),
            asset.Width, asset.Height, asset.Size, asset.ContentType, asset.BlobSha256, asset.Version, asset.UploadedBy, asset.CreatedAt,
            $"/api/v1/assets/{asset.Id}/thumbnail?v={asset.BlobSha256[..12]}", asset.Animated, pair,
            settings.PublicLink(asset.Id, asset.FileName, asset.Version), asset.DeletedAt, asset.Requests, asset.LastRequestAt,
            asset.Original?.ContentType, asset.Original?.Size);
    }
}

/// <summary>One version of an asset (VER-03); <c>Url</c> is its version link, which never changes (DLV-02).</summary>
public sealed record VersionResponse(
    int Number, string Url, string ContentType, long Size, int? Width, int? Height, string CreatedBy, DateTimeOffset CreatedAt, bool IsCurrent,
    string? OriginalContentType = null, long? OriginalSize = null);

public sealed record VersionList(IReadOnlyList<VersionResponse> Items);

/// <summary>The answer to a new version; <c>FormatChanged</c> is set when its type differs from the previous one (VER-02).</summary>
public sealed record VersionAdded(AssetResponse Asset, bool FormatChanged, string PreviousContentType);

/// <summary>An asset in the trash and when it will be deleted for good (DEL-02).</summary>
public sealed record TrashItem(AssetResponse Asset, DateTimeOffset DeletedAt, string? DeletedBy, DateTimeOffset DeletesAt);

public sealed record TrashList(IReadOnlyList<TrashItem> Items, int RetentionDays);

public sealed record PurgeResult(int Deleted);

/// <summary>A page that showed the image (STA-04): origin and path, as browsers report them.</summary>
public sealed record ReferrerResponse(string Origin, string Path, long Count, DateTimeOffset LastRequestAt);

/// <summary>Referring pages of an asset, at most 100, most requests first.</summary>
public sealed record ReferrerList(IReadOnlyList<ReferrerResponse> Items);

/// <summary>The other half of a light and dark pair; <c>Role</c> is this asset's role, <c>Markdown</c> holds both lines.</summary>
public sealed record AssetPair(string Id, string Role, string PartnerUrl, string Markdown);

/// <summary>Pairs an asset with another one of the same project; <c>Role</c> is light or dark for this asset.</summary>
public sealed record PairChange(string? PartnerId, string? Role);

/// <summary>The answer to an upload whose content is already in the project, when the caller asked to hear about it (UP-09).</summary>
public sealed record DuplicateResponse(string Title, string Detail, AssetResponse Existing);

/// <summary>One page of assets; <c>Next</c> is set when another page follows.</summary>
public sealed record AssetList(IReadOnlyList<AssetResponse> Items, string? Next);

/// <summary>Changes to an asset; properties left out stay as they are.</summary>
public sealed record AssetChanges(string? FileName, string? AltText, string? Description = null, IReadOnlyList<string>? Tags = null);

public sealed record TagCountResponse(string Name, int Count);

/// <summary>Values for the filters and tag suggestions of a project.</summary>
public sealed record FacetsResponse(IReadOnlyList<TagCountResponse> Tags, IReadOnlyList<string> Uploaders, IReadOnlyList<string> Types);

/// <summary>A project with its usage. <c>Role</c> is what the caller may do: their membership, or <c>owner</c> for admins.</summary>
public sealed record ProjectResponse(
    string Slug,
    string Name,
    string Description,
    int AssetCount,
    long TotalSize,
    DateTimeOffset? LastUpload,
    string Role,
    bool IsMember,
    bool ConvertFormats,
    int TrashCount,
    int? MaxVersions,
    long UsedBytes,
    long? QuotaBytes,
    long? MaxFileBytes)
{
    public static ProjectResponse From(ProjectSummary project, bool isAdmin) =>
        new(project.Slug, project.Name, project.Description, project.AssetCount, project.TotalSize, project.LastUpload,
            (project.Role ?? (isAdmin ? ProjectRole.Owner : ProjectRole.Viewer)).ToText(), project.Role is not null, project.ConvertFormats, project.TrashCount, project.MaxVersions,
            project.UsedBytes, project.QuotaBytes, project.MaxFileBytes);
}

public sealed record ProjectList(IReadOnlyList<ProjectResponse> Items);

public sealed record NewProject(string? Slug, string? Name, string? Description);

/// <summary>Changes to a project; the slug never changes, so tool configurations keep working.</summary>
/// <remarks><c>MaxVersions</c>: 0 keeps all versions, a positive number keeps that many (VER-04).</remarks>
/// <remarks><c>MaxFileBytes</c> and <c>QuotaBytes</c>: 0 removes the limit. Only admins set the quota (OPS-02).</remarks>
public sealed record ProjectChanges(
    string? Name, string? Description, bool? ConvertFormats = null, int? MaxVersions = null, long? MaxFileBytes = null, long? QuotaBytes = null);

public sealed record MemberResponse(string UserName, string Role, bool IsLocked);

public sealed record MemberList(IReadOnlyList<MemberResponse> Items);

public sealed record MemberChange(string? Role);

/// <summary>The signed-in user and what this instance offers them; <c>Widths</c> are the sizes <c>?w=</c> may ask for.</summary>
public sealed record MeResponse(string Name, bool IsAdmin, bool MustChangePassword, bool CanCreateProjects, IReadOnlyList<int> Widths, long MaxUploadBytes);

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

/// <summary>A personal API token as listed; the secret is never shown again.</summary>
public sealed record TokenResponse(long Id, string Name, string Prefix, string? Project, string Scope, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset CreatedAt)
{
    public static TokenResponse From(ApiToken token) =>
        new(token.Id, token.Name, token.Prefix, token.ProjectSlug, token.Scope, token.ExpiresAt, token.LastUsedAt, token.CreatedAt);
}

public sealed record TokenList(IReadOnlyList<TokenResponse> Items);

/// <summary>A new token: <c>Expires</c> is a number of days (default 90) or <c>never</c>; <c>Scope</c> is read or write (default).</summary>
public sealed record NewToken(string? Name, string? Expires, string? Project, string? Scope);

/// <summary>The answer to a new token, with the secret that is shown only now.</summary>
public sealed record TokenCreated(TokenResponse Token, string Secret);

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
[JsonSerializable(typeof(PairChange))]
[JsonSerializable(typeof(VersionList))]
[JsonSerializable(typeof(VersionAdded))]
[JsonSerializable(typeof(TrashList))]
[JsonSerializable(typeof(PurgeResult))]
[JsonSerializable(typeof(ReferrerList))]
[JsonSerializable(typeof(DuplicateResponse))]
[JsonSerializable(typeof(FacetsResponse))]
[JsonSerializable(typeof(TagChange))]
[JsonSerializable(typeof(TagChangeResult))]
[JsonSerializable(typeof(TagRename))]
[JsonSerializable(typeof(TagResult))]
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
[JsonSerializable(typeof(TokenList))]
[JsonSerializable(typeof(AuditList))]
[JsonSerializable(typeof(ExportManifest))]
[JsonSerializable(typeof(ImportResult))]
[JsonSerializable(typeof(CheckRequest))]
[JsonSerializable(typeof(CheckResponse))]
[JsonSerializable(typeof(NewToken))]
[JsonSerializable(typeof(TokenCreated))]
[JsonSerializable(typeof(SettingsChanges))]
[JsonSerializable(typeof(OverviewResponse))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

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

public sealed record AssetList(IReadOnlyList<AssetResponse> Items);

public sealed record ProjectResponse(string Slug, string Name, int AssetCount, long TotalSize);

public sealed record ProjectList(IReadOnlyList<ProjectResponse> Items);

public sealed record MeResponse(string Name, bool IsAdmin);

/// <summary>Changes to an asset; properties left out stay as they are.</summary>
public sealed record AssetChanges(string? FileName, string? AltText);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AssetResponse))]
[JsonSerializable(typeof(AssetList))]
[JsonSerializable(typeof(ProjectList))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(AssetChanges))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

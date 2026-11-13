using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MduAssets.Api;

/// <summary>What the API returns for an uploaded asset (API-05).</summary>
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
    int Version);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AssetResponse))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

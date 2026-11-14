namespace MduAssets.Api;

/// <summary>Free tags (ORG-03): trimmed, inner whitespace collapsed, compared without case.</summary>
public static class TagNames
{
    public const int MaxLength = 50;
    public const int MaxPerAsset = 30;

    /// <summary>Cleans a list of tags, drops empty and duplicate ones, or returns an error message.</summary>
    public static (IReadOnlyList<string>? Tags, string? Error) Normalize(IEnumerable<string?> tags)
    {
        var result = new List<string>();
        foreach (var raw in tags)
        {
            var tag = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (tag.Length == 0)
                continue;
            if (tag.Length > MaxLength)
                return (null, $"A tag has at most {MaxLength} characters.");
            if (!result.Any(t => t.ToLowerInvariant() == tag.ToLowerInvariant()))
                result.Add(tag);
        }

        return result.Count > MaxPerAsset ? (null, $"An image has at most {MaxPerAsset} tags.") : (result, null);
    }
}

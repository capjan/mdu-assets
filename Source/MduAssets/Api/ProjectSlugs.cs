using System.Text.RegularExpressions;

namespace MduAssets.Api;

/// <summary>Project short names appear in API paths and tool configurations, so they stay simple and never change.</summary>
public static partial class ProjectSlugs
{
    public const string Rule = "A short name has 1 to 64 lowercase letters, digits or hyphens and starts and ends with a letter or digit.";

    public static bool IsValid(string slug) => Pattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex Pattern();
}

using System.Text;

namespace MduAssets.Api;

/// <summary>Turns uploaded file names into the readable part of public links and into alt text suggestions.</summary>
public static class FileNames
{
    private const int MaxStemLength = 80;

    /// <summary>
    /// Keeps letters, digits, dots, hyphens and underscores, replaces everything else with a hyphen and puts the
    /// extension of the detected format at the end: <c>Screen Shot (2).PNG</c> becomes <c>Screen-Shot-2.png</c>.
    /// </summary>
    public static string Normalize(string? uploadedName, string extension)
    {
        var stem = Stem(uploadedName);
        var builder = new StringBuilder(stem.Length);
        foreach (var c in stem)
        {
            var keep = char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-';
            if (keep)
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var clean = builder.ToString().Trim('-', '.', '_');
        if (clean.Length > MaxStemLength)
            clean = clean[..MaxStemLength].TrimEnd('-', '.', '_');
        return (clean.Length == 0 ? "image" : clean) + "." + extension;
    }

    /// <summary>Suggests alt text from a file name: <c>app-settings_dark.png</c> becomes <c>App settings dark</c>.</summary>
    public static string SuggestAltText(string? uploadedName)
    {
        var words = Stem(uploadedName).Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "Image";
        var text = string.Join(' ', words);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Stem(string? name)
    {
        var fileName = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}

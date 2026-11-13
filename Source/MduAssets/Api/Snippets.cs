using System.Globalization;
using System.Net;
using System.Text;

namespace MduAssets.Api;

/// <summary>Ready-to-paste text for an asset (LNK-05, LNK-08).</summary>
public static class Snippets
{
    /// <summary><c>![alt](url)</c> with brackets and backslashes in the alt text escaped.</summary>
    public static string Markdown(string altText, string url)
    {
        var alt = new StringBuilder(altText.Length);
        foreach (var c in altText)
        {
            if (c is '\\' or '[' or ']')
                alt.Append('\\');
            alt.Append(c is '\r' or '\n' ? ' ' : c);
        }

        return $"![{alt}]({url})";
    }

    /// <summary>An <c>img</c> element with alt text and, when known, the size.</summary>
    public static string Html(string altText, string url, int? width, int? height)
    {
        var html = new StringBuilder($"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"{WebUtility.HtmlEncode(altText)}\"");
        if (width is { } w && height is { } h)
            html.Append(CultureInfo.InvariantCulture, $" width=\"{w}\" height=\"{h}\"");
        return html.Append('>').ToString();
    }
}

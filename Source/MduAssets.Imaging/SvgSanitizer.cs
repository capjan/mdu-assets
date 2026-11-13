using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MduAssets.Imaging;

/// <summary>The cleaned SVG file and its size, when the file states one.</summary>
public sealed record SanitizedSvg(byte[] Content, ImageInfo Info);

/// <summary>
/// Cleans an uploaded SVG file so that it cannot run script or load anything from elsewhere, even when a browser
/// opens it directly. It follows the allowlist of MDU's custom icon reader (see ADR-0012): only SVG elements are kept,
/// event handlers are removed, links and references may only point into the file or to embedded raster images, and
/// style sheets lose imports and external URLs. Comments, metadata and editor data are dropped.
/// </summary>
public static partial class SvgSanitizer
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    private static readonly HashSet<string> AllowedElements = new(StringComparer.Ordinal)
    {
        "a", "animate", "animateMotion", "animateTransform", "circle", "clipPath", "defs", "desc", "ellipse", "feBlend",
        "feColorMatrix", "feComponentTransfer", "feComposite", "feConvolveMatrix", "feDiffuseLighting",
        "feDisplacementMap", "feDistantLight", "feDropShadow", "feFlood", "feFuncA", "feFuncB", "feFuncG", "feFuncR",
        "feGaussianBlur", "feImage", "feMerge", "feMergeNode", "feMorphology", "feOffset", "fePointLight",
        "feSpecularLighting", "feSpotLight", "feTile", "feTurbulence", "filter", "g", "image", "line", "linearGradient",
        "marker", "mask", "mpath", "path", "pattern", "polygon", "polyline", "radialGradient", "rect", "set", "stop",
        "style", "svg", "switch", "symbol", "text", "textPath", "title", "tspan", "use", "view"
    };

    private static readonly HashSet<string> AnimationElements = new(StringComparer.Ordinal)
    {
        "animate", "animateMotion", "animateTransform", "set"
    };

    /// <summary>
    /// Returns the cleaned file, or <c>null</c> when the content is not an SVG document.
    /// </summary>
    public static SanitizedSvg? Sanitize(ReadOnlySpan<byte> data)
    {
        if (!ImageDetector.LooksLikeXml(data))
            return null;

        XDocument document;
        try
        {
            using var stream = new MemoryStream(data.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root is null || root.Name != Svg + "svg")
            return null;

        var clean = (XElement)Clean(root)!;
        var (width, height) = ReadSize(root);
        var content = Encoding.UTF8.GetBytes(clean.ToString(SaveOptions.DisableFormatting));
        return new SanitizedSvg(content, new ImageInfo(ImageFormat.Svg, width, height));
    }

    private static XNode? Clean(XNode node)
    {
        switch (node)
        {
            case XElement element:
                var name = element.Name.LocalName;
                if (element.Name.Namespace != Svg || !AllowedElements.Contains(name) || IsUnsafeAnimation(element))
                    return null;
                var clean = new XElement(Svg + name, CleanAttributes(element));
                if (name == "style")
                    clean.Add(new XText(CleanStyleSheet(element.Value)));
                else
                    clean.Add(element.Nodes().Select(Clean).OfType<XNode>());
                return clean;
            case XText text:
                return new XText(text.Value);
            default:
                return null;
        }
    }

    // An animation of a reference or an event handler, such as <set attributeName="href" to="javascript:…"/>,
    // would bring back what the attribute checks removed.
    private static bool IsUnsafeAnimation(XElement element)
    {
        if (!AnimationElements.Contains(element.Name.LocalName))
            return false;
        var attributeName = ((string?)element.Attribute("attributeName"))?.Trim() ?? string.Empty;
        var localName = attributeName[(attributeName.LastIndexOf(':') + 1)..];
        return localName.Equals("href", StringComparison.OrdinalIgnoreCase) ||
               localName.Equals("target", StringComparison.OrdinalIgnoreCase) ||
               localName.StartsWith("on", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<XAttribute> CleanAttributes(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            string name;
            if (attribute.Name.Namespace == XLink && attribute.Name.LocalName == "href")
                name = "href";
            else if (attribute.Name.Namespace == XNamespace.None)
                name = attribute.Name.LocalName;
            else
                continue; // editor data such as inkscape:* or sodipodi:*

            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name == "href" && !IsAllowedReference(attribute.Value))
                continue;

            yield return new XAttribute(name, CleanCss(attribute.Value));
        }
    }

    /// <summary>A reference into the file itself or to an embedded raster image.</summary>
    private static bool IsAllowedReference(string value)
    {
        var target = value.Trim();
        return target.StartsWith('#') || EmbeddedRasterImage().IsMatch(target);
    }

    private static string CleanStyleSheet(string css) =>
        CleanCss(CssImport().Replace(CssComment().Replace(RemoveEscapes(css), string.Empty), string.Empty));

    /// <summary>Removes everything in CSS that loads from elsewhere.</summary>
    private static string CleanCss(string value) => RewriteUrls(RemoveImageFunctions(RemoveEscapes(value)));

    // Escapes such as @\69mport or u\72l( would hide imports and URLs from the checks, so they are decoded first.
    // A backslash left after that is removed so that it cannot start a new escape.
    private static string RemoveEscapes(string css) =>
        CssEscape().Replace(css, match =>
        {
            if (!match.Groups["hex"].Success)
                return match.Groups["character"].Value;
            var code = int.Parse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(code) : "\uFFFD";
        }).Replace("\\", string.Empty, StringComparison.Ordinal);

    // image-set() and image() load files from plain strings, without url().
    private static string RemoveImageFunctions(string css)
    {
        for (var match = ImageFunction().Match(css); match.Success; match = ImageFunction().Match(css))
        {
            var depth = 0;
            var end = match.Index + match.Length - 1;
            for (; end < css.Length; end++)
            {
                if (css[end] == '(')
                    depth++;
                else if (css[end] == ')' && --depth == 0)
                    break;
            }

            css = css[..match.Index] + "none" + css[Math.Min(end + 1, css.Length)..];
        }

        return css;
    }

    private static string RewriteUrls(string value) =>
        UrlFunction().Replace(value, match => IsAllowedReference(match.Groups["target"].Value) ? match.Value : "none");

    private static (int? Width, int? Height) ReadSize(XElement root)
    {
        var width = ReadLength((string?)root.Attribute("width"));
        var height = ReadLength((string?)root.Attribute("height"));
        if (width is not null && height is not null)
            return (width, height);

        var viewBox = ((string?)root.Attribute("viewBox"))?.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (viewBox is { Length: 4 } &&
            double.TryParse(viewBox[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var boxWidth) &&
            double.TryParse(viewBox[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var boxHeight) &&
            boxWidth > 0 && boxHeight > 0)
        {
            return (width ?? (int)Math.Ceiling(boxWidth), height ?? (int)Math.Ceiling(boxHeight));
        }

        return (width, height);
    }

    private static int? ReadLength(string? value)
    {
        if (value is null)
            return null;
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            text = text[..^2];
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number is > 0 and < int.MaxValue
            ? (int)Math.Ceiling(number)
            : null;
    }

    [GeneratedRegex(@"^data:image/(?:png|jpeg|gif|webp|avif|bmp)[;,]", RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedRasterImage();

    [GeneratedRegex(@"url\(\s*(?<quote>['""]?)(?<target>.*?)\k<quote>\s*\)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UrlFunction();

    [GeneratedRegex(@"@import\b[^;]*;?", RegexOptions.IgnoreCase)]
    private static partial Regex CssImport();

    [GeneratedRegex(@"\\(?:(?<hex>[0-9A-Fa-f]{1,6})(?:\r\n|[ \t\r\n\f])?|(?<character>[^\r\n\f]))")]
    private static partial Regex CssEscape();

    [GeneratedRegex(@"(?<![\w-])(?:-webkit-)?(?:image-set|image|cross-fade|element)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ImageFunction();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();
}

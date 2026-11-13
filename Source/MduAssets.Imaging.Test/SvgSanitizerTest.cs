using System.Text;

namespace MduAssets.Imaging.Test;

public sealed class SvgSanitizerTest
{
    private const string Ns = "xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    private static string Clean(string svg)
    {
        var result = SvgSanitizer.Sanitize(TestImages.Svg(svg));
        Assert.NotNull(result);
        return Encoding.UTF8.GetString(result.Content);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<foreignObject><body xmlns=\"http://www.w3.org/1999/xhtml\" onload=\"alert(1)\"/></foreignObject>")]
    [InlineData("<rect onclick=\"alert(1)\" width=\"1\" height=\"1\"/>")]
    [InlineData("<a href=\"javascript:alert(1)\"><text>x</text></a>")]
    [InlineData("<a xlink:href=\"javascript:alert(1)\"><text>x</text></a>")]
    [InlineData("<set attributeName=\"href\" to=\"javascript:alert(1)\"/>")]
    [InlineData("<animate attributeName=\"onbegin\" values=\"alert(1)\"/>")]
    [InlineData("<image href=\"https://evil.example/track.png\" width=\"1\" height=\"1\"/>")]
    [InlineData("<image href=\"data:image/svg+xml;base64,PHN2Zy8+\" width=\"1\" height=\"1\"/>")]
    [InlineData("<use href=\"https://evil.example/sprite.svg#icon\"/>")]
    [InlineData("<style>@import url(https://evil.example/a.css); rect { fill: blue }</style>")]
    [InlineData("<style>@\\69mport 'https://evil.example/a.css';</style>")]
    [InlineData("<rect style=\"fill: url(https://evil.example/x)\" width=\"1\" height=\"1\"/>")]
    [InlineData("<rect style=\"background: image-set('https://evil.example/x.png' 1x)\" width=\"1\" height=\"1\"/>")]
    [InlineData("<html:script xmlns:html=\"http://www.w3.org/1999/xhtml\">alert(1)</html:script>")]
    public void RemovesActiveAndExternalContent(string payload)
    {
        var clean = Clean($"<svg {Ns} viewBox=\"0 0 10 10\">{payload}</svg>");

        Assert.DoesNotContain("alert", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("svg+xml", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoesNotResolveExternalEntities()
    {
        var svg = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
                  $"<svg {Ns}><text>&xxe;</text></svg>";

        var result = SvgSanitizer.Sanitize(TestImages.Svg(svg));

        Assert.True(result is null || !Encoding.UTF8.GetString(result.Content).Contains("root:"));
    }

    [Fact]
    public void KeepsDrawingsGradientsStylesAndAccessibleText()
    {
        var clean = Clean(
            $"<svg {Ns} width=\"120\" height=\"80\" role=\"img\" aria-labelledby=\"t\">" +
            "<title id=\"t\">Architecture</title><desc>Two boxes</desc>" +
            "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#06c\"/></linearGradient></defs>" +
            "<style>.box { fill: url(#g); }</style>" +
            "<rect class=\"box\" width=\"50\" height=\"30\"/><use xlink:href=\"#t\"/>" +
            "<image href=\"data:image/png;base64,iVBORw0KGgo=\" width=\"1\" height=\"1\"/></svg>");

        Assert.Contains("<title id=\"t\">Architecture</title>", clean);
        Assert.Contains("<desc>Two boxes</desc>", clean);
        Assert.Contains("linearGradient", clean);
        Assert.Contains("fill: url(#g)", clean);
        Assert.Contains("href=\"#t\"", clean);
        Assert.Contains("data:image/png;base64", clean);
        Assert.Contains("aria-labelledby=\"t\"", clean);
    }

    [Fact]
    public void DropsEditorMetadata()
    {
        var clean = Clean(
            $"<svg {Ns} xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" inkscape:version=\"1.3\">" +
            "<metadata><rdf>secret author</rdf></metadata><!-- comment --><rect width=\"1\" height=\"1\"/></svg>");

        Assert.DoesNotContain("inkscape", clean);
        Assert.DoesNotContain("secret author", clean);
        Assert.DoesNotContain("comment", clean);
    }

    [Theory]
    [InlineData("width=\"120\" height=\"80\"", 120, 80)]
    [InlineData("width=\"120px\" height=\"80.5px\"", 120, 81)]
    [InlineData("viewBox=\"0 0 64 32\"", 64, 32)]
    [InlineData("width=\"100%\" height=\"100%\" viewBox=\"0,0,24,24\"", 24, 24)]
    public void ReadsTheSize(string attributes, int width, int height)
    {
        var result = SvgSanitizer.Sanitize(TestImages.Svg($"<svg {Ns} {attributes}/>"));

        Assert.NotNull(result);
        Assert.Equal((width, height), (result.Info.Width, result.Info.Height));
        Assert.Equal("image/svg+xml", result.Info.ContentType);
    }

    [Theory]
    [InlineData("<html><body/></html>")]
    [InlineData("<svg>no namespace</svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><unclosed></svg>")]
    [InlineData("plain text")]
    public void RejectsWhatIsNotSvg(string content)
    {
        Assert.Null(SvgSanitizer.Sanitize(TestImages.Svg(content)));
    }
}

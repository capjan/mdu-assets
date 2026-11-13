using System.Text;

namespace MduAssets.Imaging.Test;

public sealed class ImageDetectorTest
{
    public static TheoryData<string, byte[], string, int, int> Images => new()
    {
        { "PNG", TestImages.Png(1600, 900), "image/png", 1600, 900 },
        { "GIF", TestImages.Gif(320, 200), "image/gif", 320, 200 },
        { "BMP", TestImages.Bmp(64, 48), "image/bmp", 64, 48 },
        { "BMP top-down", TestImages.Bmp(64, -48), "image/bmp", 64, 48 },
        { "JPEG", TestImages.Jpeg(4032, 3024), "image/jpeg", 4032, 3024 },
        { "WebP lossless", TestImages.WebPLossless(800, 450), "image/webp", 800, 450 },
        { "WebP extended", TestImages.WebPExtended(5000, 7000), "image/webp", 5000, 7000 },
        { "AVIF", TestImages.Heif("avif", 1920, 1080), "image/avif", 1920, 1080 },
    };

    [Theory]
    [MemberData(nameof(Images))]
    public void ReadsTypeAndSizeFromTheHeader(string format, byte[] data, string contentType, int width, int height)
    {
        var info = ImageDetector.DetectRaster(data);

        Assert.NotNull(info);
        Assert.Equal(contentType, info.ContentType);
        Assert.Equal((width, height), (info.Width, info.Height));
        Assert.Equal((long)width * height, info.Pixels);
        Assert.False(string.IsNullOrEmpty(format));
    }

    [Fact]
    public void RejectsHeicBecauseBrowsersCannotShowIt()
    {
        Assert.Null(ImageDetector.DetectRaster(TestImages.Heif("heic", 100, 100)));
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("PK\u0003\u0004 zip archive")]
    [InlineData("")]
    public void RejectsOtherContent(string text)
    {
        Assert.Null(ImageDetector.DetectRaster(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void RejectsTruncatedHeaders()
    {
        Assert.Null(ImageDetector.DetectRaster(TestImages.Png(10, 10)[..20]));
        Assert.Null(ImageDetector.DetectRaster(TestImages.Jpeg(10, 10)[..12]));
    }

    [Fact]
    public void RejectsZeroSizes()
    {
        Assert.Null(ImageDetector.DetectRaster(TestImages.Png(0, 10)));
        Assert.Null(ImageDetector.DetectRaster(TestImages.Gif(10, 0)));
    }

    [Theory]
    [InlineData("<svg/>", true)]
    [InlineData("﻿  \n<?xml version=\"1.0\"?><svg/>", true)]
    [InlineData("GIF89a", false)]
    [InlineData("   ", false)]
    public void RecognizesXmlCandidates(string text, bool expected)
    {
        Assert.Equal(expected, ImageDetector.LooksLikeXml(Encoding.UTF8.GetBytes(text)));
    }
}

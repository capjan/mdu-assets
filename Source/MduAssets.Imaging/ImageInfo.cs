namespace MduAssets.Imaging;

/// <summary>The type and size of an image, read from its first bytes without decoding it.</summary>
public sealed record ImageInfo(ImageFormat Format, int? Width, int? Height)
{
    public string ContentType => Format.ContentType;

    public string Extension => Format.Extension;

    /// <summary>Number of pixels, or <c>null</c> when the size is unknown (only possible for SVG).</summary>
    public long? Pixels => Width is { } w && Height is { } h ? (long)w * h : null;
}

/// <summary>The image formats MDU renders as images (UP-06).</summary>
public sealed record ImageFormat(string Name, string ContentType, string Extension)
{
    public static readonly ImageFormat Png = new("PNG", "image/png", "png");
    public static readonly ImageFormat Jpeg = new("JPEG", "image/jpeg", "jpg");
    public static readonly ImageFormat Gif = new("GIF", "image/gif", "gif");
    public static readonly ImageFormat WebP = new("WebP", "image/webp", "webp");
    public static readonly ImageFormat Avif = new("AVIF", "image/avif", "avif");
    public static readonly ImageFormat Bmp = new("BMP", "image/bmp", "bmp");
    public static readonly ImageFormat Svg = new("SVG", "image/svg+xml", "svg");

    public static IReadOnlyList<ImageFormat> All { get; } = [Png, Jpeg, Gif, WebP, Avif, Bmp, Svg];
}

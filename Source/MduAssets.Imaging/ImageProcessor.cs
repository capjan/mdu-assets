using NetVips;

namespace MduAssets.Imaging;

/// <summary>A cleaned upload: its bytes and the size after orientation was applied.</summary>
public sealed record PreparedImage(byte[] Content, ImageInfo Info, bool Animated, bool Changed);

/// <summary>An encoded image with its media type.</summary>
public sealed record EncodedImage(byte[] Content, string ContentType);

/// <summary>A file that libvips cannot decode although its header looked valid.</summary>
public sealed class ImageDecodeException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Image work with NetVips/libvips (ADR-0006), using the calls verified in the NetVips AOT spike. Callers limit how many
/// jobs run at once; this class keeps libvips' own cache off, so memory stays bounded by the running jobs.
/// </summary>
public static class ImageProcessor
{
    // Metadata that must not leave the server (UP-08). The ICC profile is kept on purpose.
    private static readonly string[] PrivateFields = ["exif-data", "xmp-data", "iptc-data"];

    /// <summary>Turns off libvips' operation cache and limits its threads per operation.</summary>
    public static void Configure(int threadsPerOperation)
    {
        Cache.Max = 0;
        Cache.MaxMem = 0;
        Cache.MaxFiles = 0;
        NetVips.NetVips.Concurrency = Math.Max(1, threadsPerOperation);
    }

    /// <summary>The libvips version, for the start log.</summary>
    public static string Version => $"{NetVips.NetVips.Version(0)}.{NetVips.NetVips.Version(1)}.{NetVips.NetVips.Version(2)}";

    /// <summary>
    /// Removes EXIF, GPS, XMP and IPTC metadata and applies the orientation, keeping the ICC profile (UP-08). Images without
    /// such metadata keep their exact bytes, so screenshots are never re-encoded. GIF and BMP carry no such metadata.
    /// </summary>
    public static PreparedImage Prepare(byte[] data, ImageInfo info)
    {
        if (info.Format == ImageFormat.Svg || info.Format == ImageFormat.Bmp)
            return new PreparedImage(data, info, Animated: false, Changed: false);

        try
        {
            using var image = Image.NewFromBuffer(data, info.Format == ImageFormat.Gif || info.Format == ImageFormat.WebP ? "n=-1" : "");
            var animated = image.Contains("n-pages") && (int)image.Get("n-pages") > 1;
            var orientation = image.Contains("orientation") ? (int)image.Get("orientation") : 1;
            var hasPrivateData = PrivateFields.Any(image.Contains) || image.GetFields().Any(f => f.StartsWith("png-comment", StringComparison.Ordinal));
            if (info.Format == ImageFormat.Gif || (!hasPrivateData && orientation is 1))
                return new PreparedImage(data, info, animated, Changed: false);

            using var rotated = animated ? image.Copy() : image.Autorot();
            var content = info.Format.Name switch
            {
                "JPEG" => rotated.JpegsaveBuffer(q: 92, keep: Enums.ForeignKeep.Icc),
                "PNG" => rotated.PngsaveBuffer(keep: Enums.ForeignKeep.Icc),
                "WebP" => rotated.WebpsaveBuffer(q: 90, keep: Enums.ForeignKeep.Icc),
                "AVIF" => rotated.HeifsaveBuffer(q: 60, compression: Enums.ForeignHeifCompression.Av1, effort: 4, keep: Enums.ForeignKeep.Icc),
                _ => data,
            };
            var height = animated ? info.Height : rotated.Height;
            return new PreparedImage(content, info with { Width = rotated.Width, Height = height }, animated, Changed: true);
        }
        catch (VipsException exception)
        {
            throw new ImageDecodeException("The image could not be decoded; the file may be damaged.", exception);
        }
    }

    /// <summary>A preview for the web interface (DLV-06): at most <paramref name="size"/> pixels on its longer side, as WebP.</summary>
    public static byte[] Thumbnail(string path, int size)
    {
        try
        {
            using var thumbnail = IsBmp(path)
                ? OpenBmp(path).ThumbnailImage(size, height: size, size: Enums.Size.Down)
                : Image.Thumbnail(path, size, height: size, size: Enums.Size.Down);
            return thumbnail.WebpsaveBuffer(q: 80, keep: Enums.ForeignKeep.None);
        }
        catch (VipsException exception)
        {
            throw new ImageDecodeException("No preview could be made for this image.", exception);
        }
    }

    /// <summary>
    /// A smaller copy <paramref name="width"/> pixels wide in the format of the original (DLV-03); never larger than the
    /// original. Animated images keep all frames. BMP, which libvips cannot write, becomes PNG.
    /// </summary>
    public static EncodedImage Resize(string path, string contentType, int width)
    {
        try
        {
            var animated = contentType is "image/gif" or "image/webp";
            using var resized = IsBmp(path)
                ? OpenBmp(path).ThumbnailImage(width, height: 10_000_000, size: Enums.Size.Down)
                : Image.Thumbnail(animated ? path + "[n=-1]" : path, width, height: 10_000_000, size: Enums.Size.Down);
            return Save(resized, contentType);
        }
        catch (VipsException exception)
        {
            throw new ImageDecodeException("The image could not be resized.", exception);
        }
    }

    /// <summary>Converts an image file to WebP or AVIF for content negotiation (DLV-04).</summary>
    public static EncodedImage Convert(string path, string targetType)
    {
        try
        {
            using var image = IsBmp(path) ? OpenBmp(path) : Image.NewFromFile(path, access: Enums.Access.Sequential);
            return targetType switch
            {
                "image/webp" => new EncodedImage(image.WebpsaveBuffer(q: 80, keep: Enums.ForeignKeep.Icc), targetType),
                "image/avif" => new EncodedImage(
                    image.HeifsaveBuffer(q: 50, compression: Enums.ForeignHeifCompression.Av1, effort: 4, keep: Enums.ForeignKeep.Icc), targetType),
                _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Only WebP and AVIF are produced."),
            };
        }
        catch (VipsException exception)
        {
            throw new ImageDecodeException("The image could not be converted.", exception);
        }
    }

    private static bool IsBmp(string path)
    {
        Span<byte> signature = stackalloc byte[2];
        using var file = File.OpenRead(path);
        return file.Read(signature) == 2 && signature.SequenceEqual("BM"u8);
    }

    private static Image OpenBmp(string path) =>
        BmpReader.Read(File.ReadAllBytes(path)) is { } pixels
            ? Image.NewFromMemory(pixels.Data, pixels.Width, pixels.Height, pixels.Bands, Enums.BandFormat.Uchar).Copy(interpretation: Enums.Interpretation.Srgb)
            : throw new ImageDecodeException("This kind of BMP file cannot be decoded.", new NotSupportedException("BMP variant"));

    private static EncodedImage Save(Image image, string contentType) => contentType switch
    {
        "image/jpeg" => new EncodedImage(image.JpegsaveBuffer(q: 90, keep: Enums.ForeignKeep.Icc), contentType),
        "image/webp" => new EncodedImage(image.WebpsaveBuffer(q: 85, keep: Enums.ForeignKeep.Icc), contentType),
        "image/gif" => new EncodedImage(image.GifsaveBuffer(keep: Enums.ForeignKeep.None), contentType),
        "image/avif" => new EncodedImage(
            image.HeifsaveBuffer(q: 55, compression: Enums.ForeignHeifCompression.Av1, effort: 2, keep: Enums.ForeignKeep.Icc), contentType),
        _ => new EncodedImage(image.PngsaveBuffer(keep: Enums.ForeignKeep.Icc), "image/png"),
    };
}

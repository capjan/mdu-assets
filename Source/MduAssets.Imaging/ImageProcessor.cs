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

    /// <summary>The largest width and height WebP can store.</summary>
    public const int WebPMaxDimension = 16383;

    /// <summary>
    /// How much larger a lossless copy may be than a lossy one and still be chosen: screenshots of interfaces, text and diagrams
    /// stay lossless and sharp, photos (also inside a screenshot) become lossy, where lossless is usually five to ten times larger.
    /// </summary>
    public const double LosslessAllowance = 2.0;

    /// <summary>
    /// The format decided right after upload (UP-11), the sweet spot between size and quality: JPEG becomes lossy WebP; PNG, BMP
    /// and still GIF become lossless WebP unless that is more than <see cref="LosslessAllowance"/> times the lossy size, which
    /// means photographic content. With <paramref name="lossless"/> nothing loses quality: graphics only become lossless WebP and
    /// JPEG stays as it is. Returns <c>null</c> when the image keeps its format: SVG, AVIF and WebP, animations, images with more
    /// than 8 bits per channel or beyond WebP's size limit, and whenever WebP would not be smaller than <paramref name="currentSize"/>.
    /// Orientation is applied; only the ICC profile is kept.
    /// </summary>
    public static EncodedImage? ToWebP(byte[] data, ImageInfo info, bool animated, long currentSize, bool lossless = false)
    {
        var photo = info.Format == ImageFormat.Jpeg;
        if (animated || (photo && lossless) ||
            !(photo || info.Format == ImageFormat.Png || info.Format == ImageFormat.Bmp || info.Format == ImageFormat.Gif))
            return null;
        if (info.Width > WebPMaxDimension || info.Height > WebPMaxDimension)
            return null;

        try
        {
            using var image = info.Format == ImageFormat.Bmp ? DecodeBmp(data) : Image.NewFromBuffer(data);
            if (image.Format != Enums.BandFormat.Uchar)
                return null;
            using var rotated = image.Autorot();
            byte[] content;
            if (photo)
                content = Lossy(rotated);
            else
            {
                content = rotated.WebpsaveBuffer(lossless: true, keep: Enums.ForeignKeep.Icc);
                if (!lossless && Lossy(rotated) is var lossy && content.Length > lossy.Length * LosslessAllowance)
                    content = lossy;
            }
            return content.Length < currentSize ? new EncodedImage(content, "image/webp") : null;
        }
        catch (VipsException exception)
        {
            throw new ImageDecodeException("The image could not be converted.", exception);
        }
    }

    private static byte[] Lossy(Image image) => image.WebpsaveBuffer(q: 82, smartSubsample: true, keep: Enums.ForeignKeep.Icc);

    private static bool IsBmp(string path)
    {
        Span<byte> signature = stackalloc byte[2];
        using var file = File.OpenRead(path);
        return file.Read(signature) == 2 && signature.SequenceEqual("BM"u8);
    }

    private static Image OpenBmp(string path) => DecodeBmp(File.ReadAllBytes(path));

    private static Image DecodeBmp(byte[] data) =>
        BmpReader.Read(data) is { } pixels
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

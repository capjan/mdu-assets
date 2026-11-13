using System.Buffers.Binary;

namespace MduAssets.Imaging;

/// <summary>
/// Detects the image format from the content, never from the file name, and reads the size from the header.
/// Nothing is decoded, so the pixel limit can be checked before any image library sees the file.
/// </summary>
public static class ImageDetector
{
    // UTF-8 literals cannot hold bytes above 0x7F, so these signatures are arrays.
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Returns the format and size of a raster image, or <c>null</c> when the content is no supported raster image.</summary>
    /// <remarks>SVG is text and is recognized by <see cref="SvgSanitizer"/>.</remarks>
    public static ImageInfo? DetectRaster(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature))
            return Png(data);
        if (data.StartsWith(JpegSignature))
            return Jpeg(data);
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
            return Gif(data);
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return WebP(data);
        if (data.Length >= 12 && data[4..8].SequenceEqual("ftyp"u8))
            return Avif(data);
        if (data.StartsWith("BM"u8))
            return Bmp(data);
        return null;
    }

    /// <summary>Whether the content could be SVG: optional byte order mark and whitespace, then <c>&lt;</c>.</summary>
    public static bool LooksLikeXml(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(Utf8ByteOrderMark))
            data = data[3..];
        foreach (var b in data)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                continue;
            return b == (byte)'<';
        }

        return false;
    }

    private static ImageInfo? Png(ReadOnlySpan<byte> data)
    {
        // Signature (8), chunk length (4), "IHDR" (4), width (4), height (4).
        if (data.Length < 24 || !data[12..16].SequenceEqual("IHDR"u8))
            return null;
        return Sized(ImageFormat.Png, BinaryPrimitives.ReadUInt32BigEndian(data[16..]), BinaryPrimitives.ReadUInt32BigEndian(data[20..]));
    }

    private static ImageInfo? Gif(ReadOnlySpan<byte> data) =>
        data.Length < 10
            ? null
            : Sized(ImageFormat.Gif, BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));

    private static ImageInfo? Bmp(ReadOnlySpan<byte> data)
    {
        // BITMAPINFOHEADER and later: width and height as signed 32-bit values; a negative height means top-down rows.
        if (data.Length < 26 || BinaryPrimitives.ReadUInt32LittleEndian(data[14..]) < 40)
            return null;
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        return width <= 0 || height == 0 || height == int.MinValue ? null : Sized(ImageFormat.Bmp, (uint)width, (uint)Math.Abs(height));
    }

    private static ImageInfo? Jpeg(ReadOnlySpan<byte> data)
    {
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position] != 0xFF)
                return null;
            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
            // Start of frame markers carry the size; C4 (DHT), C8 (JPG) and CC (DAC) do not.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (position + 9 > data.Length)
                    return null;
                return Sized(ImageFormat.Jpeg,
                    BinaryPrimitives.ReadUInt16BigEndian(data[(position + 7)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(data[(position + 5)..]));
            }

            if (marker == 0xDA || length < 2)
                return null;
            position += 2 + length;
        }

        return null;
    }

    private static ImageInfo? WebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30)
            return null;
        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Key frame start code 9D 01 2A, then 14-bit width and height.
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
                return null;
            return Sized(ImageFormat.WebP,
                (uint)(BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF),
                (uint)(BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF));
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (data[20] != 0x2F)
                return null;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            return Sized(ImageFormat.WebP, (bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            var width = 1u + data[24] + ((uint)data[25] << 8) + ((uint)data[26] << 16);
            var height = 1u + data[27] + ((uint)data[28] << 8) + ((uint)data[29] << 16);
            return Sized(ImageFormat.WebP, width, height);
        }

        return null;
    }

    private static ImageInfo? Avif(ReadOnlySpan<byte> data)
    {
        // ISO base media file: the ftyp box names "avif" (still) or "avis" (sequence) as major or compatible brand.
        var ftypSize = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), (uint)data.Length);
        if (ftypSize < 16)
            return null;
        var isAvif = false;
        for (var offset = 8; offset + 4 <= ftypSize; offset += 4)
        {
            if (offset == 12)
                continue; // minor version
            var brand = data.Slice(offset, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
                isAvif = true;
        }

        if (!isAvif)
            return null;

        // The size of the primary image is in an "ispe" property: version/flags (4), width (4), height (4).
        var ispe = data.IndexOf("ispe"u8);
        if (ispe < 4 || ispe + 16 > data.Length)
            return null;
        return Sized(ImageFormat.Avif,
            BinaryPrimitives.ReadUInt32BigEndian(data[(ispe + 8)..]),
            BinaryPrimitives.ReadUInt32BigEndian(data[(ispe + 12)..]));
    }

    private static ImageInfo? Sized(ImageFormat format, uint width, uint height) =>
        width is 0 or > int.MaxValue || height is 0 or > int.MaxValue
            ? null
            : new ImageInfo(format, (int)width, (int)height);
}

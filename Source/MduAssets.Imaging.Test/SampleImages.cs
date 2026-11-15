using System.Buffers.Binary;
using NetVips;

namespace MduAssets.Imaging.Test;

/// <summary>Real, decodable images made with libvips, for tests that run the image pipeline. The header-only files are <see cref="TestImages"/>.</summary>
public static class SampleImages
{
    /// <summary>An RGB gradient; different sizes give different content.</summary>
    public static byte[] Png(int width, int height)
    {
        using var image = Picture(width, height);
        return image.PngsaveBuffer(keep: Enums.ForeignKeep.None);
    }

    public static byte[] Jpeg(int width, int height)
    {
        using var image = Picture(width, height);
        return image.JpegsaveBuffer(q: 90, keep: Enums.ForeignKeep.None);
    }

    /// <summary>A JPEG with EXIF orientation 6 (rotate 90°) and a GPS position, as cameras write them.</summary>
    public static byte[] JpegWithExif(int width, int height) => InjectExif(Jpeg(width, height), BuildExif());

    /// <summary>A GIF with <paramref name="frames"/> frames of different brightness.</summary>
    public static byte[] AnimatedGif(int width, int height, int frames)
    {
        var pages = Enumerable.Range(0, frames)
            .Select(i => (Image.Black(width, height, bands: 3) + (i * 60 % 255)).Cast(Enums.BandFormat.Uchar))
            .ToArray();
        using var strip = Image.Arrayjoin(pages, across: 1);
        using var animated = strip.Mutate(m =>
        {
            m.Set(GValue.GIntType, "page-height", height);
            m.Set(GValue.ArrayIntType, "delay", Enumerable.Repeat(100, frames).ToArray());
        });
        var gif = animated.GifsaveBuffer(keep: Enums.ForeignKeep.None);
        foreach (var page in pages)
            page.Dispose();
        return gif;
    }

    /// <summary>An uncompressed 24-bit BMP with a solid color; libvips cannot read BMP, so it is written by hand.</summary>
    public static byte[] Bmp(int width, int height)
    {
        var stride = (width * 3 + 3) / 4 * 4;
        var data = new byte[54 + stride * height];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), stride * height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                data[54 + y * stride + x * 3] = 200;
        return data;
    }

    /// <summary>Width and height of an encoded image and how many frames it has.</summary>
    public static (int Width, int Height, int Frames) Measure(byte[] data)
    {
        var loader = Image.FindLoadBuffer(data) ?? string.Empty;
        using var image = Image.NewFromBuffer(data, loader.Contains("gif", StringComparison.Ordinal) || loader.Contains("webp", StringComparison.Ordinal) ? "n=-1" : "");
        var frames = image.Contains("n-pages") ? (int)image.Get("n-pages") : 1;
        return (image.Width, image.Height / frames, frames);
    }

    /// <summary>The metadata fields of an encoded image.</summary>
    public static IReadOnlyList<string> Fields(byte[] data)
    {
        using var image = Image.NewFromBuffer(data);
        return image.GetFields();
    }

    private static Image Picture(int width, int height)
    {
        using var x = Image.Xyz(width, height);
        using var red = x[0] * (255.0 / Math.Max(1, width));
        using var green = x[1] * (255.0 / Math.Max(1, height));
        using var blue = Image.Black(width, height) + 120;
        using var joined = red.Bandjoin(green, blue);
        return joined.Cast(Enums.BandFormat.Uchar).Copy(interpretation: Enums.Interpretation.Srgb);
    }

    // Minimal little-endian EXIF block: IFD0 { Orientation=6, GPSInfo -> GPS IFD { GPSLatitudeRef="N" } } (from the NetVips spike).
    private static byte[] BuildExif()
    {
        var tiff = new byte[8 + 2 + 2 * 12 + 4 + 2 + 12 + 4];
        tiff[0] = (byte)'I';
        tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        var p = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(p), 2);
        p += 2;
        WriteEntry(tiff, ref p, 0x0112, 3, 1, 6);
        WriteEntry(tiff, ref p, 0x8825, 4, 1, 8 + 2 + 2 * 12 + 4);
        p += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(p), 1);
        p += 2;
        WriteEntry(tiff, ref p, 0x0001, 2, 2, 'N');
        return [.. "Exif\0\0"u8, .. tiff];

        static void WriteEntry(byte[] b, ref int p, ushort tag, ushort type, uint count, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p + 4), count);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p + 8), value);
            p += 12;
        }
    }

    private static byte[] InjectExif(byte[] jpeg, byte[] exif)
    {
        var length = exif.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. exif];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }
}

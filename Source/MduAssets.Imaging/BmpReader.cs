using System.Buffers.Binary;

namespace MduAssets.Imaging;

/// <summary>
/// Reads uncompressed 24-bit and 32-bit BMP files into RGB(A) pixels. libvips as shipped by NetVips.Native has no BMP
/// loader, so previews, widths and conversions of BMP uploads start from these pixels.
/// </summary>
public static class BmpReader
{
    public sealed record Pixels(byte[] Data, int Width, int Height, int Bands);

    /// <summary>The pixels, or <c>null</c> for BMP variants this reader does not handle (palettes, RLE, other headers).</summary>
    public static Pixels? Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 54 || !file.StartsWith("BM"u8))
            return null;
        var offset = BinaryPrimitives.ReadInt32LittleEndian(file[10..]);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(file[14..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(file[18..]);
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(file[22..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(file[28..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(file[30..]);
        if (headerSize < 40 || width <= 0 || rawHeight == 0 || bits is not (24 or 32) || compression is not (0 or 3))
            return null;

        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);
        var bytesPerPixel = bits / 8;
        var stride = (width * bytesPerPixel + 3) / 4 * 4;
        if ((long)width * height > 100_000_000 || offset < 54 || offset + (long)stride * height > file.Length)
            return null;

        var bands = bits == 32 ? 4 : 3;
        var data = new byte[width * height * bands];
        for (var y = 0; y < height; y++)
        {
            var row = file.Slice(offset + (topDown ? y : height - 1 - y) * stride, width * bytesPerPixel);
            for (var x = 0; x < width; x++)
            {
                var source = x * bytesPerPixel;
                var target = (y * width + x) * bands;
                data[target] = row[source + 2];
                data[target + 1] = row[source + 1];
                data[target + 2] = row[source];
                if (bands == 4)
                    data[target + 3] = row[source + 3];
            }
        }

        // Many 32-bit BMPs leave alpha at zero; treat a fully transparent image as opaque.
        if (bands == 4 && data.Where((_, i) => i % 4 == 3).All(a => a == 0))
            for (var i = 3; i < data.Length; i += 4)
                data[i] = 255;

        return new Pixels(data, width, height, bands);
    }
}

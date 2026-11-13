using System.Buffers.Binary;
using System.Text;

namespace MduAssets.Imaging.Test;

/// <summary>Minimal file headers of each format, enough for detection.</summary>
public static class TestImages
{
    public static byte[] Png(uint width, uint height)
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(data, 0);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), height);
        data[24] = 8;
        data[25] = 2;
        return data;
    }

    public static byte[] Gif(ushort width, ushort height)
    {
        var data = new byte[13];
        "GIF89a"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), height);
        return data;
    }

    public static byte[] Bmp(int width, int height)
    {
        var data = new byte[54];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        return data;
    }

    /// <summary>SOI, an APP0 segment and a baseline SOF0 frame header.</summary>
    public static byte[] Jpeg(ushort width, ushort height)
    {
        var data = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        data.AddRange("JFIF\0"u8.ToArray());
        data.AddRange(new byte[9]);
        data.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08 });
        data.AddRange(new[] { (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width });
        data.AddRange(new byte[10]);
        return data.ToArray();
    }

    public static byte[] WebPLossless(int width, int height)
    {
        var data = new byte[30];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        "VP8L"u8.CopyTo(data.AsSpan(12));
        data[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(21), (uint)(width - 1) | ((uint)(height - 1) << 14));
        return data;
    }

    public static byte[] WebPExtended(int width, int height)
    {
        var data = new byte[30];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        "VP8X"u8.CopyTo(data.AsSpan(12));
        WriteUInt24(data.AsSpan(24), width - 1);
        WriteUInt24(data.AsSpan(27), height - 1);
        return data;

        static void WriteUInt24(Span<byte> target, int value)
        {
            target[0] = (byte)value;
            target[1] = (byte)(value >> 8);
            target[2] = (byte)(value >> 16);
        }
    }

    /// <summary>An ftyp box with the given major brand and an ispe property.</summary>
    public static byte[] Heif(string brand, uint width, uint height)
    {
        var data = new List<byte>();
        data.AddRange(BigEndian(24));
        data.AddRange("ftyp"u8.ToArray());
        data.AddRange(Encoding.ASCII.GetBytes(brand));
        data.AddRange(new byte[4]);
        data.AddRange("mif1"u8.ToArray());
        data.AddRange(Encoding.ASCII.GetBytes(brand));
        data.AddRange(BigEndian(20));
        data.AddRange("ispe"u8.ToArray());
        data.AddRange(new byte[4]);
        data.AddRange(BigEndian(width));
        data.AddRange(BigEndian(height));
        return data.ToArray();

        static byte[] BigEndian(uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return bytes;
        }
    }

    public static byte[] Svg(string content) => Encoding.UTF8.GetBytes(content);
}

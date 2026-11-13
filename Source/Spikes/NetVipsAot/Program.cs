// Spike: does NetVips/libvips work under .NET Native AOT for the operations the
// MDU Asset Server needs (UP-06, UP-08, DLV-03, DLV-04, DLV-06)?
// Every check prints PASS/FAIL; the exit code is the number of failures.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using NetVips;

var results = new List<(string Name, bool Ok, string Detail)>();

void Check(string name, Func<string> body)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var detail = body();
        results.Add((name, true, $"{detail} ({sw.ElapsedMilliseconds} ms)"));
    }
    catch (Exception ex)
    {
        results.Add((name, false, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}"));
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string Loader(byte[] buffer) => Image.FindLoadBuffer(buffer) ?? "(none)";

Console.WriteLine($"Runtime:  {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}, " +
                  $"NativeAOT={!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled}");

Check("init libvips", () =>
{
    Require(ModuleInitializer.VipsInitialized, ModuleInitializer.Exception?.Message ?? "not initialized");
    return $"libvips {NetVips.NetVips.Version(0)}.{NetVips.NetVips.Version(1)}.{NetVips.NetVips.Version(2)}";
});

Check("savers available", () =>
{
    string[] savers = ["pngsave_buffer", "jpegsave_buffer", "webpsave_buffer", "heifsave_buffer", "gifsave_buffer"];
    string[] loaders = ["pngload_buffer", "jpegload_buffer", "webpload_buffer", "heifload_buffer", "gifload_buffer", "svgload_buffer"];
    var missing = savers.Concat(loaders).Where(n => NetVips.NetVips.TypeFind("VipsOperation", n) == IntPtr.Zero).ToList();
    Require(missing.Count == 0, "missing: " + string.Join(", ", missing));
    return "png/jpeg/webp/heif/gif save+load, svg load";
});

// A 1600x900 RGB test picture with a gradient, shapes and photo-like noise,
// tagged with an sRGB ICC profile, so encoders and metadata handling have real work.
using var gradient = (Image.Xyz(1600, 900)[0] * 0.15).Bandjoin(Image.Xyz(1600, 900)[1] * 0.28, Image.Black(1600, 900) + 120);
using var noise = Image.Gaussnoise(1600, 900, sigma: 18, mean: 0);
using var source = (gradient + noise.Bandjoin(noise, noise))
    .Cast(Enums.BandFormat.Uchar)
    .Copy(interpretation: Enums.Interpretation.Srgb)
    .IccTransform("srgb", inputProfile: "srgb", embedded: false);
Console.WriteLine($"Source:   {source.Width}x{source.Height}, icc={source.Contains("icc-profile-data")}");
var png = source.PngsaveBuffer();

Check("detect type by content (UP-06)", () =>
{
    Require(Loader(png) == "VipsForeignLoadPngBuffer", Loader(png));
    var fake = Encoding.ASCII.GetBytes("<html>not an image</html>");
    Require(Image.FindLoadBuffer(fake) is null, "html detected as " + Loader(fake));
    return "png detected, html rejected";
});

Check("header-only read for pixel limit (OPS-01)", () =>
{
    using var header = Image.NewFromBuffer(png, access: Enums.Access.Sequential);
    Require(header.Width == 1600 && header.Height == 900, $"{header.Width}x{header.Height}");
    return $"{header.Width}x{header.Height}, {header.Bands} bands";
});

Check("invalid data raises VipsException", () =>
{
    try
    {
        using var broken = Image.NewFromBuffer(png[..200]);
        broken.PngsaveBuffer();
    }
    catch (VipsException)
    {
        return "VipsException caught";
    }
    throw new InvalidOperationException("no exception for truncated png");
});

// JPEG with EXIF orientation 6 (rotate 90°) and a GPS IFD, like a phone photo.
var jpegWithExif = InjectExif(source.JpegsaveBuffer(q: 90), BuildExif());

Check("strip EXIF/GPS, apply orientation, keep ICC (UP-08)", () =>
{
    using var original = Image.NewFromBuffer(jpegWithExif);
    Require(original.Contains("exif-data"), "test image has no EXIF");
    Require(original.Contains("exif-ifd3-GPSLatitudeRef"), "test image has no GPS");
    Require(original.Get("orientation") is int o && o == 6, "orientation not read");

    using var rotated = original.Autorot();
    var cleaned = rotated.JpegsaveBuffer(q: 90, keep: Enums.ForeignKeep.Icc);
    using var reloaded = Image.NewFromBuffer(cleaned);
    Require(!reloaded.Contains("exif-data"), "EXIF still present");
    Require(!reloaded.GetFields().Any(f => f.StartsWith("exif-ifd3", StringComparison.Ordinal)), "GPS still present");
    Require(reloaded.Width == 900 && reloaded.Height == 1600, $"not rotated: {reloaded.Width}x{reloaded.Height}");
    Require(reloaded.Contains("icc-profile-data"), "ICC profile lost");
    return $"{jpegWithExif.Length} -> {cleaned.Length} bytes, 900x1600";
});

Check("thumbnail 320 (DLV-06)", () =>
{
    using var thumb = Image.ThumbnailBuffer(png, 320);
    var webp = thumb.WebpsaveBuffer(q: 80);
    Require(thumb.Width == 320, $"width {thumb.Width}");
    return $"320x{thumb.Height}, {webp.Length} bytes webp";
});

Check("resize ?w=800, never upscale (DLV-03)", () =>
{
    using var small = Image.ThumbnailBuffer(png, 800, height: 10_000_000, size: Enums.Size.Down);
    using var notUp = Image.ThumbnailBuffer(png, 4000, height: 10_000_000, size: Enums.Size.Down);
    Require(small.Width == 800 && small.Height == 450, $"{small.Width}x{small.Height}");
    Require(notUp.Width == 1600, $"upscaled to {notUp.Width}");
    return "800x450, 4000 stays 1600";
});

Check("encode WebP (DLV-04)", () =>
{
    var webp = source.WebpsaveBuffer(q: 80, keep: Enums.ForeignKeep.None);
    Require(Loader(webp) == "VipsForeignLoadWebpBuffer", Loader(webp));
    return $"{png.Length} png -> {webp.Length} webp";
});

Check("encode AVIF (DLV-04)", () =>
{
    var avif = source.HeifsaveBuffer(q: 50, compression: Enums.ForeignHeifCompression.Av1,
        effort: 4, keep: Enums.ForeignKeep.None);
    Require(Loader(avif) == "VipsForeignLoadHeifBuffer", Loader(avif));
    using var back = Image.NewFromBuffer(avif);
    Require(back.Width == 1600, $"width {back.Width}");
    return $"{png.Length} png -> {avif.Length} avif";
});

Check("decode AVIF upload (UP-06)", () =>
{
    var avif = source.HeifsaveBuffer(q: 60, compression: Enums.ForeignHeifCompression.Av1, effort: 2);
    using var thumb = Image.ThumbnailBuffer(avif, 320);
    return $"avif -> {thumb.Width}x{thumb.Height}";
});

Check("animated GIF keeps animation on resize", () =>
{
    var frames = Enumerable.Range(0, 4)
        .Select(i => (Image.Black(200, 100, bands: 3) + (i * 60)).Cast(Enums.BandFormat.Uchar))
        .ToArray();
    using var strip = Image.Arrayjoin(frames, across: 1);
    using var animated = strip.Mutate(m =>
    {
        m.Set(GValue.GIntType, "page-height", 100);
        m.Set(GValue.ArrayIntType, "delay", new[] { 100, 100, 100, 100 });
    });
    var gif = animated.GifsaveBuffer();
    using var thumb = Image.ThumbnailBuffer(gif, 100, optionString: "n=-1");
    var resized = thumb.GifsaveBuffer();
    using var back = Image.NewFromBuffer(resized, "n=-1");
    var pages = back.Contains("n-pages") ? (int)back.Get("n-pages") : 1;
    Require(pages == 4, $"pages {pages}");
    foreach (var f in frames) f.Dispose();
    return $"4 frames kept, {gif.Length} -> {resized.Length} bytes";
});

Check("SVG render for thumbnail", () =>
{
    var svg = Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='400' height='200'>" +
        "<rect width='400' height='200' fill='#06c'/><circle cx='200' cy='100' r='80' fill='#fff'/></svg>");
    Require(Loader(svg) == "VipsForeignLoadSvgBuffer", Loader(svg));
    using var thumb = Image.ThumbnailBuffer(svg, 320);
    return $"{thumb.Width}x{thumb.Height}";
});

Check("SVG with file:// reference renders without error", () =>
{
    // Only shows that rendering does not fail; whether librsvg reads the file is not
    // verified here. Uploaded SVGs are sanitized before any rendering (UP-07).
    var svg = Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='10' height='10'>" +
        "<image width='10' height='10' xlink:href='file:///etc/hosts'/></svg>");
    using var img = Image.NewFromBuffer(svg);
    img.PngsaveBuffer();
    return "rendered without error (sanitizer still required, see UP-07)";
});

Check("parallel throughput (8 x thumbnail+webp)", () =>
{
    var sw = Stopwatch.StartNew();
    Parallel.For(0, 8, _ =>
    {
        using var t = Image.ThumbnailBuffer(png, 800);
        t.WebpsaveBuffer(q: 80);
    });
    return $"{sw.ElapsedMilliseconds} ms total";
});

Check("cache limits configurable", () =>
{
    Cache.Max = 0;
    Cache.MaxMem = 0;
    Cache.MaxFiles = 0;
    NetVips.NetVips.Concurrency = Math.Max(1, Environment.ProcessorCount / 2);
    return $"op cache off, concurrency {NetVips.NetVips.Concurrency}";
});

Console.WriteLine($"Memory:   {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB working set");
Console.WriteLine();
foreach (var (name, ok, detail) in results)
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-52} {detail}");
var failures = results.Count(r => !r.Ok);
Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures;

// --- test data helpers -------------------------------------------------------

// Minimal little-endian EXIF block: IFD0 { Orientation=6, GPSInfo -> GPS IFD { GPSLatitudeRef="N" } }.
static byte[] BuildExif()
{
    var tiff = new byte[8 + 2 + 2 * 12 + 4 + 2 + 12 + 4];
    tiff[0] = (byte)'I'; tiff[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
    BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
    var p = 8;
    BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(p), 2); p += 2;
    WriteEntry(tiff, ref p, 0x0112, 3, 1, 6);           // Orientation SHORT
    var gpsOffset = (uint)(8 + 2 + 2 * 12 + 4);
    WriteEntry(tiff, ref p, 0x8825, 4, 1, gpsOffset);   // GPSInfo LONG
    p += 4;                                             // next IFD = 0
    BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(p), 1); p += 2;
    WriteEntry(tiff, ref p, 0x0001, 2, 2, 'N');         // GPSLatitudeRef ASCII "N\0"
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

// Insert an APP1 segment right after the JPEG SOI marker.
static byte[] InjectExif(byte[] jpeg, byte[] exif)
{
    var length = exif.Length + 2;
    byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. exif];
    return [.. jpeg[..2], .. app1, .. jpeg[2..]];
}

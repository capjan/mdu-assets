using System.Runtime.InteropServices;

namespace MduAssets;

/// <summary>
/// Keeps the native memory of libvips small on glibc (NF-07). libvips works on images in many threads, glibc gives
/// threads their own malloc arenas, and freed memory in them rarely goes back to the system: one upload left the idle
/// server at about 130 MB instead of about 60 MB. Fewer arenas and a trim after image work keep it low.
/// Elsewhere (macOS, musl) both calls do nothing.
/// </summary>
internal static class GlibcMemory
{
    private const int ArenaMax = -8; // M_ARENA_MAX in malloc.h
    private static bool _available = OperatingSystem.IsLinux();

    /// <summary>Limits new malloc arenas to two, unless <c>MALLOC_ARENA_MAX</c> is set; call it first thing.</summary>
    public static void LimitArenas(int arenas = 2)
    {
        if (Environment.GetEnvironmentVariable("MALLOC_ARENA_MAX") is null)
            Call(() => Mallopt(ArenaMax, arenas));
    }

    /// <summary>Returns free memory of all arenas to the system; takes a few milliseconds.</summary>
    public static void Trim() => Call(() => MallocTrim(0));

    private static void Call(Func<int> call)
    {
        if (!_available)
            return;
        try
        {
            call();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _available = false;
        }
    }

    // Blittable arguments only, so plain DllImport needs no marshalling code and no unsafe blocks under Native AOT.
    [DllImport("libc.so.6", EntryPoint = "mallopt")]
    private static extern int Mallopt(int parameter, int value);

    [DllImport("libc.so.6", EntryPoint = "malloc_trim")]
    private static extern int MallocTrim(nuint pad);
}

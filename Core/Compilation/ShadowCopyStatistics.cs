#nullable enable
using System.Threading;

namespace T3.Core.Compilation;

/// <summary>
/// Process-wide shadow-copy cost for the startup timing summary. Deliberately a standalone holder:
/// putting these counters on <see cref="TixlAssemblyLoadContext"/> made reading the summary run that
/// type's heavy static constructor, so a broken install (missing dependency DLL) crashed at the
/// logging line instead of at the actual package load.
/// </summary>
internal static class ShadowCopyStatistics
{
    public static long Bytes => Interlocked.Read(ref _bytes);
    public static long Milliseconds => Interlocked.Read(ref _milliseconds);

    public static void AddBytes(long bytes) => Interlocked.Add(ref _bytes, bytes);
    public static void AddMilliseconds(long ms) => Interlocked.Add(ref _milliseconds, ms);

    private static long _bytes;
    private static long _milliseconds;
}

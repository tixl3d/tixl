#nullable enable
using T3.Graphics.Compat;
using T3.Core.Stats;

namespace T3.Editor.Gui.Windows.Analyze;

/// <summary>
/// Measures the GPU time of each editor frame with timestamp queries placed at the frame's start and just before
/// the main window's Present, and records it into <see cref="PerformanceMetrics.GpuFrameDuration"/>.
/// Results are read a few frames later without blocking or flushing; a slot whose result isn't ready by the time
/// it comes round again is dropped instead of waited for.
/// </summary>
internal static class GpuFrameTimer
{
    private sealed class QuerySet
    {
        public required Query Disjoint;
        public required Query FrameStart;
        public required Query FrameEnd;
        public bool IsPending;
    }

    public static void Initialize(Device device, DeviceContext context)
    {
        _context = context;
        _querySets = new QuerySet[QuerySetCount];
        for (var index = 0; index < QuerySetCount; index++)
        {
            _querySets[index] = new QuerySet
                                    {
                                        Disjoint = new Query(device, new QueryDescription { Type = QueryType.TimestampDisjoint }),
                                        FrameStart = new Query(device, new QueryDescription { Type = QueryType.Timestamp }),
                                        FrameEnd = new Query(device, new QueryDescription { Type = QueryType.Timestamp }),
                                    };
        }
    }

    /// <summary>Call before the frame's first GPU command.</summary>
    public static void BeginFrame()
    {
        if (_context == null || _querySets == null || _isFrameOpen)
            return;

        CollectFinishedResults();

        var set = _querySets[_nextSetIndex];
        set.IsPending = false; // An unread result from four frames ago is dropped rather than waited for.
        _context.Begin(set.Disjoint);
        _context.End(set.FrameStart);
        _isFrameOpen = true;
    }

    /// <summary>Call after the frame's last GPU command, before presenting.</summary>
    public static void EndFrame()
    {
        if (_context == null || _querySets == null || !_isFrameOpen)
            return;

        var set = _querySets[_nextSetIndex];
        _context.End(set.FrameEnd);
        _context.End(set.Disjoint);
        set.IsPending = true;
        _isFrameOpen = false;
        _nextSetIndex = (_nextSetIndex + 1) % QuerySetCount;
    }

    public static void Release()
    {
        if (_querySets == null)
            return;

        foreach (var set in _querySets)
        {
            set.Disjoint.Dispose();
            set.FrameStart.Dispose();
            set.FrameEnd.Dispose();
        }

        _querySets = null;
        _context = null;
    }

    /// <summary>Reads results oldest first and stops at the first one the GPU hasn't finished.</summary>
    private static void CollectFinishedResults()
    {
        for (var offset = 0; offset < QuerySetCount; offset++)
        {
            var set = _querySets![(_nextSetIndex + offset) % QuerySetCount];
            if (!set.IsPending)
                continue;

            if (!_context!.GetData(set.Disjoint, AsynchronousFlags.DoNotFlush, out QueryDataTimestampDisjoint disjoint)
                || !_context.GetData(set.FrameStart, AsynchronousFlags.DoNotFlush, out long startTicks)
                || !_context.GetData(set.FrameEnd, AsynchronousFlags.DoNotFlush, out long endTicks))
                return;

            set.IsPending = false;

            // Disjoint means the GPU clock changed during the frame (power state, driver reset); the ticks are meaningless.
            if (disjoint.Disjoint || disjoint.Frequency == 0 || endTicks < startTicks)
                continue;

            // A stall on the main thread (loading, compiling) lands between the two timestamps as GPU idle time.
            var gpuMs = (endTicks - startTicks) * 1000.0 / disjoint.Frequency;
            if (gpuMs > MaxPlausibleGpuFrameMs)
                continue;

            PerformanceMetrics.RecordGpuFrame((float)gpuMs);
        }
    }

    /** Enough for the GPU to finish a frame before its slot is reused at frame latency 1–3. */
    private const int QuerySetCount = 4;

    private const double MaxPlausibleGpuFrameMs = 250;

    private static DeviceContext? _context;
    private static QuerySet[]? _querySets;
    private static int _nextSetIndex;
    private static bool _isFrameOpen;
}

#nullable enable
using System.Runtime.InteropServices;
using SharpDX.DXGI;
using T3.Core.Logging;

namespace T3.Editor.Gui.Windows.Analyze;

/// <summary>
/// Reports how Windows presents the main swap chain: composed by DWM, as a hardware overlay, or by independent flip,
/// and how many presented frames are still queued for display. Frame pacing differs between these modes, so this is
/// the first thing to check when frame intervals jitter. DXGI only.
/// </summary>
internal static class PresentationDiagnostics
{
    /** Native DXGI_FRAME_STATISTICS_MEDIA. */
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFrameStatisticsMedia
    {
        public uint PresentCount;
        public uint PresentRefreshCount;
        public uint SyncRefreshCount;
        public long SyncQpcTime;
        public long SyncGpuTime;
        public int CompositionMode;
        public uint ApprovedPresentDuration;
    }

    /// <summary>Unknown until the swap chain has reported statistics (the first presents report none).</summary>
    public static FramePresentationMode? CompositionMode { get; private set; }

    /// <summary>Frames handed to Present that the display hasn't shown yet; -1 when unknown.</summary>
    public static int QueuedFrames { get; private set; } = -1;

    /// <summary>Call once per frame after presenting the main swap chain.</summary>
    public static void Update(SwapChain swapChain)
    {
        if (!TryGetFrameStatisticsMedia(swapChain, out var statistics))
        {
            QueuedFrames = -1;
            return;
        }

        QueuedFrames = swapChain.LastPresentCount - (int)statistics.PresentCount;

        // TEMP probe: is "queued" a real queue (fresh statistics) or stale bookkeeping (old SyncQpcTime)?
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now - _lastProbeTimestamp > System.Diagnostics.Stopwatch.Frequency)
        {
            _lastProbeTimestamp = now;
            var statisticsAgeMs = (now - statistics.SyncQpcTime) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            var refreshesSinceProbe = statistics.SyncRefreshCount - _lastProbeSyncRefreshCount;
            _lastProbeSyncRefreshCount = statistics.SyncRefreshCount;
            Log.Debug($"PresentProbe: last={swapChain.LastPresentCount} shown={statistics.PresentCount} queued={QueuedFrames} "
                      + $"presentRefresh={statistics.PresentRefreshCount} syncRefresh={statistics.SyncRefreshCount} "
                      + $"(+{refreshesSinceProbe}/s) statsAge={statisticsAgeMs:0.0}ms mode={Describe((FramePresentationMode)statistics.CompositionMode)}");
        }

        var mode = (FramePresentationMode)statistics.CompositionMode;
        if (mode == CompositionMode)
            return;

        Log.Debug($"Main window presentation changed from {Describe(CompositionMode)} to {Describe(mode)}");
        CompositionMode = mode;
    }

    public static string Describe(FramePresentationMode? mode)
    {
        return mode switch
                   {
                       FramePresentationMode.Composed           => "composed by DWM",
                       FramePresentationMode.Overlay            => "hardware overlay",
                       FramePresentationMode.None               => "independent flip",
                       FramePresentationMode.CompositionFailure => "composition failure",
                       _                                        => "unknown"
                   };
    }

    /// <summary>
    /// SharpDX 4.2 has no IDXGISwapChainMedia wrapper, so GetFrameStatisticsMedia is called through the interface's
    /// vtable. Fails (e.g. DXGI_ERROR_FRAME_STATISTICS_DISJOINT) until the swap chain has presented a few frames.
    /// </summary>
    private static unsafe bool TryGetFrameStatisticsMedia(SwapChain swapChain, out NativeFrameStatisticsMedia statistics)
    {
        statistics = default;
        if (Marshal.QueryInterface(swapChain.NativePointer, in SwapChainMediaInterfaceId, out var swapChainMedia) != 0
            || swapChainMedia == IntPtr.Zero)
            return false;

        try
        {
            var vtable = *(IntPtr**)swapChainMedia;
            var getFrameStatisticsMedia = (delegate* unmanaged[Stdcall]<IntPtr, NativeFrameStatisticsMedia*, int>)vtable[GetFrameStatisticsMediaVtableIndex];
            NativeFrameStatisticsMedia result;
            if (getFrameStatisticsMedia(swapChainMedia, &result) != 0)
                return false;

            statistics = result;
            return true;
        }
        finally
        {
            Marshal.Release(swapChainMedia);
        }
    }

    private static readonly Guid SwapChainMediaInterfaceId = new("dd95b90b-f05f-4f6a-bd65-25bfb264bd84");

    /** IUnknown's three methods precede it. */
    private const int GetFrameStatisticsMediaVtableIndex = 3;

    private static long _lastProbeTimestamp;
    private static uint _lastProbeSyncRefreshCount;
}

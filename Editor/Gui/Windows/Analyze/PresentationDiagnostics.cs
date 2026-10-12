#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpDX.DXGI;
using T3.Core.Logging;
using T3.Core.Stats;

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

        var mode = (FramePresentationMode)statistics.CompositionMode;
        if (mode == CompositionMode)
            return;

        Log.Debug($"Main window presentation changed from {Describe(CompositionMode)} to {Describe(mode)}");
        CompositionMode = mode;
    }

    /// <summary>
    /// Measures the refresh period of the display that paces the loop from its vblank count and timestamps, and hands
    /// it to <see cref="FrameTiming"/>. Call once per frame after presenting, with the pacing window's swap chain.
    /// </summary>
    public static void MeasureRefreshPeriod(SwapChain pacingSwapChain)
    {
        if (!TryGetFrameStatisticsMedia(pacingSwapChain, out var statistics) || statistics.SyncQpcTime == 0)
            return;

        if (pacingSwapChain.NativePointer != _measuredSwapChain)
        {
            _measuredSwapChain = pacingSwapChain.NativePointer;
            _baselineVblankQpc = statistics.SyncQpcTime;
            _baselineVblankCount = statistics.SyncRefreshCount;
            return;
        }

        var elapsedTicks = statistics.SyncQpcTime - _baselineVblankQpc;
        if (elapsedTicks < Stopwatch.Frequency * RefreshMeasureIntervalSec)
            return;

        var vblanks = statistics.SyncRefreshCount - _baselineVblankCount;
        _baselineVblankQpc = statistics.SyncQpcTime;
        _baselineVblankCount = statistics.SyncRefreshCount;
        if (vblanks == 0)
            return;

        var periodSec = elapsedTicks / (double)Stopwatch.Frequency / vblanks;
        if (periodSec is < MinPlausibleRefreshPeriodSec or > MaxPlausibleRefreshPeriodSec)
            return;

        FrameTiming.SetRefreshPeriodFromDisplay(periodSec);
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

    private const double RefreshMeasureIntervalSec = 1;
    private const double MinPlausibleRefreshPeriodSec = 1.0 / 500;
    private const double MaxPlausibleRefreshPeriodSec = 1.0 / 20;

    private static IntPtr _measuredSwapChain;
    private static long _baselineVblankQpc;
    private static uint _baselineVblankCount;
}

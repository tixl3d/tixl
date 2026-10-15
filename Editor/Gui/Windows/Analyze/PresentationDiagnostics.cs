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

    /// <summary>
    /// Whether the pacing window sits on the primary display, whose clock the frame-latency waitable follows while
    /// DWM composes. On any other display the loop has to wait for that display's own vblanks. True until known.
    /// </summary>
    public static bool IsPacingWindowOnPrimaryDisplay { get; private set; } = true;

    /// <summary>Call once per frame after presenting the main swap chain.</summary>
    public static void Update(SwapChain swapChain)
    {
        if (!TryGetFrameStatisticsMedia(swapChain, out var statistics))
        {
            QueuedFrames = -1;
            return;
        }

        // While DWM composes a window on a secondary display the statistics can stop updating for seconds, and the
        // difference would count every present since then as queued.
        var statisticsAgeTicks = Stopwatch.GetTimestamp() - statistics.SyncQpcTime;
        QueuedFrames = statisticsAgeTicks > Stopwatch.Frequency * MaxStatisticsAgeSec
                           ? -1
                           : swapChain.LastPresentCount - (int)statistics.PresentCount;

        var mode = (FramePresentationMode)statistics.CompositionMode;
        if (mode == CompositionMode)
            return;

        Log.Debug($"Main window presentation changed from {Describe(CompositionMode)} to {Describe(mode)}");
        CompositionMode = mode;
    }

    /// <summary>
    /// Measures the refresh period of the display that paces the loop from its vblank count and timestamps, and hands
    /// it to <see cref="FrameTiming"/>. Call once per frame after presenting, with the pacing window.
    /// </summary>
    /// <remarks>
    /// While DWM composes a window on a secondary display, the swap chain's statistics follow the composition clock
    /// of the primary display (measured: 119.5 Hz for a window on a 60 Hz monitor next to a 120 Hz panel). The rate
    /// Windows reports for the window's monitor catches that; the measurement is kept when both agree, because it
    /// is the precise one (59.94 vs 60 Hz).
    /// </remarks>
    public static void MeasureRefreshPeriod(SwapChain pacingSwapChain, IntPtr windowHandle)
    {
        var now = Stopwatch.GetTimestamp();
        if (pacingSwapChain.NativePointer != _measuredSwapChain)
        {
            _measuredSwapChain = pacingSwapChain.NativePointer;
            _baselineVblankQpc = 0;
            _hasTriedVblankBaseline = false;
            _lastMeasureTimestamp = now;
            return;
        }

        if (now - _lastMeasureTimestamp < Stopwatch.Frequency * RefreshMeasureIntervalSec)
            return;

        _lastMeasureTimestamp = now;

        // The first sample of a swap chain only sets the baseline the next one measures against.
        var isFirstSample = _baselineVblankQpc == 0 && !_hasTriedVblankBaseline;
        _hasTriedVblankBaseline = true;
        var measuredPeriodSec = MeasureVblankPeriod(pacingSwapChain);
        if (isFirstSample)
            return;

        var displayRefreshRate = GetDisplayRefreshRate(windowHandle, out var isOnPrimaryDisplay);
        IsPacingWindowOnPrimaryDisplay = isOnPrimaryDisplay;
        var measurementMatchesDisplay = measuredPeriodSec > 0
                                        && (displayRefreshRate <= 0
                                            || Math.Abs(measuredPeriodSec * displayRefreshRate - 1) <= MaxRefreshRateMismatch);
        if (measurementMatchesDisplay)
        {
            _isUsingReportedRefreshRate = false;
            FrameTiming.SetRefreshPeriodFromDisplay(measuredPeriodSec);
            return;
        }

        if (displayRefreshRate <= 0)
            return;

        if (!_isUsingReportedRefreshRate)
        {
            Log.Debug(measuredPeriodSec > 0
                          ? $"Vblank statistics report {1 / measuredPeriodSec:0.00} Hz, but the display runs at {displayRefreshRate:0.00} Hz; using the display's rate"
                          : $"No vblank statistics; using the display's reported {displayRefreshRate:0.00} Hz");
            _isUsingReportedRefreshRate = true;
        }

        FrameTiming.SetRefreshPeriodFromDisplay(1 / displayRefreshRate);
    }

    /** The vblank period since the previous call, from the swap chain's frame statistics; 0 when unknown. */
    private static double MeasureVblankPeriod(SwapChain swapChain)
    {
        if (!TryGetFrameStatisticsMedia(swapChain, out var statistics) || statistics.SyncQpcTime == 0)
            return 0;

        var elapsedTicks = statistics.SyncQpcTime - _baselineVblankQpc;
        var vblanks = statistics.SyncRefreshCount - _baselineVblankCount;
        var hadBaseline = _baselineVblankQpc != 0;
        _baselineVblankQpc = statistics.SyncQpcTime;
        _baselineVblankCount = statistics.SyncRefreshCount;
        if (!hadBaseline || vblanks == 0 || elapsedTicks <= 0)
            return 0;

        var periodSec = elapsedTicks / (double)Stopwatch.Frequency / vblanks;
        return periodSec is < MinPlausibleRefreshPeriodSec or > MaxPlausibleRefreshPeriodSec ? 0 : periodSec;
    }

    /// <summary>
    /// The refresh rate Windows reports for the monitor a window is on; 0 when unknown. The display settings only
    /// carry whole hertz, so the NTSC-style rates that are one below a common rate (59, 119, ...) are read as
    /// 60000/1001 and so on, which is what such a mode really runs at.
    /// </summary>
    private static double GetDisplayRefreshRate(IntPtr windowHandle, out bool isOnPrimaryDisplay)
    {
        isOnPrimaryDisplay = true;
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            return 0;

        isOnPrimaryDisplay = (monitorInfo.Flags & MonitorInfoPrimary) != 0;

        var displayMode = new DisplayMode { Size = (short)Marshal.SizeOf<DisplayMode>() };
        if (!EnumDisplaySettings(monitorInfo.DeviceName, EnumCurrentSettings, ref displayMode) || displayMode.DisplayFrequency <= 1)
            return 0;

        var hertz = displayMode.DisplayFrequency;
        return hertz is 23 or 29 or 47 or 59 or 71 or 119 or 143 or 239
                   ? (hertz + 1) * 1000.0 / 1001.0
                   : hertz;
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

    /** Several refresh periods even at 30 Hz; older statistics no longer describe the current queue. */
    private const double MaxStatisticsAgeSec = 0.1;

    /** Beyond this the vblank statistics describe another display's clock, not a 59.94 vs 60 Hz rounding. */
    private const double MaxRefreshRateMismatch = 0.05;

    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;
    private const int EnumCurrentSettings = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public int MonitorLeft, MonitorTop, MonitorRight, MonitorBottom;
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    /** DEVMODEW, display variant. */
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields;
        public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TtOption, Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeIndex, ref DisplayMode displayMode);

    private static IntPtr _measuredSwapChain;
    private static long _baselineVblankQpc;
    private static uint _baselineVblankCount;
    private static long _lastMeasureTimestamp;
    private static bool _isUsingReportedRefreshRate;
    private static bool _hasTriedVblankBaseline;
}

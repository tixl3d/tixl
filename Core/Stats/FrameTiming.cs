using System;

namespace T3.Core.Stats;

/// <summary>
/// Cadence of the frame loop: the refresh period of the display that paces it and how long each frame stays on screen.
/// Fed once per frame by the Editor and the Player; read from the main thread only.
/// </summary>
public static class FrameTiming
{
    /// <summary>
    /// The refresh period of the display the pacing window is on, in seconds, as the host reports it with
    /// <see cref="SetRefreshRateFromDisplay"/>; 1/60 s until the first report.
    /// </summary>
    public static double RefreshPeriodSec { get; private set; } = DefaultRefreshPeriodSec;

    public static double RefreshRate => 1 / RefreshPeriodSec;

    /// <summary>
    /// How many refresh periods each frame stays on screen under vsync, e.g. 2 when a 120 Hz display renders at 60 fps.
    /// Set by the host that presents.
    /// </summary>
    public static int VblanksPerFrame { get; set; } = 1;

    /// <summary>
    /// How long the previous frame stayed on screen, in seconds. With vsync and a reported refresh period this is
    /// <see cref="LockedFrameDurationSec"/>, plus or minus whole refreshes when a frame missed or gained one; jitter in
    /// when frames start never reaches it. Without vsync it is the interval between frame starts.
    /// </summary>
    public static double VisualDeltaSec { get; private set; } = DefaultRefreshPeriodSec;

    /// <summary>
    /// How long a frame is meant to stay on screen, in seconds: <see cref="VblanksPerFrame"/> refreshes with vsync and
    /// a reported refresh period, otherwise <see cref="VisualDeltaSec"/>. Unlike that, it ignores dropped frames.
    /// </summary>
    public static double LockedFrameDurationSec { get; private set; } = DefaultRefreshPeriodSec;

    /// <summary>
    /// The current frame's time on the visual clock, in the time base of <c>Playback.RunTimeInSecs</c>. Advances by
    /// <see cref="VisualDeltaSec"/> each frame and stays within one refresh of the frame's actual start, so
    /// anything derived from the clock (beat time, extrapolated phases) moves as evenly as the display.
    /// </summary>
    public static double VisualFrameTimeSec { get; private set; }

    /// <summary>Wall-clock time between the starts of the previous and the current frame, in seconds.</summary>
    public static double FrameIntervalSec { get; private set; }

    /// <summary>Counts calls of <see cref="RecordFrameStart"/>, so consumers can tell whether this frame was recorded.</summary>
    public static long RecordedFrameCount { get; private set; }

    /// <summary>
    /// True while the display reports when frames reached the screen (<see cref="RecordShownFrame"/>). The visual clock
    /// then counts dropped refreshes from those reports instead of inferring them from when frames start.
    /// </summary>
    public static bool IsShownByDisplayReports => _lastShownPresentId != 0 && RecordedFrameCount - _lastShownReportFrame <= MaxFramesWithoutReport;

    /// <summary>Refreshes the display reported as missed since startup: frames that stayed on screen longer than planned.</summary>
    public static long DroppedRefreshCount { get; private set; }

    /// <summary>
    /// Records that the present numbered <paramref name="presentId"/> reached the screen at <paramref name="shownTimeNs"/>.
    /// Call for every report, oldest first, before <see cref="RecordFrameStart"/>. Refreshes that passed beyond the
    /// planned <see cref="VblanksPerFrame"/> per frame (or fell short of them) are added to the next frame's step.
    /// </summary>
    public static void RecordShownFrame(ulong presentId, long shownTimeNs)
    {
        // Ids start again when a swapchain is recreated, and a pause says nothing about dropped frames.
        var elapsedSec = (shownTimeNs - _lastShownTimeNs) * 1e-9;
        var isContinuation = _lastShownPresentId != 0
                             && presentId > _lastShownPresentId
                             && elapsedSec > 0
                             && elapsedSec <= MaxPlausibleIntervalSec;
        if (isContinuation)
        {
            var refreshes = Math.Round(elapsedSec / RefreshPeriodSec);
            var plannedRefreshes = (double)(presentId - _lastShownPresentId) * VblanksPerFrame;
            var missedRefreshes = refreshes - plannedRefreshes;
            _unappliedRefreshes += missedRefreshes;
            if (missedRefreshes > 0)
                DroppedRefreshCount += (long)missedRefreshes;
        }

        _lastShownPresentId = presentId;
        _lastShownTimeNs = shownTimeNs;
        _lastShownReportFrame = RecordedFrameCount;
    }

    /// <summary>Call once per frame at its start with the current <c>Playback.RunTimeInSecs</c>.</summary>
    public static void RecordFrameStart(double frameStartSec, bool isVsynced)
    {
        var intervalSec = RecordedFrameCount == 0 ? 0 : frameStartSec - _lastFrameStartSec;
        _lastFrameStartSec = frameStartSec;
        FrameIntervalSec = intervalSec;

        RecordedFrameCount++;
        VisualDeltaSec = AdvanceVisualClock(intervalSec, isVsynced, out var isPacedByRefresh);
        LockedFrameDurationSec = isPacedByRefresh ? RefreshPeriodSec * VblanksPerFrame : VisualDeltaSec;
        VisualFrameTimeSec = frameStartSec - _visualClockLagSec;
    }

    /// <summary>
    /// Sets the refresh period from the display the pacing window is on. Call once per frame, before
    /// <see cref="RecordFrameStart"/>; a rate of 0 (display unknown) keeps the previous period. Frame intervals are no
    /// substitute for this report: a loop that is GPU-bound at a steady 20 fps looks exactly like a 20 Hz display.
    /// </summary>
    public static void SetRefreshRateFromDisplay(double hertz)
    {
        if (hertz <= 0)
            return;

        RefreshPeriodSec = 1 / hertz;
        _hasReportedRefreshPeriod = true;
    }

    /// <summary>
    /// With vsync every frame stays on screen for whole refreshes, so the visual clock advances exactly one frame
    /// period (<see cref="VblanksPerFrame"/> refreshes) per frame, the same value every frame. Only once the wall
    /// clock is a whole refresh ahead or behind (a missed or early vblank) does a frame advance by whole refreshes
    /// more or less. Jitter in when frames start stays below that and never reaches the animation; the lag can't
    /// drift with it, because it is the elapsed time minus whole frame periods.
    /// </summary>
    private static double AdvanceVisualClock(double intervalSec, bool isVsynced, out bool isPacedByRefresh)
    {
        // Pauses (breakpoints, minimized windows, hitches while loading) restart the clock instead of catching up.
        isPacedByRefresh = isVsynced && _hasReportedRefreshPeriod;
        var canPaceByRefresh = isPacedByRefresh
                               && intervalSec > 0
                               && intervalSec <= MaxPlausibleIntervalSec;
        if (!canPaceByRefresh)
        {
            _visualClockLagSec = 0;
            return Math.Max(0, intervalSec);
        }

        var framePeriod = RefreshPeriodSec * VblanksPerFrame;
        if (IsShownByDisplayReports)
            return AdvanceByDisplayReports(intervalSec, framePeriod);

        _visualClockLagSec += intervalSec - framePeriod;

        var wholeRefreshes = Math.Truncate(_visualClockLagSec / RefreshPeriodSec);
        _visualClockLagSec -= wholeRefreshes * RefreshPeriodSec;
        return Math.Max(0, framePeriod + wholeRefreshes * RefreshPeriodSec);
    }

    /// <summary>
    /// One frame period plus whatever refreshes the display reported as missed (or gained) since the last frame. The
    /// lag only keeps <see cref="VisualFrameTimeSec"/> near the frame's start; reports arrive a frame or two late.
    /// </summary>
    private static double AdvanceByDisplayReports(double intervalSec, double framePeriod)
    {
        var visualDelta = framePeriod + _unappliedRefreshes * RefreshPeriodSec;
        _unappliedRefreshes = 0;
        if (visualDelta < 0)
        {
            _unappliedRefreshes = visualDelta / RefreshPeriodSec;
            visualDelta = 0;
        }

        _visualClockLagSec += intervalSec - visualDelta;
        if (Math.Abs(_visualClockLagSec) > MaxPlausibleIntervalSec)
            _visualClockLagSec = 0;

        return visualDelta;
    }

    private const double DefaultRefreshPeriodSec = 1.0 / 60.0;

    /** Without a report for this many frames the clock falls back to inferring drops from frame starts. */
    private const int MaxFramesWithoutReport = 30;

    private static ulong _lastShownPresentId;
    private static long _lastShownTimeNs;
    private static long _lastShownReportFrame;

    /** Refreshes reported as missed (positive) or gained (negative) and not yet added to a frame's step. */
    private static double _unappliedRefreshes;

    private const double MaxPlausibleIntervalSec = 0.25;

    private static bool _hasReportedRefreshPeriod;
    private static double _lastFrameStartSec;

    /** Wall-clock time minus visual time; stays within one refresh. */
    private static double _visualClockLagSec;
}

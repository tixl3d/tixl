using System;

namespace T3.Core.Stats;

/// <summary>
/// Cadence of the frame loop: the measured frame period and the refresh period of the display that paces it.
/// Fed once per frame by the Editor and the Player; read from the main thread only.
/// </summary>
public static class FrameTiming
{
    /// <summary>Average interval between frames over the last couple of seconds, in seconds.</summary>
    public static double FramePeriodSec { get; private set; } = DefaultRefreshPeriodSec;

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
    /// The current frame rate relative to the 60 fps that per-frame simulations were tuned at, e.g. 2 at 120 fps.
    /// </summary>
    public static double FrameRateRelativeTo60 => DefaultRefreshPeriodSec / FramePeriodSec;

    /// <summary>
    /// How far animations should advance this frame, in seconds. With vsync and a reported refresh period this is
    /// whole refresh periods — how long the previous frame stayed on screen — instead of the interval between frame
    /// starts, which jitters with CPU work. Otherwise it is that interval.
    /// </summary>
    public static double VisualDeltaSec { get; private set; } = DefaultRefreshPeriodSec;

    /// <summary>
    /// The current frame's time on the visual clock, in the time base of <c>Playback.RunTimeInSecs</c>. Advances by
    /// <see cref="VisualDeltaSec"/> each frame and stays within one refresh period of the frame's actual start, so
    /// anything derived from the clock (beat time, extrapolated phases) moves as evenly as the display.
    /// </summary>
    public static double VisualFrameTimeSec { get; private set; }

    /// <summary>Wall-clock time between the starts of the previous and the current frame, in seconds.</summary>
    public static double FrameIntervalSec { get; private set; }

    /// <summary>Counts calls of <see cref="RecordFrameStart"/>, so consumers can tell whether this frame was recorded.</summary>
    public static long RecordedFrameCount { get; private set; }

    /// <summary>Call once per frame at its start with the current <c>Playback.RunTimeInSecs</c>.</summary>
    public static void RecordFrameStart(double frameStartSec, bool isVsynced)
    {
        var intervalSec = RecordedFrameCount == 0 ? 0 : frameStartSec - _lastFrameStartSec;
        _lastFrameStartSec = frameStartSec;
        FrameIntervalSec = intervalSec;

        RecordedFrameCount++;
        VisualDeltaSec = AdvanceVisualClock(intervalSec, isVsynced);
        VisualFrameTimeSec = frameStartSec - _visualClockLagSec;

        // Pauses (breakpoints, minimized windows, hitches while loading) say nothing about the cadence.
        if (intervalSec <= 0 || intervalSec > MaxPlausibleIntervalSec)
            return;

        if (_count == WindowSize)
        {
            _intervalSum -= _intervals[_nextIndex];
        }
        else
        {
            _count++;
        }

        _intervals[_nextIndex] = intervalSec;
        _intervalSum += intervalSec;
        _nextIndex = (_nextIndex + 1) % WindowSize;
        FramePeriodSec = _intervalSum / _count;
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
    /// With vsync every frame stays on screen for whole refresh periods, so the visual clock advances one frame period
    /// (<see cref="VblanksPerFrame"/> refresh periods) per frame and only catches up in whole periods once it is a full
    /// period behind the wall clock (a missed vblank) or ahead of it. Jitter in when frames start stays below that and
    /// never reaches the animation. A slow pull towards the wall clock keeps the lag centred, so a slightly-off period
    /// (59.95 reported for a 59.94 Hz mode) doesn't drift it into a skipped or doubled frame every few seconds.
    /// </summary>
    private static double AdvanceVisualClock(double intervalSec, bool isVsynced)
    {
        var canPaceByRefresh = isVsynced
                               && _hasReportedRefreshPeriod
                               && intervalSec > 0
                               && intervalSec <= MaxPlausibleIntervalSec;
        if (!canPaceByRefresh)
        {
            _visualClockLagSec = 0;
            return Math.Max(0, intervalSec);
        }

        var period = RefreshPeriodSec * VblanksPerFrame;
        _visualClockLagSec += intervalSec - period;
        var correction = _visualClockLagSec * LagCorrectionRate;
        _visualClockLagSec -= correction;
        var visualDelta = period + correction;
        if (Math.Abs(_visualClockLagSec) >= period)
        {
            var wholePeriods = Math.Truncate(_visualClockLagSec / period);
            visualDelta += wholePeriods * period;
            _visualClockLagSec -= wholePeriods * period;
        }

        return Math.Max(0, visualDelta);
    }

    private const double DefaultRefreshPeriodSec = 1.0 / 60.0;

    /** About two seconds at 60 Hz. */
    private const int WindowSize = 120;

    /** Fraction of the visual clock's lag recovered per frame; ~1 s time constant, under 1 % change of speed. */
    private const double LagCorrectionRate = 0.02;
    private const double MaxPlausibleIntervalSec = 0.25;

    private static readonly double[] _intervals = new double[WindowSize];
    private static int _count;
    private static int _nextIndex;
    private static double _intervalSum;
    private static bool _hasReportedRefreshPeriod;
    private static double _lastFrameStartSec;

    /** Wall-clock time minus visual time; stays within one refresh period. */
    private static double _visualClockLagSec;
}

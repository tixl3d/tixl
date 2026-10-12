using System;

namespace T3.Core.Stats;

/// <summary>
/// Measured cadence of the frame loop: the current frame period and, while presenting with vsync, the display's
/// refresh period. Both are measured from frame intervals, so they work on every backend and display, including
/// fractional rates like 59.94 Hz. Fed once per frame by the Editor and the Player; read from the main thread only.
/// </summary>
public static class FrameTiming
{
    /// <summary>Average interval between frames over the last couple of seconds, in seconds.</summary>
    public static double FramePeriodSec { get; private set; } = DefaultRefreshPeriodSec;

    /// <summary>
    /// The display's refresh period in seconds, measured while vsync paces the loop. Keeps its last measured value
    /// when vsync is off or frames miss vblanks, and is 1/60 s until a first measurement exists.
    /// A loop that is steadily too slow for every vblank measures a multiple of the true period.
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
    /// How far animations should advance this frame, in seconds. With vsync and a measured refresh period this is
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
        _vsyncedFramesInWindow = isVsynced ? Math.Min(_vsyncedFramesInWindow + 1, WindowSize) : 0;

        FramePeriodSec = _intervalSum / _count;

        var hasRecentDisplayReport = _lastDisplayReportFrame >= 0
                                     && RecordedFrameCount - _lastDisplayReportFrame < MaxFramesWithoutDisplayReport;
        if (!hasRecentDisplayReport
            && _vsyncedFramesInWindow == WindowSize
            && ++_framesSinceRefreshEstimate >= RefreshEstimateIntervalFrames)
        {
            _framesSinceRefreshEstimate = 0;
            UpdateRefreshPeriodIfSteady();
        }
    }

    /// <summary>
    /// Sets the refresh period from the pacing display's own vblank timestamps. Takes precedence over the estimate from
    /// frame intervals, which fails when the loop is released on a different grid than the display scans out — e.g. a
    /// 48 Hz panel whose frames start on a 60 Hz compositor cadence (intervals of 16.7 and 33.3 ms).
    /// </summary>
    public static void SetRefreshPeriodFromDisplay(double periodSec)
    {
        RefreshPeriodSec = periodSec;
        _hasMeasuredRefreshPeriod = true;
        _lastDisplayReportFrame = RecordedFrameCount;
    }

    /// <summary>
    /// With vsync every interval spans a whole number of refresh periods plus the jitter of when the loop wakes up.
    /// That jitter can be large (Vulkan FIFO on Wayland releases images on the compositor's schedule, so 48 Hz shows
    /// up as 15.5 / 20.8 / 25.5 ms), but it telescopes: the window's total time divided by the vblanks it spans is
    /// precise. Each interval is rounded to whole periods of the window's mean, which also accounts for missed
    /// vblanks. Too many intervals that fit no whole period mean the loop isn't vsync-paced, and the previous
    /// estimate is kept.
    /// </summary>
    private static void UpdateRefreshPeriodIfSteady()
    {
        var period = _intervalSum / _count;

        // A second pass, because missed vblanks bias the mean upwards.
        for (var pass = 0; pass < 2; pass++)
        {
            var sum = 0.0;
            var vblankCount = 0.0;
            var misfitCount = 0;
            for (var index = 0; index < _count; index++)
            {
                var interval = _intervals[index];
                var periods = Math.Round(interval / period);
                if (periods < 1 || Math.Abs(interval - periods * period) > period * MaxPhaseJitter)
                {
                    misfitCount++;
                    continue;
                }

                sum += interval;
                vblankCount += periods;
            }

            if (misfitCount > _count / 10 || vblankCount == 0)
                return;

            period = sum / vblankCount;
        }

        RefreshPeriodSec = period / VblanksPerFrame;
        _hasMeasuredRefreshPeriod = true;
    }

    /// <summary>
    /// With vsync every frame stays on screen for whole refresh periods, so the visual clock advances one frame period
    /// (<see cref="VblanksPerFrame"/> refresh periods) per frame and only catches up in whole periods once it is a full period behind the wall clock (a missed vblank)
    /// or ahead of it. Jitter in when frames start stays below that and never reaches the animation. A slow pull
    /// towards the wall clock keeps the lag centred, so a slightly-off period estimate doesn't drift it into a
    /// skipped or doubled frame every few seconds.
    /// </summary>
    private static double AdvanceVisualClock(double intervalSec, bool isVsynced)
    {
        var canPaceByRefresh = isVsynced
                               && _hasMeasuredRefreshPeriod
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

    private const int RefreshEstimateIntervalFrames = 30;

    /** How far, as a fraction of the period, an interval may be off a whole number of periods. Rounding flips at 0.5. */
    private const double MaxPhaseJitter = 0.4;

    /** Fraction of the visual clock's lag recovered per frame; ~1 s time constant, under 1 % change of speed. */
    private const double LagCorrectionRate = 0.02;
    private const double MaxPlausibleIntervalSec = 0.25;

    /** About three seconds at 60 Hz; after that the interval estimate takes over again. */
    private const int MaxFramesWithoutDisplayReport = 180;

    private static readonly double[] _intervals = new double[WindowSize];
    private static int _count;
    private static int _nextIndex;
    private static double _intervalSum;
    private static int _vsyncedFramesInWindow;
    private static int _framesSinceRefreshEstimate;
    private static bool _hasMeasuredRefreshPeriod;
    private static double _lastFrameStartSec;
    private static long _lastDisplayReportFrame = -1;

    /** Wall-clock time minus visual time; stays within one refresh period. */
    private static double _visualClockLagSec;
}

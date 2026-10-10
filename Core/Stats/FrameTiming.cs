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

        if (_vsyncedFramesInWindow == WindowSize && ++_framesSinceRefreshEstimate >= RefreshEstimateIntervalFrames)
        {
            _framesSinceRefreshEstimate = 0;
            UpdateRefreshPeriodIfSteady();
        }
    }

    /// <summary>
    /// With vsync and no missed vblanks every interval is one refresh period, so the mean of the window's middle
    /// 80 % is a precise measurement; single hitches at either end are excluded. A wide spread within that middle
    /// means missed or doubled frames, and the previous estimate is kept.
    /// </summary>
    private static void UpdateRefreshPeriodIfSteady()
    {
        Array.Copy(_intervals, _sortedScratch, WindowSize);
        Array.Sort(_sortedScratch);
        const int trimmedCount = WindowSize / 10;
        var low = _sortedScratch[trimmedCount];
        var high = _sortedScratch[WindowSize - 1 - trimmedCount];
        if (high > low * MaxSteadySpread)
            return;

        var sum = 0.0;
        for (var index = trimmedCount; index < WindowSize - trimmedCount; index++)
        {
            sum += _sortedScratch[index];
        }

        RefreshPeriodSec = sum / (WindowSize - 2 * trimmedCount);
        _hasMeasuredRefreshPeriod = true;
    }

    /// <summary>
    /// With vsync every frame stays on screen for whole refresh periods, so the visual clock advances one period per
    /// frame and only catches up in whole periods once it is a full period behind the wall clock (a missed vblank)
    /// or ahead of it. Jitter in when frames start stays below that and never reaches the animation.
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

        var period = RefreshPeriodSec;
        var visualDelta = period;
        _visualClockLagSec += intervalSec - period;
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
    private const double MaxSteadySpread = 1.2;
    private const double MaxPlausibleIntervalSec = 0.25;

    private static readonly double[] _intervals = new double[WindowSize];
    private static readonly double[] _sortedScratch = new double[WindowSize];
    private static int _count;
    private static int _nextIndex;
    private static double _intervalSum;
    private static int _vsyncedFramesInWindow;
    private static int _framesSinceRefreshEstimate;
    private static bool _hasMeasuredRefreshPeriod;
    private static double _lastFrameStartSec;

    /** Wall-clock time minus visual time; stays within one refresh period. */
    private static double _visualClockLagSec;
}

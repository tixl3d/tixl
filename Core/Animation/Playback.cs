using System;
using System.Diagnostics;
using T3.Core.Settings;
using T3.Core.Stats;

namespace T3.Core.Animation;

/// <summary>
/// Some notes terminology:  "Time" vs. "TimeInSecs" - Default measure for time (unless it has the "*InSecs" suffix) is a "bar".
/// So at 120 BPM a unit of time is 2 seconds.    
/// 
/// "Local" vs. "Playback": Local time can be overridden for by operators (like [SetCommandTime]) and [TimeClip]. These should be
/// used for most operators.  The global Playback time is provided the Playback used in the output. The global time should be used
/// for updating operators to have consistent and predictable "timing" even in sub-graphs with overridden time.
/// Examples for this are [Pulsate] or [Counter]. 
/// 
/// "Time" vs "FxTime"
///  - FxTime keeps running if "Idle Motion" (aka. "continued playback") is activated. The effect time should be used for most Operators.
///  - Time is used for all UI interactions and everything that is driven by keyframes.
/// 
/// RunTime is the time since application.
/// IsLive is true if we are playing live, false if we are rendering
/// </summary>
public class Playback
{
    public Playback()
    {
        _isRenderingToFile = false;
        Current = this;
    }

    public static Playback Current { get; set; }
    public CompositionSettings Settings { get; set; }

    /// <summary>
    /// The absolute current time as controlled by the timeline interaction in bars.
    /// </summary>
    public virtual double TimeInBars { get; set; }

    /// <summary>
    /// The current time used for animation (would advance from <see cref="TimeInBars"/> if Idle Motion is enabled. 
    /// </summary>
    public double FxTimeInBars { get; protected set; }

    /// <summary>
    /// Convenience function to convert from internal TimeInBars mapped to seconds for current BPM. 
    /// </summary>
    public double TimeInSecs { get => TimeInBars * 240.0 / Bpm;
        set => TimeInBars = value * Bpm / 240.0; }

    public TimeRange LoopRange;

    public double Bpm { get;  set; } = 120.0;
        
    /// <summary>
    /// Controls if the playback is controlled by rendering output.
    /// </summary>
    /// <remarks>
    /// During rendering of videos or image sequence this setting is set to true
    /// to prevent time updates that interfere with rendered controlled audio output. 
    /// </remarks>
    public bool IsRenderingToFile
    {
        get => _isRenderingToFile;
        set {
            _isRenderingToFile = value;

            // Update() measures from the clock it is about to switch to, so the next frame duration starts at 0
            // instead of the gap between file time and the app's runtime.
            if (value)
            {
                PlaybackSpeed = 0;
                _lastFrameStart = TimeInSecs;
            }
            else
            {
                _lastFrameStart = RunTimeInSecs;
            }
        }
    }
        
    public double PlaybackSpeed { get; set; }
        
    /// <summary>
    /// The frame rate relative to 60 fps, e.g. 2 on a 120 Hz display or 25/60 when exporting at 25 fps.
    /// Live it is measured (<see cref="FrameTiming.FrameRateRelativeTo60"/>); while rendering to file it is the value
    /// set here by the exporter.
    ///
    /// If possible simulation operators like [ParticleSystem] or [FeedbackEffect] should apply this factor to their overall speed factor.
    /// </summary>
    public double FrameSpeedFactor
    {
        get => IsRenderingToFile ? _frameSpeedFactorForFileRendering : FrameTiming.FrameRateRelativeTo60;
        set => _frameSpeedFactorForFileRendering = value;
    }
    public bool IsLooping = false;
    public static bool OpNotReady;
        
    public static double RunTimeInSecs => RunTimeWatch.Elapsed.TotalSeconds;
    public static int FrameCount;
    /// <summary>
    /// Duration of the current frame in seconds: the visual frame duration live, the file time step while rendering
    /// to file, or <see cref="FixedFrameDurationSec"/> while that is set.
    /// </summary>
    public static double LastFrameDuration
    {
        get => FixedFrameDurationSec ?? _lastFrameDuration;
        protected set => _lastFrameDuration = value;
    }

    /// <summary>
    /// While set, <see cref="LastFrameDuration"/> returns this instead of a measured duration, so frame-duration
    /// dependent ops (damping, springs, simulations) step identically on every machine and display. Set by export
    /// (one frame of the export rate) and by visual reference tests (1/60 s per evaluation step).
    /// </summary>
    public static double? FixedFrameDurationSec { get; set; }
    public double LastFrameDurationInBars => BarsFromSeconds(LastFrameDuration);
        
    public virtual void Update(bool idleMotionEnabled = false)
    {
        FrameCount++;
        // If we are not live, TimeInBars is provided externally
        Current = this;
        var currentRuntime = IsRenderingToFile ?   TimeInSecs : RunTimeInSecs;

        var wallClockDuration = currentRuntime - _lastFrameStart;
        _lastFrameStart = currentRuntime;
        LastFrameDuration = IsRenderingToFile ? wallClockDuration : GetLiveFrameDuration(wallClockDuration);

        var timeSinceLastFrameInSecs = LastFrameDuration;
        var isPlaying = Math.Abs(PlaybackSpeed) > 0.001;

        if (IsRenderingToFile)
        {
            FxTimeInBars = TimeInBars;
        }
        else if (isPlaying)
        {
            TimeInBars += timeSinceLastFrameInSecs * PlaybackSpeed * Bpm / 240.0;
            FxTimeInBars = TimeInBars;
        }
        else
        {
            var timeWasManipulated = Math.Abs(TimeInBars - _previousTimeInBars) > 0.00001f;
            if (timeWasManipulated)
            {
                FxTimeInBars = TimeInBars;
            }
            else if (idleMotionEnabled)
            {
                FxTimeInBars += timeSinceLastFrameInSecs * Bpm / 240f;
            }
        }

        // don't support looping if recording (looping sound is not implemented yet)
        if (!IsRenderingToFile && Math.Abs(PlaybackSpeed) > 0.1 && IsLooping && TimeInBars > LoopRange.End)
        {
            double loopDuration = LoopRange.End - LoopRange.Start;

            // Jump to start if loop is negative or sound is too far out of time region
            if (loopDuration <= 0 || TimeInBars - LoopRange.End > 1.0)
                TimeInBars = LoopRange.Start;
            else
                TimeInBars -= loopDuration;
        }

        _previousTimeInBars = TimeInBars;
    }

    public double BarsFromSeconds(double secs)
    {
        return secs * Bpm / 240.0;
    }
        
    public double SecondsFromBars(double bars)
    {
        return bars * 240.0 / Bpm;
    }

    /// <summary>
    /// The time animations advance by in live playback: the visual frame duration when the host recorded this frame
    /// with <see cref="FrameTiming.RecordFrameStart"/> (whole refresh periods under vsync), otherwise the wall-clock duration.
    /// </summary>
    protected static double GetLiveFrameDuration(double wallClockDuration)
    {
        if (FrameTiming.RecordedFrameCount == _lastUsedTimingFrame)
            return wallClockDuration;

        _lastUsedTimingFrame = FrameTiming.RecordedFrameCount;
        return FrameTiming.VisualDeltaSec;
    }

    private static double _lastFrameStart;
    private static double _lastFrameDuration;
    private static long _lastUsedTimingFrame = -1;
    private double _frameSpeedFactorForFileRendering = 1;
    private double _previousTimeInBars;
    private static readonly Stopwatch RunTimeWatch = Stopwatch.StartNew();
    private bool _isRenderingToFile;
}
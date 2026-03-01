using System;
using System.Diagnostics;
using T3.Core.Operator;

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
    public PlaybackSettings Settings { get; set; }

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
            if (value)
            {
                PlaybackSpeed = 0;
                _lastFrameStart = RunTimeInSecs;
            }
            else
            {
                _lastFrameStart = TimeInSecs;
            }
        }
    }
        
    public double PlaybackSpeed { get; set; }
        
    /// <summary>
    /// This is set when rendering updates not at 60fps. This can happen for...
    /// 
    /// - high framerate displays -> e.g. 2 for 120hz displays
    /// - when rendering low fps image sequences -> e.g. 25/60 for 25fps
    ///    
    /// If possible simulation operators like [ParticleSystem] or [FeedbackEffect] should apply this factor to their overall speed factor.
    /// </summary>
    public double FrameSpeedFactor { get; set; } = 1;
    public bool IsLooping = false;
    public static bool OpNotReady;
        
    public static double RunTimeInSecs => RunTimeWatch.Elapsed.TotalSeconds;
    public static int FrameCount;
    public static double LastFrameDuration { get; protected set; }
    public double LastFrameDurationInBars => BarsFromSeconds(LastFrameDuration);
        
    public virtual void Update(bool idleMotionEnabled = false)
    {
        FrameCount++;
        // If we are not live, TimeInBars is provided externally
        Current = this;
        var currentRuntime = IsRenderingToFile ?   TimeInSecs : RunTimeInSecs;

        LastFrameDuration = currentRuntime - _lastFrameStart;
        _lastFrameStart = currentRuntime;

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
        
    private static double _lastFrameStart;
    private double _previousTimeInBars;
    private static readonly Stopwatch RunTimeWatch = Stopwatch.StartNew();
    private bool _isRenderingToFile;
}
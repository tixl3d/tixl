using System;

namespace T3.Core.Animation;

/// <summary>
/// Snapshot of how a timeline <see cref="TimeRange"/> maps to a source-content
/// <see cref="TimeRange"/>, plus the BPM needed to convert bars to seconds. Captures
/// the canonical playhead → source-time math every TimeClip-based op performs.
/// </summary>
/// <remarks>
/// <para>
/// Lives next to <see cref="TimeRange"/> and <see cref="TimeClip"/> because it's
/// genuinely a property of timeline clips: audio, video, image-sequence, and the new
/// <c>DataClip</c> all do exactly this math inline today. New code should publish a
/// <see cref="TimeRangeMapping"/> instead so consumers can call the conversion at any
/// timeline point — not just "now".
/// </para>
/// <para>
/// Designed as a <c>readonly struct</c> with value-typed fields so passing it on a
/// graph slot or capturing it for one-frame use is allocation-free and gives a stable
/// snapshot — the consumer can't accidentally read a mutated TimeClip on a later frame.
/// </para>
/// </remarks>
public readonly struct TimeRangeMapping
{
    /// <summary>The clip's placement on the timeline (bars).</summary>
    public readonly TimeRange TimeRange;

    /// <summary>The slice of the source content that the clip exposes (bars).</summary>
    public readonly TimeRange SourceRange;

    /// <summary>BPM at the moment the mapping was built — baked in so consumers don't need <see cref="Playback"/>.</summary>
    public readonly double Bpm;

    public TimeRangeMapping(TimeRange timeRange, TimeRange sourceRange, double bpm)
    {
        TimeRange = timeRange;
        SourceRange = sourceRange;
        Bpm = bpm;
    }

    /// <summary>
    /// Maps a timeline-local time (bars) to a source-relative time (bars). The result
    /// is unbounded — caller decides what to do when the input falls outside
    /// <see cref="TimeRange"/> (typically: ignore, or clamp).
    /// </summary>
    public double LocalBarsToSourceBars(double localBars)
    {
        var pos = localBars - TimeRange.Start;
        if (Math.Abs(TimeRange.End - TimeRange.Start) > 0.0001f)
        {
            var rate = (SourceRange.End - SourceRange.Start) / (TimeRange.End - TimeRange.Start);
            pos *= rate;
        }
        return pos + SourceRange.Start;
    }

    /// <summary>
    /// Maps a timeline-local time (bars) to a source-relative time (seconds), using the
    /// <see cref="Bpm"/> captured at mapping construction.
    /// </summary>
    public double LocalBarsToSourceSecs(double localBars)
        => LocalBarsToSourceBars(localBars) * 240.0 / Bpm;

    /// <summary>
    /// Inverse of <see cref="LocalBarsToSourceBars"/>: maps a source-relative time (bars)
    /// to its timeline-local position (bars). Useful for rendering source-time events
    /// (e.g. recorded MIDI / data points) at the right X on the timeline.
    /// </summary>
    public double SourceBarsToLocalBars(double sourceBars)
    {
        var sourceDur = SourceRange.End - SourceRange.Start;
        var timeDur = TimeRange.End - TimeRange.Start;
        if (Math.Abs(sourceDur) < 0.0001f)
            return TimeRange.Start;

        var rate = timeDur / sourceDur;
        return TimeRange.Start + (sourceBars - SourceRange.Start) * rate;
    }

    /// <summary>
    /// Convenience: <see cref="SourceBarsToLocalBars"/> with the input in seconds.
    /// </summary>
    public double SourceSecsToLocalBars(double sourceSecs)
        => SourceBarsToLocalBars(sourceSecs * Bpm / 240.0);

    /// <summary>True if <paramref name="localBars"/> falls inside <see cref="TimeRange"/>.</summary>
    public bool IsActive(double localBars)
        => localBars >= TimeRange.Start && localBars <= TimeRange.End;
}

/// <summary>
/// Convenience constructors for building a <see cref="TimeRangeMapping"/> from a
/// <see cref="TimeClip"/> and the current <see cref="Playback"/>. Lives outside
/// <see cref="TimeRangeMapping"/> itself to avoid pulling <see cref="TimeClip"/> /
/// <see cref="Playback"/> into the struct's already-tight API.
/// </summary>
public static class TimeRangeMappingExtensions
{
    public static TimeRangeMapping ToMapping(this TimeClip clip, Playback playback)
        => new(clip.TimeRange, clip.SourceRange, playback.Bpm);
}

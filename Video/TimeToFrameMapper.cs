using T3.Core.Utils;

namespace T3.Video;

/// <summary>
/// Pure, deterministic mapping from a requested playback time (seconds) to a stream presentation
/// timestamp (PTS). Holds no D3D or FFmpeg state so it can be unit-tested in isolation. This is the core
/// that guarantees the same <c>double</c> time always resolves to the same frame, whether paused or playing.
/// </summary>
public static class TimeToFrameMapper
{
    /// <summary>
    /// Resolve a requested time against the clip duration. Looping wraps modulo duration (negatives wrap
    /// forward); otherwise the time is clamped to <c>[0, duration]</c> so out-of-range requests resolve to
    /// the first/last frame.
    /// </summary>
    public static double ResolvePlaybackSeconds(double requestedSeconds, double durationSeconds, bool loop)
    {
        if (durationSeconds <= 0)
            return 0;

        return loop
                   ? MathUtils.Fmod(requestedSeconds, durationSeconds)
                   : Math.Clamp(requestedSeconds, 0, durationSeconds);
    }

    /// <summary>
    /// Floor-to-PTS: the presentation timestamp of the frame whose interval <c>[pts, pts+1)</c> contains
    /// <paramref name="seconds"/>. Flooring (never rounding) is what makes a paused seek and a playing
    /// pass-through land on the identical frame. <paramref name="streamStartPts"/> is the stream's
    /// <c>start_time</c> (often 0); the time base is <paramref name="timeBaseNum"/>/<paramref name="timeBaseDen"/>.
    /// </summary>
    public static long SecondsToPts(double seconds, long streamStartPts, int timeBaseNum, int timeBaseDen)
    {
        if (timeBaseNum <= 0 || timeBaseDen <= 0)
            return streamStartPts;

        // seconds = (pts - start) * num/den  =>  pts = start + floor(seconds * den / num)
        var ticks = (long)Math.Floor(seconds * timeBaseDen / timeBaseNum);
        return streamStartPts + ticks;
    }

    /// <summary>Inverse of <see cref="SecondsToPts"/>, for diagnostics and clamping.</summary>
    public static double PtsToSeconds(long pts, long streamStartPts, int timeBaseNum, int timeBaseDen)
    {
        if (timeBaseDen <= 0)
            return 0;

        return (pts - streamStartPts) * (double)timeBaseNum / timeBaseDen;
    }
}

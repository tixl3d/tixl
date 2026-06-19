using T3.Core.Utils;

namespace T3.VideoServices;

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

    /// <summary>
    /// Like <see cref="SecondsToPts"/> but snapped to the frame grid: the PTS of the frame whose display
    /// interval contains <paramref name="seconds"/>, assuming constant <paramref name="fps"/>. Keeping the
    /// target on actual frame boundaries means a decoder that stops at the first <c>pts &gt;= target</c> lands
    /// exactly on the displayed frame instead of overshooting to the next one — and repeated requests within
    /// one frame resolve to the same target. Without this, a display refresh that outruns the video frame rate
    /// (e.g. 24 fps clip on a 60 Hz editor) overshoots, then seeks backward every frame and re-decodes the GOP.
    /// Falls back to per-tick flooring when fps is unknown.
    /// </summary>
    public static long SecondsToFramePts(double seconds, long streamStartPts, int timeBaseNum, int timeBaseDen, double fps)
    {
        if (fps <= 0 || timeBaseNum <= 0 || timeBaseDen <= 0)
            return SecondsToPts(seconds, streamStartPts, timeBaseNum, timeBaseDen);

        // The frame index FLOORS the time to the displayed frame; its PTS must then land exactly on that frame's
        // boundary, so ROUND the conversion (not floor). Flooring here lets float error at the boundary push the
        // target a tick into the previous frame, so the decoder lands one frame early and disagrees with a cache
        // lookup that floored the same time to this index — a reproducible 1-frame split at certain times.
        var frameIndex = Math.Floor(seconds * fps);
        var ticks = (long)Math.Round(frameIndex / fps * timeBaseDen / timeBaseNum);
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

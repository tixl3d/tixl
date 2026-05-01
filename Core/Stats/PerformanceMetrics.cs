using System;
using System.Diagnostics;

namespace T3.Core.Stats;

/// <summary>
/// Host-agnostic rolling performance metrics (frame duration, UI render duration, managed GC allocations).
/// Used by both the Editor and the Player.
///
/// Frame / UI render values are supplied by the caller; GC allocations are sampled internally
/// via <see cref="GC.GetTotalAllocatedBytes()"/> whenever <see cref="RecordFrame"/> is called.
///
/// Thread-safety: intended to be called from the main render thread only.
/// </summary>
public static class PerformanceMetrics
{
    public const int WindowSize = 400;
    public const int BucketCount = 16;

    /// <summary>Total frames recorded since process start. Grows monotonically.</summary>
    public static long TotalFrameCount { get; private set; }

    /// <summary>Frame duration in milliseconds. Bucket anchored at 16.66 ms (60 Hz).</summary>
    public static readonly RollingMetric FrameDuration =
        RollingMetric.CreateLinear(WindowSize, BucketCount, 0, 32f);

    /// <summary>UI render duration in milliseconds. Linear 0..30 ms.</summary>
    public static readonly RollingMetric UiRenderDuration =
        RollingMetric.CreateLinear(WindowSize, BucketCount, 0f, 32f);

    /// <summary>Managed allocations per frame, in kilobytes. Log10-bucketed 0.1 kB .. 10 MB.</summary>
    public static readonly RollingMetric GcAllocationsKb =
        RollingMetric.CreateLog10(WindowSize, 10, 0f, 4f);

    private static long _lastGcTotalBytes;

    /// <summary>Wall-clock time in seconds since process start. Monotonic, allocation-free.</summary>
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>
    /// Record a completed frame. Updates <see cref="FrameDuration"/> and samples GC allocations.
    /// Call once per frame, after the frame finishes rendering.
    /// </summary>
    public static void RecordFrame(float frameDurationMs)
    {
        var now = Now;
        TotalFrameCount++;
        FrameDuration.Update(frameDurationMs, now);
        SampleGc(now);
    }

    /// <summary>
    /// Record the duration of the UI pass within a frame (editor only). Call once per frame.
    /// </summary>
    public static void RecordUiRender(float uiRenderMs)
    {
        UiRenderDuration.Update(uiRenderMs, Now);
    }

    private static void SampleGc(double now)
    {
        var total = GC.GetTotalAllocatedBytes(precise:true);
        var delta = total - _lastGcTotalBytes;
        _lastGcTotalBytes = total;
        if (delta < 0)
            delta = 0; // tolerate counter wrap / host reset
        GcAllocationsKb.Update((float)(delta / 1024.0), now);
    }
}

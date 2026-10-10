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

    /// <summary>
    /// Main-thread time per frame in milliseconds (evaluation, UI, issuing GPU commands), excluding GPU execution.
    /// </summary>
    public static readonly RollingMetric CpuFrameDuration =
        RollingMetric.CreateLinear(WindowSize, BucketCount, 0f, 32f);

    /// <summary>
    /// Time spent handing finished frames to the displays, in milliseconds. Its own metric because a vsynced
    /// Present blocks: with several swap chains (the main window, the viewer, one per bound output) the frame
    /// can be spent waiting for vblanks while nothing is being computed, which is invisible in the frame bar.
    /// </summary>
    public static readonly RollingMetric PresentDuration =
        RollingMetric.CreateLinear(WindowSize, BucketCount, 0f, 32f);

    /// <summary>
    /// GPU time per frame in milliseconds, from timestamps around the frame's commands. Samples arrive a few frames
    /// late (the GPU reports when it is done) and include GPU idle gaps while the CPU is still submitting.
    /// </summary>
    public static readonly RollingMetric GpuFrameDuration =
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

    /// <summary>Record the main thread's time for a frame (editor only). Call once per frame.</summary>
    public static void RecordCpuFrame(float cpuMs)
    {
        CpuFrameDuration.Update(cpuMs, Now);
    }

    /// <summary>Record the time the frame spent in Present. Call once per frame.</summary>
    public static void RecordPresent(float presentMs)
    {
        PresentDuration.Update(presentMs, Now);
    }

    /// <summary>Record a frame's GPU time once the GPU has reported it.</summary>
    public static void RecordGpuFrame(float gpuMs)
    {
        GpuFrameDuration.Update(gpuMs, Now);
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

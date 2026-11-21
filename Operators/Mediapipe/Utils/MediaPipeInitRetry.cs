namespace Lib.io.video.mediapipe;

/// <summary>
/// Pacing for re-initializing a MediaPipe worker after a failure.
/// <para>
/// A failed initialization (missing model, missing native library, a delegate the native rejects) used to
/// be retried on every frame: a caught exception plus a log line 60 times a second, and a native pipeline
/// rebuilt each time. The first retry waits a second, then 2, 4, 8 and 16 seconds, capped at 30 - long
/// enough that a broken model costs nothing, short enough that fixing it (dropping the model in, enabling
/// GPU) is picked up while the project still runs. A changed parameter resets the counter, so deliberate
/// edits never wait.
/// </para>
/// </summary>
internal static class MediaPipeInitRetry
{
    /// <summary>Milliseconds to wait before attempt number <paramref name="attempts"/> (0-based).</summary>
    public static long BackoffMs(int attempts)
        => Math.Min(30_000L, 1_000L << Math.Min(5, Math.Max(0, attempts)));
}

namespace Lib.io.video.mediapipe;

/// <summary>
/// Defers closing a MediaPipe task instance (<c>FaceDetector</c>, <c>HandLandmarker</c>, ...) until the
/// worker that drives it has exited.
/// <para>
/// The task APIs block inside native inference and offer no cancellation, so a worker can be mid
/// <c>DetectForVideo</c> when its operator is reconfigured or disposed. Closing the native instance from
/// the evaluation thread at that moment is a use-after-free inside native code — a hard process crash
/// that no managed exception handler can catch. Waiting for the worker on the evaluation thread instead
/// would stall the render loop for the duration of an inference.
/// </para>
/// <para>
/// The reaper solves both: the wait happens on the thread pool, chains behind any teardown still in
/// flight (so two native instances never close concurrently on one operator), and
/// <see cref="WaitForPending"/> lets <c>Dispose</c> put a bound on how long shutdown waits for it.
/// </para>
/// <para>
/// Chaining is deliberate and is the one place where a truly wedged native call costs something: if an
/// inference never returns, the instances queued behind it are not closed either (they stay allocated
/// until the process exits, which a wedged graph implies anyway). Relaxing the order - closing while an
/// earlier close is still running - would trade that bounded leak for the concurrent-close crash this
/// class exists to prevent, so the ordering stays.
/// </para>
/// </summary>
internal sealed class MediaPipeWorkerReaper
{
    private readonly object _lock = new();
    private Task _pending = Task.CompletedTask;

    /// <summary>
    /// Releases a MediaPipe task instance (<c>FaceDetector</c>, <c>ImageSegmenter</c>, ...).
    /// <para>
    /// The task APIs implement <see cref="IDisposable"/> explicitly, so <c>Dispose()</c> is not callable
    /// by name on the concrete type; that is why the original code called <c>Close()</c>. <c>Close()</c>
    /// only closes the calculator graph, while <c>Dispose()</c> additionally releases the native
    /// <c>TaskRunner</c> - using <c>Close()</c> alone leaks one runner per worker restart (every
    /// parameter change, every enable/disable). This helper casts to the interface so the full cleanup
    /// runs, and tolerates a type that is not disposable at all.
    /// </para>
    /// </summary>
    public static void CloseNative(object? nativeTask)
    {
        if (nativeTask is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Schedules <paramref name="nativeTask"/> to be disposed once <paramref name="worker"/> has exited,
    /// after every teardown queued before it. Any of the three may be <c>null</c>. Never blocks and never
    /// throws: teardown runs during shutdown and must not turn into an evaluation error.
    /// </summary>
    public void Queue(Task? worker, object? nativeTask, CancellationTokenSource? cancellation)
    {
        if (worker == null && nativeTask == null && cancellation == null)
            return;

        lock (_lock)
        {
            _pending = CloseAfterAsync(_pending, worker, nativeTask, cancellation);
        }

        // A static local, so the continuation does not capture the operator instance: after Dispose the
        // reaper may still outlive the operator for one inference, and keeping `this` alive there would
        // also keep every output texture of the disposed operator alive.
        static async Task CloseAfterAsync(Task previous, Task? worker, object? nativeTask, CancellationTokenSource? cancellation)
        {
            try { await previous.ConfigureAwait(false); } catch { /* the previous teardown is already guarded */ }
            if (worker != null)
            {
                try { await worker.ConfigureAwait(false); } catch { /* a cancelled or faulted worker is expected */ }
            }

            try { CloseNative(nativeTask); } catch (Exception e) { Log.Warning($"MediaPipe: closing a native task failed: {e.Message}"); }
            try { cancellation?.Dispose(); } catch { /* disposal of a signalled source cannot fail meaningfully */ }
        }
    }

    /// <summary>
    /// Waits up to <paramref name="milliseconds"/> for the queued teardowns to finish and returns whether
    /// they did. Called from <c>Dispose</c>: shutdown should give a running inference a moment to return,
    /// but it must never hang on a native library that stopped responding.
    /// </summary>
    public bool WaitForPending(int milliseconds)
    {
        Task pending;
        lock (_lock)
        {
            pending = _pending;
        }

        try
        {
            return pending.Wait(milliseconds);
        }
        catch (Exception)
        {
            return true;
        }
    }
}

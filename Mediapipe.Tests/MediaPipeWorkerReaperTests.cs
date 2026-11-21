using System.Diagnostics;
using Lib.io.video.mediapipe;
using Xunit;

namespace Mediapipe.Tests;

/// <summary>
/// The operator teardown path, without a GPU or a native library: these are the invariants that keep a
/// reconfigured or disposed operator from faulting the process, and each one is a crash or a leak when
/// it regresses.
/// </summary>
public class MediaPipeWorkerReaperTests
{
    /// <summary>A native task that records when it was disposed relative to the worker it drives.</summary>
    private sealed class FakeNativeTask : IDisposable
    {
        public bool Disposed { get; private set; }
        public bool WorkerHadExited { get; private set; }
        private readonly Task _worker;

        public FakeNativeTask(Task worker) => _worker = worker;

        public void Dispose()
        {
            WorkerHadExited = _worker.IsCompleted;
            Disposed = true;
        }
    }

    [Fact]
    public void Queue_DisposesOnlyAfterTheWorkerExits()
    {
        var gate = new TaskCompletionSource();
        var worker = Task.Run(async () => await gate.Task);
        var native = new FakeNativeTask(worker);
        var reaper = new MediaPipeWorkerReaper();

        reaper.Queue(worker, native, null);

        // Still running: the native instance must not be closed yet - that is the use-after-free.
        Assert.False(native.Disposed);

        gate.SetResult();
        Assert.True(reaper.WaitForPending(5000));
        Assert.True(native.Disposed);
        Assert.True(native.WorkerHadExited, "the native task was disposed while its worker still ran");
    }

    [Fact]
    public void Queue_FaultedWorker_StillDisposes()
    {
        var worker = Task.Run(() => throw new InvalidOperationException("inference failed"));
        var native = new FakeNativeTask(worker);
        var reaper = new MediaPipeWorkerReaper();

        reaper.Queue(worker, native, null);

        Assert.True(reaper.WaitForPending(5000));
        Assert.True(native.Disposed, "a faulted worker must still release its native task");
    }

    [Fact]
    public void Queue_CancelledWorker_StillDisposes()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var worker = Task.FromCanceled(cts.Token);
        var native = new FakeNativeTask(worker);
        var reaper = new MediaPipeWorkerReaper();

        reaper.Queue(worker, native, null);

        Assert.True(reaper.WaitForPending(5000));
        Assert.True(native.Disposed);
    }

    /// <summary>
    /// Restarting a worker several times must close the previous native instance before the next one is
    /// torn down: the task APIs are not safe to close concurrently.
    /// </summary>
    [Fact]
    public void Queue_SerialisesConsecutiveTeardowns()
    {
        var reaper = new MediaPipeWorkerReaper();
        var closes = new List<int>();
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();

        var workerA = Task.Run(async () => await first.Task);
        var workerB = Task.Run(async () => await second.Task);

        reaper.Queue(workerA, new RecordingDisposable(1, closes), null);
        reaper.Queue(workerB, new RecordingDisposable(2, closes), null);

        // B finishes first, but its native instance must still be closed after A's - the queue chains.
        second.SetResult();
        first.SetResult();

        Assert.True(reaper.WaitForPending(5000));
        Assert.Equal([1, 2], closes);
    }

    private sealed class RecordingDisposable(int id, List<int> log) : IDisposable
    {
        public void Dispose()
        {
            lock (log)
            {
                log.Add(id);
            }
        }
    }

    [Fact]
    public void Queue_DisposesTheCancellationSource()
    {
        var reaper = new MediaPipeWorkerReaper();
        var cts = new CancellationTokenSource();

        reaper.Queue(Task.CompletedTask, null, cts);

        Assert.True(reaper.WaitForPending(5000));
        // A disposed CancellationTokenSource throws on Cancel - the cheapest observable proof.
        Assert.Throws<ObjectDisposedException>(() => cts.Cancel());
    }

    [Fact]
    public void Queue_NothingToDo_DoesNotBlockOrThrow()
    {
        var reaper = new MediaPipeWorkerReaper();
        reaper.Queue(null, null, null);
        Assert.True(reaper.WaitForPending(0));
    }

    /// <summary>
    /// Dispose on a wedged native library must not hang editor shutdown: WaitForPending is the bound.
    /// </summary>
    [Fact]
    public void WaitForPending_TimesOutOnAWorkerThatNeverReturns()
    {
        var reaper = new MediaPipeWorkerReaper();
        var never = new TaskCompletionSource();
        reaper.Queue(never.Task, null, null);

        var stopwatch = Stopwatch.StartNew();
        Assert.False(reaper.WaitForPending(100));
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "the wait must be bounded by its timeout");

        never.SetResult();
        Assert.True(reaper.WaitForPending(5000));
    }

    /// <summary>
    /// CloseNative has to reach the interface implementation: the task APIs implement IDisposable
    /// explicitly, so calling Close() instead silently leaks the native TaskRunner on every restart.
    /// </summary>
    [Fact]
    public void CloseNative_UsesTheInterfaceImplementation()
    {
        var explicitOnly = new ExplicitlyDisposable();
        MediaPipeWorkerReaper.CloseNative(explicitOnly);
        Assert.True(explicitOnly.Disposed);

        // Not disposable at all: must be a no-op rather than a throw.
        MediaPipeWorkerReaper.CloseNative(new object());
        MediaPipeWorkerReaper.CloseNative(null);
    }

    private sealed class ExplicitlyDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        void IDisposable.Dispose() => Disposed = true;
    }
}

/// <summary>
/// The init pacing that keeps a broken model from rebuilding a native pipeline on every frame.
/// </summary>
public class MediaPipeInitRetryTests
{
    /// <summary>
    /// The backoff the operators compute: 1 s, 2 s, 4 s, ... capped at 30 s. A regression to a fixed
    /// short delay is a frame-rate hit; a regression to no delay is an exception + a log line per frame.
    /// </summary>
    [Theory]
    [InlineData(0, 1_000L)]
    [InlineData(1, 2_000L)]
    [InlineData(2, 4_000L)]
    [InlineData(3, 8_000L)]
    [InlineData(4, 16_000L)]
    [InlineData(5, 30_000L)]
    [InlineData(6, 30_000L)]
    [InlineData(60, 30_000L)]
    public void Backoff_GrowsExponentiallyAndCaps(int attempts, long expectedMs)
    {
        // The operators call this exact helper, so the curve they pace their retries with is what is
        // asserted here - not a copy of the expression that would keep passing after a regression.
        Assert.Equal(expectedMs, MediaPipeInitRetry.BackoffMs(attempts));
    }
}

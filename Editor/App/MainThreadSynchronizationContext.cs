#nullable enable
using System.Collections.Concurrent;
using System.Threading;

namespace T3.Editor.App;

/// <summary>
/// Brings continuations back to the main thread, as WinForms' context did: an <c>await</c> started there resumes
/// there, and <c>TaskScheduler.FromCurrentSynchronizationContext</c> works. The editor's loop runs the queued
/// work once per pass through <see cref="RunPending"/>.
/// </summary>
internal sealed class MainThreadSynchronizationContext : SynchronizationContext
{
    private MainThreadSynchronizationContext(int mainThreadId)
    {
        _mainThreadId = mainThreadId;
    }

    /// <summary>Call once, on the main thread, before anything awaits.</summary>
    public static void Install()
    {
        _instance = new MainThreadSynchronizationContext(Environment.CurrentManagedThreadId);
        SetSynchronizationContext(_instance);
    }

    /// <summary>Runs everything posted so far. Work posted while it runs waits for the next call.</summary>
    public static void RunPending()
    {
        var instance = _instance;
        if (instance == null)
            return;

        for (var count = instance._queue.Count; count > 0 && instance._queue.TryDequeue(out var work); count--)
        {
            try
            {
                work.Callback(work.State);
            }
            catch (Exception e)
            {
                Log.Error($"A continuation on the main thread failed: {e}");
            }
        }
    }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        _queue.Enqueue((callback, state));
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            callback(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Exception? exception = null;
        Post(_ =>
             {
                 try
                 {
                     callback(state);
                 }
                 catch (Exception e)
                 {
                     exception = e;
                 }
                 finally
                 {
                     done.Set();
                 }
             }, null);
        done.Wait();

        if (exception != null)
            throw new InvalidOperationException("A call sent to the main thread failed.", exception);
    }

    public override SynchronizationContext CreateCopy() => this;

    private static MainThreadSynchronizationContext? _instance;
    private readonly int _mainThreadId;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
}

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Utils;

/// <summary>
/// Runs an operator's expensive computation on a worker thread so the UI keeps its
/// frame rate. The op keeps publishing the last finished result while a newer one
/// is computed; when rendering to file, <see cref="Playback.OpNotReady"/> makes the
/// render loop wait for the pending result instead of exporting a stale frame.
///
/// Usage per frame from an op's Update:
/// <code>
/// var result = _asyncComputation.Update(context, Result, inputsVersion, token => ComputeNewResult(token));
/// Result.Value = result ?? fallback;
/// </code>
/// <c>inputsVersion</c> must change whenever any input changes (combine parameter
/// values and content-version counters of input data). The compute function must
/// only touch data captured for it - never live op state shared with other frames -
/// and should call <c>token.ThrowIfCancellationRequested()</c> in its main loops:
/// when the inputs change again mid-computation the job is cancelled, so the newer
/// inputs don't have to wait for a result nobody wants anymore.
/// </summary>
public sealed class AsyncComputation<T> where T : class
{
    public bool IsComputing => _runningTask != null;

    /// <summary>
    /// Collects a finished computation, starts a new one if the inputs changed, and
    /// maintains the slot's update trigger and the export handshake. Returns the
    /// latest finished result - null until the first one lands.
    /// </summary>
    public T? Update(EvaluationContext context, Slot<T> resultSlot, int inputsVersion, Func<CancellationToken, T> compute)
    {
        // Newer inputs supersede the running job - ask it to stop early
        if (_runningTask is { IsCompleted: false } && _runningVersion != inputsVersion)
            _cancellation?.Cancel();

        _resultJustLanded = false;

        if (_runningTask is { IsCompleted: true })
        {
            if (_runningTask.IsCompletedSuccessfully)
            {
                _latestResult = _runningTask.Result;
                _computedVersion = _runningVersion;
                _resultJustLanded = true;
            }
            else if (_runningTask.IsCanceled || _cancellation is { IsCancellationRequested: true })
            {
                // Superseded - _computedVersion stays stale so the newer inputs start right away
            }
            else
            {
                // Inputs may have been mutated mid-read by an upstream op; a later
                // version change retries, so don't spin on the same failed inputs.
                Log.Warning($"Async computation failed: {_runningTask.Exception?.InnerException?.Message}");
                _computedVersion = _runningVersion;
            }

            _runningTask = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }

        if (_runningTask == null && _computedVersion != inputsVersion)
        {
            _runningVersion = inputsVersion;
            _progress = 0;
            _jobStartTime = System.Diagnostics.Stopwatch.GetTimestamp();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _runningTask = Task.Run(() => compute(token), token);
        }

        var isComputing = _runningTask != null;

        // A result lands inside an Update, whose dirty flag is cleared right after - so a
        // consumer that didn't pull this op in exactly that evaluation would never see the
        // new value. Staying dirty for one more evaluation gives the invalidation pass a
        // chance to propagate the change before the trigger is released.
        var keepDirty = isComputing || _resultJustLanded;
        resultSlot.DirtyFlag.Trigger = keepDirty ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        if (isComputing && context.Playback.IsRenderingToFile)
            Playback.OpNotReady = true;

        return _latestResult;
    }

    /// <summary>
    /// Cancels a pending computation and blocks until it has exited. Call before
    /// computing synchronously (e.g. when the op's Async parameter was just switched
    /// off), so the worker can't race the synchronous computation on shared scratch
    /// buffers. Also releases the slot's compute trigger, which would otherwise keep
    /// the op re-evaluating every frame.
    /// </summary>
    public void WaitForPending(Slot<T> resultSlot)
    {
        resultSlot.DirtyFlag.Trigger = DirtyFlagTrigger.None;
        _resultJustLanded = false;
        if (_runningTask == null)
            return;

        try
        {
            _cancellation?.Cancel();
            _runningTask.Wait();
        }
        catch (AggregateException)
        {
            // Cancelled or failed - either way it has exited; collected by the next Update
        }
    }

    /// <summary>
    /// Called from the compute function to report progress in 0..1. Safe to call
    /// from the worker thread; only one job runs at a time.
    /// </summary>
    public void ReportProgress(float progress)
    {
        _progress = progress;
    }

    /// <summary>
    /// True when a job has been running longer than <see cref="UiProgressDelay"/> -
    /// short computations finish without flashing a progress bar. Feed this into
    /// <see cref="T3.Core.Operator.Interfaces.IProgressProvider"/>.
    /// </summary>
    public bool TryGetUiProgress(out float progress)
    {
        progress = _progress;
        if (_runningTask == null)
            return false;

        var elapsedSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - _jobStartTime)
                             / (double)System.Diagnostics.Stopwatch.Frequency;
        return elapsedSeconds > UiProgressDelay;
    }

    private const double UiProgressDelay = 0.5;

    private Task<T>? _runningTask;
    private CancellationTokenSource? _cancellation;
    private T? _latestResult;
    private int _computedVersion = -1;
    private int _runningVersion;
    private bool _resultJustLanded;
    private volatile float _progress;
    private long _jobStartTime;
}

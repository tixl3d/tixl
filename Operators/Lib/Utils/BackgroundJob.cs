#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace Lib.Utils;

/// <summary>What <see cref="BackgroundJob{TKey,TResult}.Poll"/> has for the operator this evaluation.</summary>
internal enum BackgroundJobOutcome
{
    /// <summary>Nothing new: keep the outputs as they are (the last result, or a job is still running).</summary>
    Unchanged,

    /// <summary>A finished result is ready to publish.</summary>
    Ready,

    /// <summary>The job threw. The error was logged once; the operator should clear its outputs.</summary>
    Failed,
}

/// <summary>
/// Runs an operator's CPU-heavy computation on a worker thread so the frame never waits for it. The work must
/// depend only on <typeparamref name="TKey"/> (inputs, and references to immutable data such as a <c>VdbVolume</c>)
/// and must not touch GPU resources: the operator uploads a finished result on the evaluation thread.
/// </summary>
/// <remarks>
/// One job runs at a time and the latest key wins; a finished job is still published, so a continuously changing
/// input shows results one step behind rather than none. While a job runs the outputs are re-evaluated every frame
/// (<see cref="DirtyFlagTrigger.Animated"/>): a wake-up bumped from the worker could land inside the update and be
/// cleared with it. Rendering to a file waits for every result. A key that failed is not retried until it changes.
/// </remarks>
internal sealed class BackgroundJob<TKey, TResult> where TKey : IEquatable<TKey>
{
    private readonly record struct JobResult(TResult? Value, string? Error);

    /// <param name="owner">The operator, for log context.</param>
    /// <param name="name">Operator name used in log messages.</param>
    /// <param name="work">Pure function of the key. Pass a cached delegate (a method group stored once), not a closure per call.</param>
    /// <param name="outputs">Every output slot whose update action polls this job.</param>
    public BackgroundJob(IGuidPathContainer? owner, string name, Func<TKey, TResult> work, params ISlot[] outputs)
        : this(owner, name, work, null, outputs)
    {
    }

    /// <param name="prepare">
    /// Called on the evaluation thread right before a job starts, to copy mutable inputs (a reused point list,
    /// a mesh edited in place) into the key the worker reads. Its equality must ignore the copied data.
    /// </param>
    public BackgroundJob(IGuidPathContainer? owner, string name, Func<TKey, TResult> work, Func<TKey, TKey>? prepare, params ISlot[] outputs)
    {
        _prepare = prepare;
        _owner = owner;
        _name = name;
        _work = work;
        _outputs = outputs;
        _originalTriggers = new DirtyFlagTrigger[outputs.Length];
        for (var i = 0; i < outputs.Length; i++)
            _originalTriggers[i] = outputs[i].DirtyFlag.Trigger;
    }

    /// <summary>The most recent error message, or null after a success.</summary>
    public string? LastError => _lastError;

    /// <summary>True while a computation is in flight.</summary>
    public bool IsBusy => _task != null;

    /// <summary>
    /// Call on every evaluation with the current key. Returns <see cref="BackgroundJobOutcome.Ready"/> with the
    /// result to publish, <see cref="BackgroundJobOutcome.Failed"/> when the job threw, and otherwise
    /// <see cref="BackgroundJobOutcome.Unchanged"/>. Allocation-free while the key is unchanged.
    /// </summary>
    public BackgroundJobOutcome Poll(EvaluationContext context, in TKey key, out TResult? result)
        => Poll(IsRenderingToFile(context), key, out result);

    /// <summary><see cref="Poll(EvaluationContext, in TKey, out TResult)"/> with the render-to-file state given directly.</summary>
    /// <param name="mustFinish">Run every job to completion before returning, as when rendering to a file.</param>
    public BackgroundJobOutcome Poll(bool mustFinish, in TKey key, out TResult? result)
    {
        result = default;
        var outcome = BackgroundJobOutcome.Unchanged;

        if (_task != null && (mustFinish || _task.IsCompleted))
            outcome = Harvest(out result);

        // Latest wins: a new key that arrives while a job runs starts once that job is harvested.
        if (_task == null && !IsKnown(key))
        {
            Start(key);
            if (mustFinish)
            {
                _task!.Wait();
            }
            else if (!_hasPublished)
            {
                // Only the first result is waited for: a chain of jobs that each waited on every
                // change would add their waits up on every frame of an animated input.
                _task!.Wait(InlineWaitMs);
            }

            if (_task!.IsCompleted)
                outcome = Harvest(out result);
        }

        UpdateTriggers();
        return outcome;
    }

    /// <summary>
    /// True when <see cref="Poll"/> with this key would start a job now. Lets an operator copy mutable input data
    /// into the key only when a job will actually read it, instead of on every evaluation.
    /// </summary>
    public bool NeedsStart(EvaluationContext context, in TKey key)
        => !IsKnown(key) && (_task == null || _task.IsCompleted || IsRenderingToFile(context));

    private static bool IsRenderingToFile(EvaluationContext context) => context.Playback is { IsRenderingToFile: true };

    /// <summary>Forget the running job and the remembered keys, e.g. when an input became invalid.</summary>
    public void Reset()
    {
        // A running computation cannot be aborted; dropping the task discards its result.
        _task = null;
        _hasPublished = false;
        _hasFailed = false;
        _publishedKey = default!;
        _failedKey = default!;
        UpdateTriggers();
    }

    private bool IsKnown(in TKey key)
    {
        var comparer = EqualityComparer<TKey>.Default;
        if (_task != null && comparer.Equals(key, _taskKey))
            return true;

        if (_hasPublished && comparer.Equals(key, _publishedKey))
            return true;

        return _hasFailed && comparer.Equals(key, _failedKey);
    }

    private void Start(TKey key)
    {
        if (_prepare != null)
            key = _prepare(key);

        _taskKey = key;
        var work = _work;
        _task = Task.Run(() =>
                         {
                             try
                             {
                                 return new JobResult(work(key), null);
                             }
                             catch (Exception e)
                             {
                                 // Reported on the evaluation thread when the result is harvested.
                                 return new JobResult(default, e.Message);
                             }
                         });
    }

    private BackgroundJobOutcome Harvest(out TResult? result)
    {
        var task = _task!;
        var key = _taskKey;
        _task = null;
        _taskKey = default!;

        var jobResult = task.Result;
        if (jobResult.Error != null)
        {
            if (_lastError != jobResult.Error)
                Log.Warning($"{_name} failed: {jobResult.Error}", _owner);

            _lastError = jobResult.Error;
            _failedKey = key;
            _hasFailed = true;
            _hasPublished = false;
            result = default;
            return BackgroundJobOutcome.Failed;
        }

        _lastError = null;
        _publishedKey = key;
        _hasPublished = true;
        _hasFailed = false;
        result = jobResult.Value;
        return BackgroundJobOutcome.Ready;
    }

    private void UpdateTriggers()
    {
        var busy = _task != null;
        if (busy == _triggersRaised)
            return;

        _triggersRaised = busy;
        for (var i = 0; i < _outputs.Length; i++)
            _outputs[i].DirtyFlag.Trigger = busy ? DirtyFlagTrigger.Animated : _originalTriggers[i];
    }

    /// <summary>How long the first evaluation waits for the first result before handing the job to the next frames.</summary>
    private const int InlineWaitMs = 4;

    private readonly IGuidPathContainer? _owner;
    private readonly string _name;
    private readonly Func<TKey, TResult> _work;
    private readonly Func<TKey, TKey>? _prepare;
    private readonly ISlot[] _outputs;
    private readonly DirtyFlagTrigger[] _originalTriggers;

    private Task<JobResult>? _task;
    private TKey _taskKey = default!;
    private TKey _publishedKey = default!;
    private TKey _failedKey = default!;
    private bool _hasPublished;
    private bool _hasFailed;
    private bool _triggersRaised;
    private string? _lastError;
}

/// <summary>
/// For operators that run their own worker task: keeps the outputs re-evaluating while the task is in flight.
/// </summary>
/// <remarks>
/// Bumping an output's version from the worker is not a reliable wake-up: when the bump lands while the
/// evaluation thread is inside the update, the slot's flag is cleared right after and the finished result
/// is never harvested in a static graph. Polling with <see cref="DirtyFlagTrigger.Animated"/> until the
/// result is in cannot miss it. Use only on outputs whose trigger is otherwise <see cref="DirtyFlagTrigger.None"/>.
/// </remarks>
internal static class BackgroundPolling
{
    public static void Set(bool busy, ISlot slot)
    {
        slot.DirtyFlag.Trigger = busy ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
    }

    public static void Set(bool busy, ISlot[] slots)
    {
        var trigger = busy ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        for (var i = 0; i < slots.Length; i++)
            slots[i].DirtyFlag.Trigger = trigger;
    }
}

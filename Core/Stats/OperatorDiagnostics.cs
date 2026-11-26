using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Utils;

namespace T3.Core.Stats;

/// <summary>
/// Collects warnings and errors that operators push about themselves, so problems in nested instances can be
/// shown on the operators that contain them. Every change also updates a per-parent count of problems inside,
/// which makes <see cref="GetStatusInside"/> a single lookup.
/// </summary>
public static class OperatorDiagnostics
{
    public static void LogWarningState(this Instance instance, string message)
    {
        Logging.Log.Warning(message, instance);
        KeepState(instance, message, Statuses.HasWarning);
    }

    public static void LogErrorState(this Instance instance, string message)
    {
        Logging.Log.Error(message, instance);
        KeepState(instance, message, Statuses.HasError);
    }

    public static void ClearErrorState(this Instance instance)
    {
        // Called on every successful update of some ops, so skip the hashing and locking when nothing is failing.
        if (_statusUpdates.IsEmpty)
            return;

        var hash = OperatorUtils.ComputeInstanceHash(instance.InstancePath);
        lock (_lock)
        {
            if (!_statusUpdates.TryRemove(hash, out var removed))
                return;

            AddToParentCounts(removed, -1);
        }
    }

    public static void ClearAll()
    {
        lock (_lock)
        {
            _statusUpdates.Clear();
            _problemCountsInside.Clear();
        }
    }

    /// <summary>
    /// The worst problem any instance nested (at any depth) inside this one has reported, or <see cref="Statuses.Okay"/>.
    /// </summary>
    public static Statuses GetStatusInside(Instance instance)
    {
        if (_problemCountsInside.IsEmpty)
            return Statuses.Okay;

        var hash = OperatorUtils.ComputeInstanceHash(instance.InstancePath);
        if (!_problemCountsInside.TryGetValue(hash, out var counts))
            return Statuses.Okay;

        return counts.Errors > 0 ? Statuses.HasError : Statuses.HasWarning;
    }

    /// <summary>
    /// The most severe, then most recent, problem reported from inside this instance. Scans all entries, so only
    /// call it for a hovered item, not every frame.
    /// </summary>
    public static bool TryGetProblemInside(Instance instance, out StatusUpdate? problem)
    {
        problem = null;
        var parentPath = instance.InstancePath;
        foreach (var update in _statusUpdates.Values)
        {
            if (!IsNestedIn(update.IdPath, parentPath))
                continue;

            if (problem == null
                || update.Statuses > problem.Statuses
                || (update.Statuses == problem.Statuses && update.Time > problem.Time))
            {
                problem = update;
            }
        }

        return problem != null;
    }

    public static bool TryGetOwnProblem(Instance instance, out StatusUpdate? problem)
    {
        problem = null;
        if (_statusUpdates.IsEmpty)
            return false;

        return _statusUpdates.TryGetValue(OperatorUtils.ComputeInstanceHash(instance.InstancePath), out problem);
    }

    public static IReadOnlyDictionary<long, StatusUpdate> StatusUpdates => _statusUpdates;

    public static double LastChangeTime { get; private set; }

    public enum Statuses
    {
        Unknown = 0,
        Okay = 1,
        HasWarning = 2,
        HasError = 3,
    }

    public sealed record StatusUpdate(
        long InstanceHash,
        IReadOnlyList<Guid> IdPath,
        string Message,
        Statuses Statuses,
        double Time);

    private static void KeepState(Instance instance, string message, Statuses status)
    {
        var hash = OperatorUtils.ComputeInstanceHash(instance.InstancePath);
        var update = new StatusUpdate(hash, instance.InstancePath, message, status, Playback.RunTimeInSecs);
        lock (_lock)
        {
            if (_statusUpdates.TryGetValue(hash, out var previous))
                AddToParentCounts(previous, -1);

            _statusUpdates[hash] = update;
            AddToParentCounts(update, 1);
        }

        LastChangeTime = Playback.RunTimeInSecs;
    }

    /** Every proper prefix of the path is the path of an instance containing the one that reported. */
    private static void AddToParentCounts(StatusUpdate update, int delta)
    {
        var isError = update.Statuses == Statuses.HasError;
        for (var parentDepth = 1; parentDepth < update.IdPath.Count; parentDepth++)
        {
            var parentHash = OperatorUtils.ComputeInstanceHash(update.IdPath, parentDepth);
            _problemCountsInside.TryGetValue(parentHash, out var counts);
            counts = isError
                         ? counts with { Errors = counts.Errors + delta }
                         : counts with { Warnings = counts.Warnings + delta };

            if (counts.Errors <= 0 && counts.Warnings <= 0)
            {
                _problemCountsInside.TryRemove(parentHash, out _);
            }
            else
            {
                _problemCountsInside[parentHash] = counts;
            }
        }
    }

    private static bool IsNestedIn(IReadOnlyList<Guid> path, IReadOnlyList<Guid> parentPath)
    {
        if (path.Count <= parentPath.Count)
            return false;

        for (var index = 0; index < parentPath.Count; index++)
        {
            if (path[index] != parentPath[index])
                return false;
        }

        return true;
    }

    private readonly record struct ProblemCounts(int Warnings, int Errors);

    /** Serializes changes, which can come from shader compiles on other threads; reads stay lock-free. */
    private static readonly object _lock = new();

    private static readonly ConcurrentDictionary<long, StatusUpdate> _statusUpdates = [];
    private static readonly ConcurrentDictionary<long, ProblemCounts> _problemCountsInside = [];
}

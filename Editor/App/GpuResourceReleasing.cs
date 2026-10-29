#nullable enable
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.App;

/// <summary>
/// Releases the GPU resources of operators that can rebuild them (<see cref="IReleasableGpuResources"/>): the largest
/// unused ones while TiXL is over its GPU memory limit, optionally any that went unused for a set time, and all of
/// them on request. A released operator re-renders the next time something pulls it, which can cost a frame — so
/// by default nothing is released while memory is plentiful.
/// </summary>
/// <remarks>
/// "Reached" is the invalidation walk, not an update: a render target whose inputs never change is not updated,
/// but every frame that shows it walks through it, so it is never mistaken for unused.
/// </remarks>
internal static class GpuResourceReleasing
{
    /// <summary>Takes effect at the start of the next frame, when nothing is being evaluated.</summary>
    public static void RequestReleaseAll() => _releaseAllRequested = true;

    /// <summary>Call at the start of a frame, before any output is evaluated.</summary>
    public static void Update()
    {
        if (_releaseAllRequested)
        {
            _releaseAllRequested = false;
            Release(int.MaxValue, long.MaxValue, "on request");

            // Resources that operators dropped without disposing go too.
            GC.Collect();
            return;
        }

        var now = Playback.RunTimeInSecs;
        if (now - _lastPassTime < PassIntervalSec)
            return;

        _lastPassTime = now;
        RecordTick(now);

        GpuMemoryBudget.UpdateFrame();
        if (GpuMemoryBudget.IsOverLimit && TryGetTickAt(now - PressureMinUnusedSec, out var recentTick))
        {
            // Releasing down to a margin below the limit, so usage hovering at it does not release every second.
            var target = GpuMemoryBudget.LimitBytes - GpuMemoryBudget.BudgetBytes * LimitMarginPercent / 100;
            Release(recentTick, GpuMemoryBudget.UsageBytes - target, "over the GPU memory limit");
            return;
        }

        var unusedSeconds = UserSettings.Config.ReleaseUnusedRenderTargetsAfterSeconds;
        if (unusedSeconds > 0 && TryGetTickAt(now - unusedSeconds, out var idleTick))
            Release(idleTick, long.MaxValue, $"unused for {unusedSeconds}s");
    }

    /// <param name="reachedBeforeTick">Only owners whose outputs no invalidation pass reached since this tick.</param>
    /// <param name="bytesWanted">Stops once this much was released, largest first.</param>
    private static void Release(int reachedBeforeTick, long bytesWanted, string reason)
    {
        _candidates.Clear();
        ReleasableGpuResources.CollectLive(_candidates);

        _unused.Clear();
        foreach (var owner in _candidates)
        {
            if (owner is Instance instance && LastReachedTick(instance) < reachedBeforeTick && owner.ReleasableBytes > 0)
                _unused.Add(owner);
        }

        _candidates.Clear();
        if (_unused.Count == 0)
            return;

        if (bytesWanted != long.MaxValue)
            _unused.Sort(_largestFirst);

        long releasedBytes = 0;
        var releasedCount = 0;
        foreach (var owner in _unused)
        {
            if (releasedBytes >= bytesWanted)
                break;

            var bytes = owner.ReleasableBytes;
            if (!owner.TryReleaseGpuResources())
                continue;

            releasedBytes += bytes;
            releasedCount++;
        }

        _unused.Clear();
        if (releasedCount > 0)
            Log.Debug($"Released GPU resources of {releasedCount} operators ({releasedBytes >> 20} MB): {reason}.");
    }

    private static int LastReachedTick(Instance instance)
    {
        var latest = int.MinValue;
        foreach (var output in instance.Outputs)
        {
            latest = Math.Max(latest, output.DirtyFlag.LastInvalidationTick);
        }

        return latest;
    }

    /// <summary>
    /// The invalidation tick is counted in passes, not seconds, and a frame makes a varying number of them. Keeping
    /// what it was at each past pass turns "unused for N seconds" into a measured tick.
    /// </summary>
    private static void RecordTick(double now)
    {
        _tickHistory.Enqueue((now, DirtyFlag.GlobalInvalidationTick));

        var keepSeconds = Math.Max(UserSettings.Config.ReleaseUnusedRenderTargetsAfterSeconds, PressureMinUnusedSec) + 2 * PassIntervalSec;
        while (_tickHistory.Count > 0 && _tickHistory.Peek().Time < now - keepSeconds)
        {
            _tickHistory.Dequeue();
        }
    }

    /// <summary>The tick of the latest pass at or before <paramref name="time"/>. False until history reaches back that far.</summary>
    private static bool TryGetTickAt(double time, out int tick)
    {
        tick = 0;
        var found = false;
        foreach (var (sampleTime, sampleTick) in _tickHistory)
        {
            if (sampleTime > time)
                break;

            tick = sampleTick;
            found = true;
        }

        return found;
    }

    private const double PassIntervalSec = 1;

    /** Under pressure, anything unused for this long goes — still long enough not to release what a cut back to would show. */
    private const double PressureMinUnusedSec = 2;

    private const long LimitMarginPercent = 10;

    private static bool _releaseAllRequested;
    private static double _lastPassTime = double.NegativeInfinity;
    private static readonly Queue<(double Time, int Tick)> _tickHistory = new();
    private static readonly List<IReleasableGpuResources> _candidates = [];
    private static readonly List<IReleasableGpuResources> _unused = [];
    private static readonly Comparison<IReleasableGpuResources> _largestFirst = (a, b) => b.ReleasableBytes.CompareTo(a.ReleasableBytes);
}

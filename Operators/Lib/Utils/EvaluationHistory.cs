#nullable enable
using T3.Core.Animation;

namespace Lib.Utils;

/// <summary>
/// Keeps one history slot per evaluation of an operator within a frame, so each evaluation is compared with the same
/// evaluation one frame earlier. An op is evaluated several times per frame by [Loop] iterations, motion blur passes,
/// cube map faces, and by other windows (thumbnails, previews) that use their own <see cref="EvaluationContext"/>.
/// </summary>
/// <remarks>
/// Evaluations are matched by context, repeat pass and cube face (<see cref="EvaluationContext.RenderPass"/>) and call order within that pass. An
/// instance reached in a different order with the same number of calls swaps slots unnoticed.
/// </remarks>
internal sealed class EvaluationHistory<TSlot> : IDisposable where TSlot : class, IDisposable
{
    public EvaluationHistory(Func<TSlot> createSlot, int maxCallsPerPass)
    {
        _createSlot = createSlot;
        _maxCallsPerPass = maxCallsPerPass;
    }

    /// <summary>
    /// Counts this evaluation and returns its slot. <paramref name="previousSeenFrame"/> is the
    /// <see cref="Playback.FrameCount"/> this evaluation last happened in (by either method), or -1 for a new slot.
    /// <paramref name="isFirstEvaluation"/> is true for the first call of the first pass, which is the only
    /// evaluation an op without loops or repeat passes has.
    /// Returns false above the call limit; the caller then has no history for this evaluation.
    /// </summary>
    public bool TryGetSlot(EvaluationContext context,
                           [NotNullWhen(true)] out TSlot? slot,
                           out bool isFirstEvaluation,
                           out int previousSeenFrame)
    {
        slot = null;
        previousSeenFrame = -1;

        var passCalls = CountCall(context, out var passIndex, out var callIndex);
        isFirstEvaluation = passIndex == 0 && callIndex == 0;
        if (callIndex >= _maxCallsPerPass)
            return false;

        while (passCalls.Entries.Count <= callIndex)
        {
            passCalls.Entries.Add(new Entry(_createSlot()));
        }

        var entry = passCalls.Entries[callIndex];
        previousSeenFrame = entry.LastSeenFrame;
        entry.LastSeenFrame = Playback.FrameCount;
        slot = entry.Slot;
        return true;
    }

    /// <summary>
    /// Counts this evaluation like <see cref="TryGetSlot"/> but returns the slot of the matching evaluation in the
    /// first pass. For evaluations that repeat an earlier one instead of advancing (paused time, motion blur passes).
    /// </summary>
    public bool TryGetFirstPassSlot(EvaluationContext context, [NotNullWhen(true)] out TSlot? slot)
    {
        slot = null;
        CountCall(context, out var passIndex, out var callIndex);

        var firstPass = _contextHistory?.Passes[0];
        if (firstPass == null || callIndex >= firstPass.Entries.Count)
            return false;

        var entry = firstPass.Entries[callIndex];
        entry.LastSeenFrame = Playback.FrameCount;
        slot = entry.Slot;
        return true;
    }

    public void Dispose()
    {
        for (var contextIndex = 0; contextIndex < _contexts.Count; contextIndex++)
        {
            _contexts[contextIndex].Dispose();
        }

        _contexts.Clear();
        _contextHistory = null;
    }

    private PassCalls CountCall(EvaluationContext context, out int passIndex, out int callIndex)
    {
        var frame = Playback.FrameCount;
        if (frame != _lastSweepFrame)
        {
            _lastSweepFrame = frame;
            ReleaseUnused(frame);
        }

        var contextHistory = GetContextHistory(context);
        contextHistory.LastUseFrame = frame;
        _contextHistory = contextHistory;

        // Motion blur samples and cube faces each keep their own history.
        passIndex = Math.Clamp(context.RenderPass.RepeatPass * CubeFaceCount + context.RenderPass.CubeFace, 0, MaxPasses - 1);

        if (passIndex >= contextHistory.Passes.Length)
            Array.Resize(ref contextHistory.Passes, passIndex + 1);

        var passCalls = contextHistory.Passes[passIndex] ??= new PassCalls();
        if (passCalls.Frame != frame)
        {
            passCalls.Frame = frame;
            passCalls.Count = 0;
        }

        callIndex = passCalls.Count++;
        return passCalls;
    }

    private ContextHistory GetContextHistory(EvaluationContext context)
    {
        for (var contextIndex = 0; contextIndex < _contexts.Count; contextIndex++)
        {
            if (ReferenceEquals(_contexts[contextIndex].Context, context))
                return _contexts[contextIndex];
        }

        var contextHistory = new ContextHistory(context);
        _contexts.Add(contextHistory);
        return contextHistory;
    }

    /** Frees contexts that stopped evaluating this op, and slots of calls that no longer happen (a loop got shorter). */
    private void ReleaseUnused(int frame)
    {
        for (var contextIndex = _contexts.Count - 1; contextIndex >= 0; contextIndex--)
        {
            var contextHistory = _contexts[contextIndex];
            if (frame - contextHistory.LastUseFrame > ReleaseAfterFrames)
            {
                contextHistory.Dispose();
                _contexts.RemoveAt(contextIndex);
                if (ReferenceEquals(_contextHistory, contextHistory))
                    _contextHistory = null;

                continue;
            }

            foreach (var passCalls in contextHistory.Passes)
            {
                passCalls?.ReleaseTrailingUnused(frame);
            }
        }
    }

    private sealed class Entry(TSlot slot)
    {
        public readonly TSlot Slot = slot;
        public int LastSeenFrame = -1;
    }

    private sealed class PassCalls
    {
        public readonly List<Entry> Entries = new();
        public int Frame = -1;
        public int Count;

        public void ReleaseTrailingUnused(int frame)
        {
            for (var entryIndex = Entries.Count - 1; entryIndex >= 0; entryIndex--)
            {
                var entry = Entries[entryIndex];
                if (frame - entry.LastSeenFrame <= ReleaseAfterFrames)
                    return;

                entry.Slot.Dispose();
                Entries.RemoveAt(entryIndex);
            }
        }

        public void Dispose()
        {
            foreach (var entry in Entries)
            {
                entry.Slot.Dispose();
            }

            Entries.Clear();
        }
    }

    /** Holds the context only until it stops evaluating this op for <see cref="ReleaseAfterFrames"/>. */
    private sealed class ContextHistory(EvaluationContext context)
    {
        public readonly EvaluationContext Context = context;
        public PassCalls?[] Passes = new PassCalls?[1];
        public int LastUseFrame;

        public void Dispose()
        {
            foreach (var passCalls in Passes)
            {
                passCalls?.Dispose();
            }
        }
    }

    private const int CubeFaceCount = 6;

    /** Bounds the per-pass history against unexpected pass indices: 64 motion blur samples of up to 6 cube faces. */
    private const int MaxPasses = 64 * CubeFaceCount;

    private const int ReleaseAfterFrames = 300;

    private readonly Func<TSlot> _createSlot;
    private readonly int _maxCallsPerPass;
    private readonly List<ContextHistory> _contexts = new(2);

    /** The context of the last counted call, for <see cref="TryGetFirstPassSlot"/>. */
    private ContextHistory? _contextHistory;

    private int _lastSweepFrame = -1;
}

#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Audio;
using T3.Core.Operator.Slots;

namespace Lib.flow;

/// <summary>
/// Executes timeline Command time clips. Collects clips two ways: wired into <see cref="Clips"/>,
/// and — with <see cref="AutoCollect"/> on — every sibling child with an unconnected Command time-clip
/// output in the same composition. The graph-independent "drop a clip on the timeline and it renders"
/// path, mirroring how <c>[AudioClipPlayer]</c> auto-collects <c>[AudioClip]</c>s. Each clip's own
/// time-range gate and source-time remapping still apply (see <see cref="TimeClipSlot{T}"/>).
/// </summary>
[Guid("fb61e317-4b12-46c6-85d1-5925ccce09cd")]
internal sealed class TimeClipPlayer : Instance<TimeClipPlayer>
{
    [Output(Guid = "2c8581cc-c72e-4fe7-b251-80c2874d15b3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Command> Output = new();

    public TimeClipPlayer()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Wired clips execute first, in input order.
        var wired = Clips.CollectedInputs;
        for (var i = 0; i < wired.Count; i++)
        {
            Execute(wired[i], context);
        }

        Clips.DirtyFlag.Clear();

        if (!AutoCollect.GetValue(context) || Parent == null)
            return;

        // Rebuild the sibling list only on a Parent change (hot-reload → new instance) or a graph
        // structure change — not every frame. A clip is auto-collected only while its time-clip output
        // has no outgoing connection; wiring it anywhere hands control to that consumer.
        if (!ReferenceEquals(_cachedParent, Parent) || _cachedStructureVersion != Parent.Symbol.VersionCounter)
        {
            _cachedParent = Parent;
            _cachedStructureVersion = Parent.Symbol.VersionCounter;
            _autoCollected.Clear();

            _connectedOutputs.Clear();
            var connections = Parent.Symbol.Connections;
            for (var i = 0; i < connections.Count; i++)
            {
                _connectedOutputs.Add((connections[i].SourceParentOrChildId, connections[i].SourceSlotId));
            }

            foreach (var child in Parent.Children.Values)
            {
                // Audio clips expose a Command time-clip slot for their timeline presence, but they are
                // driven by [AudioClipPlayer] / the audio graph — executing them here would double-drive.
                if (child == this || child is IAudioClipProvider)
                    continue;

                var childOutputs = child.Outputs;
                for (var outputIndex = 0; outputIndex < childOutputs.Count; outputIndex++)
                {
                    // Only the clip output being unwired matters — other outputs of the same op may
                    // be connected elsewhere without claiming the clip.
                    if (childOutputs[outputIndex] is TimeClipSlot<Command> clipSlot
                        && !_connectedOutputs.Contains((child.SymbolChildId, clipSlot.Id)))
                    {
                        _autoCollected.Add(clipSlot);
                        break;
                    }
                }
            }
        }

        // Higher LayerIndex sits on a lower timeline row and executes first, so clips on upper rows
        // draw on top — matching the visual stacking. Ties execute left-to-right.
        _autoCollected.Sort(_executionOrder);

        for (var i = 0; i < _autoCollected.Count; i++)
        {
            // No connection reaches these slots, so the regular invalidation pass never marks them
            // dirty — without this, GetValue returns the cached command and the subgraph never runs.
            _autoCollected[i].InvalidateGraph();
            Execute(_autoCollected[i], context);
        }
    }

    private static void Execute(Slot<Command> commandSlot, EvaluationContext context)
    {
        commandSlot.Value?.PrepareAction?.Invoke(context);
        commandSlot.GetValue(context);
        commandSlot.Value?.RestoreAction?.Invoke(context);
    }

    private static readonly Comparison<TimeClipSlot<Command>> _executionOrder
        = static (a, b) =>
          {
              var byLayer = b.TimeClip.LayerIndex.CompareTo(a.TimeClip.LayerIndex);
              return byLayer != 0 ? byLayer : a.TimeClip.TimeRange.Start.CompareTo(b.TimeClip.TimeRange.Start);
          };

    // AutoCollect scan cache — rebuilt only when Parent changes (reload) or the composition's
    // VersionCounter bumps (any edit), not every frame. Buffers reused, not reallocated.
    private Instance? _cachedParent;
    private int _cachedStructureVersion = -1;
    private readonly List<TimeClipSlot<Command>> _autoCollected = new();
    private readonly HashSet<(Guid ChildId, Guid SlotId)> _connectedOutputs = new();

    [Input(Guid = "6e16649b-b2a0-473a-980e-090dbc8ab294")]
    public readonly MultiInputSlot<Command> Clips = new();

    [Input(Guid = "3e7347b1-642c-4719-8e89-d7281b916753")]
    public readonly InputSlot<bool> AutoCollect = new();
}

#nullable enable
using System.Collections.Generic;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.DataTypes;

/// <summary>
/// THROWAWAY SPIKE (step 2a) — the value carried by an "AudioReference" wire: a structural node in the
/// audio routing graph, modelled on <see cref="ShaderGraphNode"/>. It declares topology (a leaf source, or
/// a combinator folding upstream nodes) and is traversed by a downstream *root* off the per-frame dirty
/// path; no audio samples flow on the wire. Structure + recursive collection only — the BASS realisation
/// is added in 2b. Must be registered as an input type in <c>SymbolPackage.RegisterTypes</c> (required so
/// the combinator/root <c>MultiInputSlot&lt;AudioGraphNode&gt;</c> inputs can build an input definition).
/// </summary>
public sealed class AudioGraphNode
{
    /// <summary>How the root routes a leaf source: <c>Mixable</c> joins a bus submix (default); <c>Direct</c> routes
    /// straight to the output / 3D engine (spatial hardware 3D) and can't be bus-inserted.</summary>
    public enum RoutingKind { Mixable, Direct }

    /// <summary>Leaf routing kind (combinators ignore it). Baked into the wire value now so adding it later
    /// wouldn't be a shape migration; only <c>Mixable</c> is realised until G5 wires up Direct/spatial.</summary>
    public RoutingKind Routing = RoutingKind.Mixable;

    /// <summary>Leaf identity for logging / inspection (e.g. a source label). Null for combinators.</summary>
    public string? SourceLabel;

    /// <summary>Leaf only: the source's BASS decode-stream handle, for the root to route into a bus. 0 = combinator / none.</summary>
    public int SourceChannel;

    /// <summary>Leaf only: the channel's lifetime and playback position are owned by another subsystem (the
    /// <c>AudioEngine</c> soundtrack path for an [AudioClip]), not by the graph. The root must route it
    /// <b>without</b> <c>MixerChanBuffer</c> (buffer latency breaks the engine's seek/resync) and must not free
    /// it on removal. False for graph-owned sources (tone / file) whose channel the graph creates and frees.</summary>
    public bool ExternallyManagedChannel;

    /// <summary>Per-node gain the root applies to this source's channel when routing it.</summary>
    public float Gain = 1f;

    /// <summary>Populated each traversal from the connected input nodes; empty for a leaf source.</summary>
    public readonly List<AudioGraphNode> InputNodes = new();

    /// <param name="multiInput">A combinator's node multi-input; null for a leaf source.</param>
    public AudioGraphNode(Instance instance, MultiInputSlot<AudioGraphNode>? multiInput = null)
    {
        _instance = instance;
        _multiInput = multiInput;
    }

    /// <summary>
    /// Recursively refreshes this node's connected inputs. Deduped per frame so a node shared by two
    /// roots is traversed once. Each child op's own Update (triggered here by GetValue) already refreshed
    /// its node, so the tree is fully populated once the top-level GetValue calls return.
    /// </summary>
    public void Update(EvaluationContext context)
    {
        if (_lastUpdateFrame == Playback.FrameCount)
            return;

        _lastUpdateFrame = Playback.FrameCount;

        InputNodes.Clear();
        if (_multiInput == null)
            return; // leaf source — no upstream nodes

        var inputs = _multiInput.GetCollectedTypedInputs(true);
        for (var i = 0; i < inputs.Count; i++)
        {
            var node = inputs[i].GetValue(context);
            if (node != null)
                InputNodes.Add(node);
        }

        _multiInput.DirtyFlag.Clear();
    }

    /// <summary>A leaf source reached by the root's collection, paired with its effective gain
    /// (product of ancestor combinator gains × its own gain).</summary>
    public readonly record struct CollectedSource(AudioGraphNode Leaf, float Gain);

    /// <summary>
    /// Collects reachable leaf sources (depth-first) with effective gain. A combinator folds its own
    /// <see cref="Gain"/> (e.g. a group volume) into all descendants; a leaf contributes its channel at
    /// the accumulated gain. Leaves without a channel are skipped.
    /// </summary>
    public void Collect(List<CollectedSource> results, float gainSoFar)
    {
        var gain = gainSoFar * Gain;

        if (InputNodes.Count == 0)
        {
            if (SourceChannel != 0)
                results.Add(new CollectedSource(this, gain));
            return;
        }

        for (var i = 0; i < InputNodes.Count; i++)
            InputNodes[i].Collect(results, gain);
    }

    public override string ToString() => SourceLabel ?? _instance.Symbol.Name;

    private readonly Instance _instance;
    private readonly MultiInputSlot<AudioGraphNode>? _multiInput;
    private int _lastUpdateFrame = -1;
}

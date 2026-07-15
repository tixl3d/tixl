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
    /// <summary>Leaf identity for logging / inspection (e.g. a source label). Null for combinators.</summary>
    public string? SourceLabel;

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

    /// <summary>Collects every leaf source reachable under this node (depth-first). A leaf has no input nodes.</summary>
    public void CollectLeafSources(List<AudioGraphNode> results)
    {
        if (InputNodes.Count == 0)
        {
            results.Add(this);
            return;
        }

        for (var i = 0; i < InputNodes.Count; i++)
            InputNodes[i].CollectLeafSources(results);
    }

    public override string ToString() => SourceLabel ?? _instance.Symbol.Name;

    private readonly Instance _instance;
    private readonly MultiInputSlot<AudioGraphNode>? _multiInput;
    private int _lastUpdateFrame = -1;
}

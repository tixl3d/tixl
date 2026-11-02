#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Animation;
using T3.Core.Audio.Native;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.DataTypes;

/// <summary>
/// The value carried by an "AudioReference" wire: a structural node in the
/// audio routing graph, modelled on <see cref="ShaderGraphNode"/>. It declares topology (a leaf source, or
/// a combinator folding upstream nodes) and is traversed by a downstream *root* off the per-frame dirty
/// path; no audio samples flow on the wire. Must be registered as an input type in <c>SymbolPackage.RegisterTypes</c> (required so
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

    /// <summary>Leaf only: the source for the root to route into a bus. The default handle means combinator / none.</summary>
    public AudioHandle SourceChannel;

    /// <summary>Per-node gain the root applies to this source when routing it.</summary>
    public float Gain = 1f;

    /// <summary>Stamped by a routing bus each frame it collects this leaf. Lets a source op detect that it
    /// is graph-routed even without a direct wire (an auto-collected clip), so it hands its channel over.</summary>
    public int LastCollectedFrame = -100;

    /// <summary>Combinator only: declares an insert the routing bus realises as a nested bus carrying this
    /// node's collected sources. The declaring op applies and updates the actual effect through the
    /// callbacks. Null = no insert.</summary>
    public AudioFxInsert? FxInsert;

    /// <summary>Callbacks a routing bus invokes on the nested bus realising an <see cref="FxInsert"/>.
    /// Created once per op; must not allocate per frame.</summary>
    public sealed class AudioFxInsert
    {
        /// <summary>The effect the nested bus runs; <see cref="AudioEffectKind.None"/> for a metering tap.</summary>
        public AudioEffectKind Effect = AudioEffectKind.None;

        /// <summary>Called once when the nested bus is created.</summary>
        public required Action<AudioHandle> Apply;

        /// <summary>Called every frame while realised — push current parameter values to the effect.</summary>
        public required Action<AudioHandle> UpdateParams;

        /// <summary>Called when the bus is retired, before it is freed — drop cached per-bus state.</summary>
        public required Action<AudioHandle> Remove;
    }

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

    /// <summary>
    /// Turns this node into an analysis/metering tap: it declares an insert that applies no effect, purely so a
    /// routing bus realises the sources below it as their own bus, which can be read as one signal.
    /// </summary>
    public void DeclareAnalysisTap()
    {
        FxInsert = new AudioFxInsert
                       {
                           Apply = bus => _realisedSubmixes.Add(bus),
                           UpdateParams = _ => { },
                           Remove = bus => _realisedSubmixes.Remove(bus),
                       };
    }

    /// <summary>Buses a routing bus realised for this node's insert; empty while the tap is only a side
    /// branch. More than one means several buses collected it.</summary>
    public IReadOnlyCollection<AudioHandle> RealisedSubmixes => _realisedSubmixes;

    /// <summary>
    /// The signal a tap should read: the realised bus when wired inline (post source gains), else a single leaf
    /// as the side-branch fallback. False when nothing is readable.
    /// </summary>
    public bool TryGetAnalysisChannel(out AudioHandle channel)
    {
        foreach (var bus in _realisedSubmixes)
        {
            channel = bus;
            return true;
        }

        _analysisScratch.Clear();
        Collect(_analysisScratch, 1f);
        if (_analysisScratch.Count == 1)
        {
            channel = _analysisScratch[0].Leaf.SourceChannel;
            return channel.IsValid;
        }

        channel = default;
        return false;
    }

    /// <summary>A leaf source reached by the root's collection, paired with its effective gain
    /// (product of ancestor combinator gains × its own gain) and the FX node it flows into
    /// (the *nearest* ancestor declaring an <see cref="FxInsert"/>; null = routed dry).</summary>
    public readonly record struct CollectedSource(AudioGraphNode Leaf, float Gain, AudioGraphNode? FxNode = null);

    /// <summary>An FX-declaring node encountered during collection, with the FX node enclosing it
    /// (null = it feeds the bus directly). Emitted parent-before-child, so a realiser can create the
    /// nested submix chain in list order.</summary>
    public readonly record struct FxEdge(AudioGraphNode Fx, AudioGraphNode? Parent);

    /// <summary>
    /// Collects reachable leaf sources (depth-first) with effective gain. A combinator folds its own
    /// <see cref="Gain"/> (e.g. a group volume) into all descendants; a leaf contributes its channel at
    /// the accumulated gain. Leaves without a channel are skipped. Each source is tagged with its nearest
    /// FX-declaring ancestor, and the FX nesting structure is reported through <paramref name="fxEdges"/>
    /// so chained inserts (echo into reverb) realise as nested submixes.
    /// </summary>
    public void Collect(List<CollectedSource> results, float gainSoFar, List<FxEdge>? fxEdges = null, AudioGraphNode? enclosingFx = null)
    {
        var gain = gainSoFar * Gain;

        var fx = enclosingFx;
        if (FxInsert != null)
        {
            fx = this;
            if (fxEdges != null && !ContainsFx(fxEdges, this))
                fxEdges.Add(new FxEdge(this, enclosingFx));
        }

        if (InputNodes.Count == 0)
        {
            if (SourceChannel.IsValid)
                results.Add(new CollectedSource(this, gain, fx));
            return;
        }

        for (var i = 0; i < InputNodes.Count; i++)
            InputNodes[i].Collect(results, gain, fxEdges, fx);
    }

    // A node reached via two paths keeps its first-seen enclosure (diamonds are rare; first wins).
    private static bool ContainsFx(List<FxEdge> fxEdges, AudioGraphNode fx)
    {
        for (var i = 0; i < fxEdges.Count; i++)
        {
            if (ReferenceEquals(fxEdges[i].Fx, fx))
                return true;
        }

        return false;
    }

    public override string ToString() => SourceLabel ?? _instance.Symbol.Name;

    private readonly Instance _instance;
    private readonly MultiInputSlot<AudioGraphNode>? _multiInput;
    private readonly HashSet<AudioHandle> _realisedSubmixes = new();
    private readonly List<CollectedSource> _analysisScratch = new();
    private int _lastUpdateFrame = -1;
}

#nullable enable
using System;
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

    /// <summary>Stamped by a routing bus each frame it collects this leaf. Lets a source op detect that it
    /// is graph-routed even without a direct wire (an auto-collected clip), so it hands its channel over.</summary>
    public int LastCollectedFrame = -100;

    /// <summary>Combinator only: declares an FX insert the routing bus realises as a nested submix carrying
    /// this node's collected sources. The wire stays BASS-agnostic — the declaring op applies and updates the
    /// actual effect through the callbacks. Null = no insert.</summary>
    public AudioFxInsert? FxInsert;

    /// <summary>Callbacks a routing bus invokes on the nested submix realising an <see cref="FxInsert"/>.
    /// All receive the submix channel handle. Created once per op; must not allocate per frame.</summary>
    public sealed class AudioFxInsert
    {
        /// <summary>Called once when the nested submix is created — set the effect on the channel.</summary>
        public required Action<int> Apply;

        /// <summary>Called every frame while realised — push current parameter values to the effect.</summary>
        public required Action<int> UpdateParams;

        /// <summary>Called when the submix is retired, before it is freed — drop cached per-submix state.</summary>
        public required Action<int> Remove;
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
    /// routing bus realises the sources below it as their own buffered submix. That submix is the only place an
    /// engine-owned channel (an [AudioClip], a video's audio) can be metered or analysed — those are routed
    /// un-buffered, so reading them individually silently returns nothing.
    /// </summary>
    public void DeclareAnalysisTap()
    {
        FxInsert = new AudioFxInsert
                       {
                           Apply = submix => _realisedSubmixes.Add(submix),
                           UpdateParams = _ => { },
                           Remove = submix => _realisedSubmixes.Remove(submix),
                       };
    }

    /// <summary>Submixes a routing bus realised for this node's insert; empty while the tap is only a side
    /// branch. More than one means several buses collected it.</summary>
    public IReadOnlyCollection<int> RealisedSubmixes => _realisedSubmixes;

    /// <summary>
    /// The channel a tap should read: the realised submix when wired inline (post source gains, works for every
    /// source type), else a single graph-owned leaf as the side-branch fallback. False when nothing is readable
    /// — including a side branch on engine-owned sources, which <see cref="HasExternallyManagedLeaf"/> detects
    /// so the op can say so rather than silently reporting zero.
    /// </summary>
    public bool TryGetAnalysisChannel(out int channel)
    {
        foreach (var submix in _realisedSubmixes)
        {
            channel = submix;
            return true;
        }

        CollectForAnalysis();
        if (_analysisScratch.Count == 1 && !_analysisScratch[0].Leaf.ExternallyManagedChannel)
        {
            channel = _analysisScratch[0].Leaf.SourceChannel;
            return channel != 0;
        }

        channel = 0;
        return false;
    }

    /// <summary>True when a reachable leaf's channel belongs to another subsystem, so a side-branch tap on it
    /// reads as silence. Drives the misconfiguration warning on tap operators.</summary>
    public bool HasExternallyManagedLeaf()
    {
        CollectForAnalysis();
        for (var i = 0; i < _analysisScratch.Count; i++)
        {
            if (_analysisScratch[i].Leaf.ExternallyManagedChannel)
                return true;
        }

        return false;
    }

    private void CollectForAnalysis()
    {
        _analysisScratch.Clear();
        Collect(_analysisScratch, 1f);
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
            if (SourceChannel != 0)
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
    private readonly HashSet<int> _realisedSubmixes = new();
    private readonly List<CollectedSource> _analysisScratch = new();
    private int _lastUpdateFrame = -1;
}

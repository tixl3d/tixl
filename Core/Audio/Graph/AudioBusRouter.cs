#nullable enable
using System.Collections.Generic;
using T3.Core.Animation;
using T3.Core.Audio.Native;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;

namespace T3.Core.Audio.Graph;

/// <summary>
/// Plays a set of audio-graph nodes as one bus: a native bus under the operator bus that holds every collected
/// source, with each insert realised as a nested bus. Routing is reconciled live as the graph changes. Drives
/// the [AudioBus] operator and the editor's output-window audio preview.
/// </summary>
/// <remarks>
/// Call <see cref="BeginFrame"/>, then <see cref="Add"/> for each root node, then <see cref="Route"/> — once
/// per frame, on the main thread.
/// </remarks>
public sealed class AudioBusRouter
{
    /// <param name="yieldsToOtherBuses">
    /// True for a preview: it leaves alone every source that another bus currently plays. A source can only be
    /// routed to one bus, so two buses that both claim a source would steal it from each other every frame.
    /// </param>
    /// <param name="logSource">The operator warnings are attributed to; null for the editor preview.</param>
    public AudioBusRouter(bool yieldsToOtherBuses, Instance? logSource)
    {
        _yieldsToOtherBuses = yieldsToOtherBuses;
        _logSource = logSource;
    }

    /// <summary>Sources collected in the current frame, with their effective gain.</summary>
    public IReadOnlyList<AudioGraphNode.CollectedSource> Collected => _collected;

    /// <summary>Peak level of the bus (post source gains, post bus volume), measured in <see cref="Route"/>.</summary>
    public float Level { get; private set; }

    /// <summary>Starts a frame's collection. Returns false while no bus can be created (audio not initialised).</summary>
    public bool BeginFrame()
    {
        _collected.Clear();
        _fxEdges.Clear();

        EnsureBus();
        if (!_bus.IsValid)
            return false;

        // Heartbeat: if this bus stops being driven, the engine stops it (instead of leaving it
        // playing a frozen last state that ignores upstream parameter changes).
        AudioBusRegistry.MarkAlive(_bus);
        return true;
    }

    /// <summary>Collects the sources reachable from <paramref name="node"/> into this frame's bus.</summary>
    public void Add(AudioGraphNode? node)
    {
        node?.Collect(_collected, 1f, _fxEdges);
    }

    /// <summary>
    /// Routes this frame's collected sources into the bus and applies <paramref name="volume"/>. Returns true
    /// when the routing changed.
    /// </summary>
    /// <param name="transportStopped">Stops the bus, which also stops pulling its sources.</param>
    public bool Route(float volume, bool transportStopped)
    {
        var engine = _engine!;

        // Realise the insert nesting as a chain of nested buses. Edges arrive parent-before-child, so
        // an enclosing group's bus exists before its inner groups need it as their parent.
        for (var i = 0; i < _fxEdges.Count; i++)
        {
            var edge = _fxEdges[i];
            var parent = _bus;
            if (edge.Parent != null && _fxGroups.TryGetValue(edge.Parent, out var parentGroup))
                parent = parentGroup.Bus;

            var group = EnsureFxGroup(engine, edge.Fx, parent);
            if (group != null)
                group.LastAliveFrame = Playback.FrameCount;
        }

        // Resolve each source's target: its nearest insert's bus, or this bus when routed dry.
        _desiredTargets.Clear();
        for (var i = 0; i < _collected.Count; i++)
        {
            var src = _collected[i];
            if (src.Leaf.Routing == AudioGraphNode.RoutingKind.Direct)
                continue; // spatial sources bypass the bus

            var source = src.Leaf.SourceChannel;
            if (!source.IsValid)
                continue;

            if (_yieldsToOtherBuses && IsPlayedByAnotherBus(engine, src.Leaf, source))
                continue;

            var target = _bus;
            if (src.FxNode != null && _fxGroups.TryGetValue(src.FxNode, out var group))
                target = group.Bus;

            _desiredTargets[source] = target;
            src.Leaf.LastCollectedFrame = Playback.FrameCount;
            engine.SetVolume(source, src.Gain);
        }

        RefreshAndRetireFxGroups(engine);

        var changed = false;
        foreach (var (source, target) in _desiredTargets)
        {
            // Trust the engine's routing, not our bookkeeping: another subsystem may have moved the source since.
            if (_routedTargets.TryGetValue(source, out var current) && current == target && engine.GetRouteTarget(source) == target)
                continue;

            if (engine.Route(source, target))
            {
                _routedTargets[source] = target;
                changed = true;
            }
            else
            {
                // A source whose owner released it this frame, or a full route queue: both resolve by the next frame.
                Log.Gated.Audio($"[AudioBus] could not route source {source.Value}");
                _routedTargets.Remove(source);
            }
        }

        _toRemove.Clear();
        foreach (var source in _routedTargets.Keys)
        {
            if (!_desiredTargets.ContainsKey(source))
                _toRemove.Add(source);
        }

        for (var i = 0; i < _toRemove.Count; i++)
        {
            ReleaseSource(engine, _toRemove[i]);
            changed = true;
        }

        engine.SetBusVolume(_bus, volume);
        engine.SetBusPlaying(_bus, !transportStopped);

        Level = System.Math.Min(engine.GetLevel(_bus), 1f);
        return changed;
    }

    /// <summary>Clears every insert's internal state, so a tail still ringing from live playback can't leak into an export.</summary>
    public void ResetEffects()
    {
        // No insert carries state yet: the effect operators pass their audio through.
    }

    /// <summary>Releases the buses without calling back into operators, so it is safe from a finalizer.</summary>
    public void FreeHandles()
    {
        if (_engine == null)
            return;

        foreach (var group in _fxGroups.Values)
            _engine.ReleaseDeferred(group.Bus);

        _fxGroups.Clear();

        for (var i = 0; i < _pendingFrees.Count; i++)
            _engine.ReleaseDeferred(_pendingFrees[i].Bus);

        _pendingFrees.Clear();
        _routedTargets.Clear();

        if (!_bus.IsValid)
            return;

        AudioBusRegistry.Unregister(_bus);
        _engine.ReleaseDeferred(_bus);
        _bus = default;
    }

    // One nested bus per insert-declaring node currently flowing into this bus. Parent is the bus of the
    // enclosing group (or this bus) — chained inserts nest.
    private sealed class FxGroup
    {
        public AudioHandle Bus;
        public AudioHandle Parent;
        public int LastAliveFrame;
    }

    // The other bus stamps the leaf when it collects it; the source then sits in one of that bus's groups.
    private bool IsPlayedByAnotherBus(NativeAudioEngine engine, AudioGraphNode leaf, AudioHandle source)
    {
        if (Playback.FrameCount - leaf.LastCollectedFrame > CollectedFrameSlack)
            return false;

        var target = engine.GetRouteTarget(source);
        return target.IsValid && !IsOwnBus(target);
    }

    private bool IsOwnBus(AudioHandle bus)
    {
        if (bus == _bus)
            return true;

        foreach (var group in _fxGroups.Values)
        {
            if (group.Bus == bus)
                return true;
        }

        return false;
    }

    // Only unroute the source if it is still where this bus put it — another bus may have taken it over since.
    private void ReleaseSource(NativeAudioEngine engine, AudioHandle source)
    {
        if (_routedTargets.TryGetValue(source, out var target) && engine.GetRouteTarget(source) == target)
            engine.Route(source, default);

        _routedTargets.Remove(source);
    }

    private FxGroup? EnsureFxGroup(NativeAudioEngine engine, AudioGraphNode fxNode, AudioHandle parent)
    {
        if (_fxGroups.TryGetValue(fxNode, out var group))
        {
            // Rewiring can change what encloses this insert — move the bus to its new parent.
            if (group.Parent != parent && engine.Route(group.Bus, parent))
                group.Parent = parent;

            return group;
        }

        var bus = engine.CreateBus();
        if (!bus.IsValid)
            return null;

        if (!engine.Route(bus, parent))
        {
            LogWarning("[AudioBus] failed to route an insert into its bus");
            engine.DestroyBus(bus);
            return null;
        }

        group = new FxGroup { Bus = bus, Parent = parent, LastAliveFrame = Playback.FrameCount };
        _fxGroups.Add(fxNode, group);
        fxNode.FxInsert?.Apply(bus);
        return group;
    }

    private void RefreshAndRetireFxGroups(NativeAudioEngine engine)
    {
        _fxGroupsToRetire.Clear();
        foreach (var (fxNode, group) in _fxGroups)
        {
            if (group.LastAliveFrame == Playback.FrameCount)
            {
                fxNode.FxInsert?.UpdateParams(group.Bus);
            }
            else
            {
                _fxGroupsToRetire.Add(fxNode);
            }
        }

        for (var i = 0; i < _fxGroupsToRetire.Count; i++)
        {
            var fxNode = _fxGroupsToRetire[i];
            var group = _fxGroups[fxNode];
            _fxGroups.Remove(fxNode);
            fxNode.FxInsert?.Remove(group.Bus);

            // Its sources move out later in this frame, so freeing it now would silence them for a moment.
            _pendingFrees.Add((group.Bus, Playback.RunTimeInSecs + RetiredBusLifetimeSecs));
        }

        for (var i = _pendingFrees.Count - 1; i >= 0; i--)
        {
            if (Playback.RunTimeInSecs < _pendingFrees[i].FreeAfter)
                continue;

            engine.DestroyBus(_pendingFrees[i].Bus);
            _pendingFrees.RemoveAt(i);
        }
    }

    private void EnsureBus()
    {
        if (_bus.IsValid)
            return;

        AudioMixerManager.Initialize();
        _engine = AudioMixerManager.NativeEngine;
        if (_engine == null)
            return;

        var bus = _engine.CreateBus();
        if (!bus.IsValid)
            return;

        if (!_engine.Route(bus, AudioMixerManager.OperatorBus))
        {
            Log.Error("[AudioBus] failed to route the bus into the operator bus", _logSource);
            _engine.DestroyBus(bus);
            return;
        }

        _bus = bus;
    }

    private void LogWarning(string message)
    {
        if (_logSource != null)
        {
            Log.Warning(message, _logSource);
        }
        else
        {
            Log.Warning(message);
        }
    }

    private const double RetiredBusLifetimeSecs = 0.1;

    /** The other bus stamps during its own evaluation, which may run after this router in a frame. */
    private const int CollectedFrameSlack = 2;

    private readonly bool _yieldsToOtherBuses;
    private readonly Instance? _logSource;
    private readonly List<AudioGraphNode.CollectedSource> _collected = new();
    private readonly List<AudioGraphNode.FxEdge> _fxEdges = new();
    private readonly Dictionary<AudioHandle, AudioHandle> _desiredTargets = new();
    private readonly Dictionary<AudioHandle, AudioHandle> _routedTargets = new();
    private readonly List<AudioHandle> _toRemove = new();
    private readonly Dictionary<AudioGraphNode, FxGroup> _fxGroups = new();
    private readonly List<AudioGraphNode> _fxGroupsToRetire = new();
    private readonly List<(AudioHandle Bus, double FreeAfter)> _pendingFrees = new();
    private NativeAudioEngine? _engine;
    private AudioHandle _bus;
}

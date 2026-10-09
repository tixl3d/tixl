#nullable enable
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;

namespace T3.Core.Audio.Graph;

/// <summary>
/// Plays a set of audio-graph nodes as one bus: a submix under the operator mixer that holds every collected
/// source channel, with each FX insert realised as a nested submix. Routing is reconciled live as the graph
/// changes. Drives the [AudioBus] operator and the editor's output-window audio preview.
/// </summary>
/// <remarks>
/// Call <see cref="BeginFrame"/>, then <see cref="Add"/> for each root node, then <see cref="Route"/> — once
/// per frame, on the main thread.
/// </remarks>
public sealed class AudioBusRouter
{
    /// <param name="yieldsToOtherBuses">
    /// True for a preview: it leaves alone every source that another bus currently plays. A BASS channel can
    /// only sit in one mixer, so two buses that both claim a source would steal it from each other every frame.
    /// </param>
    /// <param name="logSource">The operator warnings are attributed to; null for the editor preview.</param>
    public AudioBusRouter(bool yieldsToOtherBuses, Instance? logSource)
    {
        _yieldsToOtherBuses = yieldsToOtherBuses;
        _logSource = logSource;
    }

    /// <summary>Sources collected in the current frame, with their effective gain.</summary>
    public IReadOnlyList<AudioGraphNode.CollectedSource> Collected => _collected;

    /// <summary>Peak level of the bus submix (post source gains, pre bus volume), measured in <see cref="Route"/>.</summary>
    public float Level { get; private set; }

    /// <summary>Starts a frame's collection. Returns false while no submix can be created (audio not initialised).</summary>
    public bool BeginFrame()
    {
        // A device change tears down BASS entirely — every cached mixer handle is dead. Drop all routing
        // state so the submix and FX groups rebuild from scratch (inserts re-Apply on the new submixes);
        // without this the reconciler retries dead handles every frame.
        if (_mixerGeneration != AudioMixerManager.ResetGeneration)
        {
            _mixerGeneration = AudioMixerManager.ResetGeneration;
            if (_submix != 0)
            {
                AudioBusRegistry.Unregister(_submix);
                _submix = 0;
            }

            foreach (var (fxNode, group) in _fxGroups)
                fxNode.FxInsert?.Remove(group.Submix);

            _fxGroups.Clear();
            _routedTargets.Clear();
            _pendingFrees.Clear();
        }

        _collected.Clear();
        _fxEdges.Clear();

        EnsureSubmix();
        if (_submix == 0)
            return false;

        // Heartbeat: if this bus stops being driven, the engine pauses its submix (instead of leaving it
        // playing a frozen last state that ignores upstream parameter changes).
        AudioBusRegistry.MarkAlive(_submix);
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
    /// <param name="transportStopped">Pauses the submix, which also stops pulling generator streams.</param>
    public bool Route(float volume, bool transportStopped)
    {
        // Realise the FX nesting as a chain of nested submixes. Edges arrive parent-before-child, so
        // an enclosing FX group's submix exists before its inner groups need it as their parent.
        for (var i = 0; i < _fxEdges.Count; i++)
        {
            var edge = _fxEdges[i];
            var parentMixer = _submix;
            if (edge.Parent != null && _fxGroups.TryGetValue(edge.Parent, out var parentGroup))
                parentMixer = parentGroup.Submix;

            var group = EnsureFxGroup(edge.Fx, parentMixer);
            if (group != null)
                group.LastAliveFrame = Playback.FrameCount;
        }

        // Resolve each source's target mixer: its nearest FX group's submix, or the bus when routed dry.
        _desiredTargets.Clear();
        _externallyManaged.Clear();
        for (var i = 0; i < _collected.Count; i++)
        {
            var src = _collected[i];
            if (src.Leaf.Routing == AudioGraphNode.RoutingKind.Direct)
                continue; // spatial/HW-3D bypasses the bus

            var ch = src.Leaf.SourceChannel;
            if (ch == 0)
                continue;

            if (_yieldsToOtherBuses && IsPlayedByAnotherBus(src.Leaf, ch))
                continue;

            var target = _submix;
            if (src.FxNode != null && _fxGroups.TryGetValue(src.FxNode, out var group))
                target = group.Submix;

            _desiredTargets[ch] = target;
            if (src.Leaf.ExternallyManagedChannel)
                _externallyManaged.Add(ch);
            src.Leaf.LastCollectedFrame = Playback.FrameCount;
            Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, src.Gain);
        }

        // Push current FX parameters and retire groups whose FX node vanished from the collection —
        // with a short gain fade so an effect tail isn't truncated.
        RefreshAndRetireFxGroups();

        var changed = false;
        foreach (var (ch, target) in _desiredTargets)
        {
            // Trust BASS, not our bookkeeping: another subsystem (engine reclaim, export) may have moved
            // the channel elsewhere since we routed it — re-add whenever it isn't actually in the target.
            if (_routedTargets.TryGetValue(ch, out var current) && current == target
                && BassMix.ChannelGetMixer(ch) == target)
                continue;

            // A BASS channel can only be in one mixer — pull it out of wherever it currently sits
            // (the engine's SoundtrackMixer for clip channels, or another bus's submix after a
            // pin/rewire handoff) before adding. Clip channels are added un-buffered: MixerChanBuffer
            // latency would break the engine's per-frame seek/resync of the clip position.
            var externallyManaged = _externallyManaged.Contains(ch);
            if (BassMix.ChannelGetMixer(ch) != 0)
                BassMix.MixerRemoveChannel(ch);

            var flags = externallyManaged ? BassFlags.Default : BassFlags.MixerChanBuffer;
            if (BassMix.MixerAddChannel(target, ch, flags))
            {
                _routedTargets[ch] = target;
                changed = true;
            }
            else
            {
                LogWarning($"[AudioBus] failed to route channel {ch}: {Bass.LastError}");
                _routedTargets.Remove(ch);
            }
        }

        _toRemove.Clear();
        foreach (var ch in _routedTargets.Keys)
            if (!_desiredTargets.ContainsKey(ch))
                _toRemove.Add(ch);

        for (var i = 0; i < _toRemove.Count; i++)
        {
            ReleaseChannel(_toRemove[i]);
            changed = true;
        }

        Bass.ChannelSetAttribute(_submix, ChannelAttribute.Volume, volume);
        BassMix.ChannelFlags(_submix, transportStopped ? BassFlags.MixerChanPause : 0, BassFlags.MixerChanPause);

        MeasureLevel();
        return changed;
    }

    /// <summary>Clears every effect's internal state, so a tail still ringing from live playback can't leak into an export.</summary>
    public void ResetEffects()
    {
        foreach (var (_, group) in _fxGroups)
        {
            if (group.Submix != 0)
                Bass.FXReset(group.Submix);
        }

        if (_submix != 0)
            Bass.FXReset(_submix);
    }

    /// <summary>Frees the BASS handles without calling back into operators, so it is safe from a finalizer.</summary>
    public void FreeHandles()
    {
        foreach (var group in _fxGroups.Values)
            Bass.StreamFree(group.Submix);

        _fxGroups.Clear();

        for (var i = 0; i < _pendingFrees.Count; i++)
            Bass.StreamFree(_pendingFrees[i].Submix);

        _pendingFrees.Clear();
        _routedTargets.Clear();

        if (_submix == 0)
            return;

        AudioBusRegistry.Unregister(_submix);
        Bass.StreamFree(_submix);
        _submix = 0;
    }

    // One nested submix per FX-declaring node currently flowing into this bus. ParentMixer is the
    // submix of the enclosing FX group (or the bus submix) — chained inserts nest.
    private sealed class FxGroup
    {
        public int Submix;
        public int ParentMixer;
        public int LastAliveFrame;
    }

    // The other bus stamps the leaf when it collects it; the channel then sits in one of that bus's mixers.
    private bool IsPlayedByAnotherBus(AudioGraphNode leaf, int channel)
    {
        if (Playback.FrameCount - leaf.LastCollectedFrame > CollectedFrameSlack)
            return false;

        var mixer = BassMix.ChannelGetMixer(channel);
        return mixer != 0 && !IsOwnMixer(mixer);
    }

    private bool IsOwnMixer(int mixer)
    {
        if (mixer == _submix)
            return true;

        foreach (var group in _fxGroups.Values)
        {
            if (group.Submix == mixer)
                return true;
        }

        return false;
    }

    // Only pull the channel if it is still where this bus put it — another bus may have taken it over since.
    private void ReleaseChannel(int channel)
    {
        if (_routedTargets.TryGetValue(channel, out var target) && BassMix.ChannelGetMixer(channel) == target)
            BassMix.MixerRemoveChannel(channel);

        _routedTargets.Remove(channel);
    }

    // Single measurement point for the submix level. BassMix.ChannelGetLevel consumes the data window since
    // the last call, so a second reader in the same frame would read ~0. Uses the buffer-inspecting variant:
    // the device pulls the mixer chain in coarse chunks, so the plain per-frame call mostly reads 0.
    private void MeasureLevel()
    {
        if (_lastLevelFrame == Playback.FrameCount)
            return;

        _lastLevelFrame = Playback.FrameCount;

        if (BassMix.ChannelGetLevel(_submix, _levelPair, 0.05f, 0) == -1)
            return;

        Level = System.Math.Min(System.Math.Max(_levelPair[0], _levelPair[1]), 1f);
    }

    private FxGroup? EnsureFxGroup(AudioGraphNode fxNode, int parentMixer)
    {
        if (_fxGroups.TryGetValue(fxNode, out var group))
        {
            // Rewiring can change what encloses this insert — move the submix to its new parent.
            if (group.ParentMixer != parentMixer)
            {
                BassMix.MixerRemoveChannel(group.Submix);
                if (BassMix.MixerAddChannel(parentMixer, group.Submix, BassFlags.MixerChanBuffer))
                    group.ParentMixer = parentMixer;
                else
                    LogWarning($"[AudioBus] failed to re-parent FX submix: {Bass.LastError}");
            }

            return group;
        }

        var submix = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
        if (submix == 0)
        {
            LogWarning($"[AudioBus] failed to create FX submix: {Bass.LastError}");
            return null;
        }

        if (!BassMix.MixerAddChannel(parentMixer, submix, BassFlags.MixerChanBuffer))
        {
            LogWarning($"[AudioBus] failed to add FX submix to bus: {Bass.LastError}");
            Bass.StreamFree(submix);
            return null;
        }

        group = new FxGroup { Submix = submix, ParentMixer = parentMixer, LastAliveFrame = Playback.FrameCount };
        _fxGroups.Add(fxNode, group);
        fxNode.FxInsert?.Apply(submix);
        return group;
    }

    private void RefreshAndRetireFxGroups()
    {
        _fxGroupsToRetire.Clear();
        foreach (var (fxNode, group) in _fxGroups)
        {
            if (group.LastAliveFrame == Playback.FrameCount)
            {
                fxNode.FxInsert?.UpdateParams(group.Submix);
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
            fxNode.FxInsert?.Remove(group.Submix);

            // Fade instead of truncating so a reverb/echo tail rings out before the submix is freed.
            Bass.ChannelSlideAttribute(group.Submix, ChannelAttribute.Volume, 0f, FxRetireFadeMs);
            _pendingFrees.Add((group.Submix, Playback.RunTimeInSecs + FxRetireFadeMs / 1000.0 + 0.1));
        }

        for (var i = _pendingFrees.Count - 1; i >= 0; i--)
        {
            if (Playback.RunTimeInSecs < _pendingFrees[i].FreeAfter)
                continue;

            BassMix.MixerRemoveChannel(_pendingFrees[i].Submix);
            Bass.StreamFree(_pendingFrees[i].Submix);
            _pendingFrees.RemoveAt(i);
        }
    }

    private void EnsureSubmix()
    {
        if (_submix != 0)
            return;

        if (!AudioMixerManager.IsInitialized)
        {
            AudioMixerManager.Initialize();
            if (AudioMixerManager.OperatorMixerHandle == 0)
                return;
        }

        _submix = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
        if (_submix == 0)
        {
            LogWarning($"[AudioBus] failed to create bus submix: {Bass.LastError}");
            return;
        }

        if (!BassMix.MixerAddChannel(AudioMixerManager.OperatorMixerHandle, _submix, BassFlags.MixerChanBuffer))
            Log.Error($"[AudioBus] failed to add bus to operator mixer: {Bass.LastError}", _logSource);
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

    private const int FxRetireFadeMs = 400;

    /** The other bus stamps during its own evaluation, which may run after this router in a frame. */
    private const int CollectedFrameSlack = 2;

    private readonly bool _yieldsToOtherBuses;
    private readonly Instance? _logSource;
    private readonly List<AudioGraphNode.CollectedSource> _collected = new();
    private readonly List<AudioGraphNode.FxEdge> _fxEdges = new();
    private readonly Dictionary<int, int> _desiredTargets = new();  // channel → target mixer
    private readonly HashSet<int> _externallyManaged = new();
    private readonly Dictionary<int, int> _routedTargets = new();   // channel → mixer it sits in
    private readonly List<int> _toRemove = new();
    private readonly Dictionary<AudioGraphNode, FxGroup> _fxGroups = new();
    private readonly List<AudioGraphNode> _fxGroupsToRetire = new();
    private readonly List<(int Submix, double FreeAfter)> _pendingFrees = new();
    private readonly float[] _levelPair = new float[2];
    private int _submix;
    private int _mixerGeneration;
    private int _lastLevelFrame = -1;
}

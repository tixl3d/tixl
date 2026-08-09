#nullable enable
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Plays "loose" audio-graph sources — ops implementing <see cref="IAudioSource"/> whose output isn't wired
/// into any bus/combinator — through an implicit default bus, so basic audio plays with no [AudioBus] op.
/// Sources wired into an [AudioBus]/[CombineAudio] are left to that explicit path (they get routed there).
/// Runs per-frame from the editor playback update, like <see cref="AudioClipCollector"/>. Loose sources play
/// at <b>static</b> input values (no animation) — wire them into the graph for animated params.
/// </summary>
public static class AudioGraphCollector
{
    public static void CollectLooseSources(Instance? composition)
    {
        if (composition == null)
            return;

        // A device change frees every BASS handle, so the cached bus and routing set are dead ints. Without
        // this the non-zero _defaultBus is reused forever and loose graph audio stays silent until restart.
        if (_resetGeneration != AudioMixerManager.ResetGeneration)
        {
            _resetGeneration = AudioMixerManager.ResetGeneration;
            _defaultBus = 0;
            _routed.Clear();
        }

        RebuildLooseListIfChanged(composition);

        if (_looseSources.Count == 0 && _routed.Count == 0)
            return;

        if (!EnsureDefaultBus())
            return;

        // Transport gating: loose sources follow the transport too — stopped playback means silence,
        // giving stop=quiet as the natural off-switch for implicitly playing audio.
        // Render-export steps time with PlaybackSpeed 0, so recording counts as running.
        var transportStopped = (Playback.Current?.PlaybackSpeed ?? 0) == 0 && !AudioRendering.IsRecording;
        BassMix.ChannelFlags(_defaultBus, transportStopped ? BassFlags.MixerChanPause : 0, BassFlags.MixerChanPause);

        // Ensure each loose source's channel (from static inputs) and build the desired set.
        _desired.Clear();
        for (var i = 0; i < _looseSources.Count; i++)
        {
            var src = _looseSources[i];
            src.EnsureChannelFromStaticInputs();

            var node = src.AudioReferenceOutput.Value;
            if (node == null || node.SourceChannel == 0 || node.Routing == AudioGraphNode.RoutingKind.Direct)
                continue;

            // An auto-collecting [AudioBus] / [CombineAudio] stamps every leaf it takes. Leaving those to the
            // implicit bus as well would have the two adding the same channel to different mixers every frame,
            // stealing it back and forth. An explicit collector wins.
            if (Playback.FrameCount - node.LastCollectedFrame <= CollectedFrameSlack)
                continue;

            _desired.Add(node.SourceChannel);
            Bass.ChannelSetAttribute(node.SourceChannel, ChannelAttribute.Volume, node.Gain);
        }

        // Route newly-loose channels into the default bus.
        foreach (var ch in _desired)
        {
            if (_routed.Contains(ch))
                continue;

            if (BassMix.MixerAddChannel(_defaultBus, ch, BassFlags.MixerChanBuffer))
                _routed.Add(ch);
        }

        // Drop channels that are no longer loose (now wired to a bus, or their source is gone).
        _toRemove.Clear();
        foreach (var ch in _routed)
            if (!_desired.Contains(ch))
                _toRemove.Add(ch);

        for (var i = 0; i < _toRemove.Count; i++)
        {
            BassMix.MixerRemoveChannel(_toRemove[i]);
            _routed.Remove(_toRemove[i]);
        }
    }

    // Rebuild the loose-source list only when the composition or its connection structure changes.
    private static void RebuildLooseListIfChanged(Instance composition)
    {
        if (ReferenceEquals(_cachedComposition, composition) && _cachedStructureVersion == composition.Symbol.VersionCounter)
            return;

        _cachedComposition = composition;
        _cachedStructureVersion = composition.Symbol.VersionCounter;
        _looseSources.Clear();

        var connections = composition.Symbol.Connections;
        foreach (var child in composition.Children.Values)
        {
            if (child is not IAudioSource src)
                continue;

            var outputId = src.AudioReferenceOutput.Id;
            var childId = child.SymbolChildId;

            var wired = false;
            for (var i = 0; i < connections.Count; i++)
            {
                var c = connections[i];
                if (c.SourceParentOrChildId == childId && c.SourceSlotId == outputId)
                {
                    wired = true;
                    break;
                }
            }

            if (!wired)
                _looseSources.Add(src);
        }
    }

    private static bool EnsureDefaultBus()
    {
        if (_defaultBus != 0)
            return true;

        if (!AudioMixerManager.IsInitialized)
        {
            AudioMixerManager.Initialize();
            if (AudioMixerManager.OperatorMixerHandle == 0)
                return false;
        }

        _defaultBus = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
        if (_defaultBus == 0)
        {
            Log.Warning($"[AudioGraphCollector] failed to create default bus: {Bass.LastError}");
            return false;
        }

        if (!BassMix.MixerAddChannel(AudioMixerManager.OperatorMixerHandle, _defaultBus, BassFlags.MixerChanBuffer))
            Log.Error($"[AudioGraphCollector] failed to add default bus to operator mixer: {Bass.LastError}");

        return true;
    }

    // The bus stamps during its own evaluation, which may run after this collector in a frame.
    private const int CollectedFrameSlack = 2;

    private static Instance? _cachedComposition;
    private static int _cachedStructureVersion = -1;
    private static readonly List<IAudioSource> _looseSources = new();
    private static readonly HashSet<int> _desired = new();
    private static readonly HashSet<int> _routed = new();
    private static readonly List<int> _toRemove = new();
    private static int _defaultBus;
    private static int _resetGeneration = -1;
}

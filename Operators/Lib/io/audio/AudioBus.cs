#nullable enable
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.Audio;

namespace Lib.io.audio
{
    /// <summary>
    /// Output of the audio processing graph. Collects the audio sources wired into it (recursively, through
    /// [CombineAudio]s), routes each collected source's channel into a bus (submix) under the operator mixer —
    /// reconciling add/remove live as the graph changes — and applies a master <see cref="Volume"/>.
    /// <c>Direct</c>-routed sources (spatial HW-3D) bypass the bus. Wire Result into your render command chain
    /// so it is evaluated each frame.
    /// </summary>
    [Guid("b7e0d240-1e42-4c8a-9f31-0ab1cd2e0100")]
    internal sealed class AudioBus : Instance<AudioBus>
    {
        [Output(Guid = "b7e0d240-0001-4c8a-9f31-0ab1cd2e0100", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
        public readonly Slot<Command> Result = new();

        [Output(Guid = "b7e0d240-0004-4c8a-9f31-0ab1cd2e0100", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
        public readonly Slot<float> Level = new();

        public AudioBus()
        {
            Result.UpdateAction += Update;
            Level.UpdateAction += UpdateLevel;
        }

        // Meters the whole submix (post source gains, pre master Volume) — "react to what you hear" for this
        // bus. Only meaningful while the bus is evaluated; a paused/stale submix reports its last level or 0.
        private void UpdateLevel(EvaluationContext context)
        {
            if (_submix == 0)
            {
                Level.Value = 0;
                return;
            }

            var levelData = BassMix.ChannelGetLevel(_submix);
            if (levelData == -1)
                return;

            var left = (levelData & 0xFFFF) / 32768f;
            var right = ((levelData >> 16) & 0xFFFF) / 32768f;
            Level.Value = Math.Min(Math.Max(left, right), 1f);
        }

        private void Update(EvaluationContext context)
        {
            EnsureSubmix();
            if (_submix == 0)
                return;

            // Heartbeat: if this bus stops being evaluated, the engine pauses its submix (instead of
            // leaving it playing a frozen last state that ignores upstream parameter changes).
            AudioBusRegistry.MarkAlive(_submix);

            _collected.Clear();
            var inputs = Input.GetCollectedTypedInputs(true);
            for (var i = 0; i < inputs.Count; i++)
                inputs[i]?.GetValue(context)?.Collect(_collected, 1f);
            Input.DirtyFlag.Clear();

            // Realise FX-declaring nodes as nested submixes under the bus and resolve each source's target
            // mixer (its FX group's submix, or the bus itself when routed dry).
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

                var target = _submix;
                if (src.FxNode != null)
                {
                    var group = EnsureFxGroup(src.FxNode);
                    if (group != null)
                    {
                        group.LastAliveFrame = Playback.FrameCount;
                        target = group.Submix;
                    }
                }

                _desiredTargets[ch] = target;
                if (src.Leaf.ExternallyManagedChannel)
                    _externallyManaged.Add(ch);
                Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, src.Gain);
            }

            // Push current FX parameters and retire groups whose FX node vanished from the collection —
            // with a short gain fade so an effect tail isn't truncated.
            RefreshAndRetireFxGroups();

            var changed = false;
            foreach (var (ch, target) in _desiredTargets)
            {
                if (_routedTargets.TryGetValue(ch, out var current) && current == target)
                    continue;

                // A clip channel lives in the SoundtrackMixer (engine-owned) — a BASS channel can only be in one
                // mixer, so pull it out first, and add it un-buffered: MixerChanBuffer latency would break the
                // engine's per-frame seek/resync of the clip position.
                var externallyManaged = _externallyManaged.Contains(ch);
                if (externallyManaged || _routedTargets.ContainsKey(ch))
                    BassMix.MixerRemoveChannel(ch);

                var flags = externallyManaged ? BassFlags.Default : BassFlags.MixerChanBuffer;
                if (BassMix.MixerAddChannel(target, ch, flags))
                {
                    _routedTargets[ch] = target;
                    changed = true;
                }
                else
                {
                    _routedTargets.Remove(ch);
                }
            }

            _toRemove.Clear();
            foreach (var ch in _routedTargets.Keys)
                if (!_desiredTargets.ContainsKey(ch))
                    _toRemove.Add(ch);

            for (var i = 0; i < _toRemove.Count; i++)
            {
                BassMix.MixerRemoveChannel(_toRemove[i]);
                _routedTargets.Remove(_toRemove[i]);
                changed = true;
            }

            Bass.ChannelSetAttribute(_submix, ChannelAttribute.Volume, Volume.GetValue(context));

            // Transport gating: graph audio follows the transport. While stopped (PlaybackSpeed == 0) the
            // submix pauses — which also stops pulling generator streams — matching soundtrack-clip behaviour.
            var transportStopped = context.Playback.PlaybackSpeed == 0;
            BassMix.ChannelFlags(_submix, transportStopped ? BassFlags.MixerChanPause : 0, BassFlags.MixerChanPause);

            if (!changed)
                return;

            var labels = string.Join(", ", _collected.ConvertAll(c => c.FxNode == null ? $"{c.Leaf}×{c.Gain:0.00}" : $"{c.Leaf}×{c.Gain:0.00}→{c.FxNode}"));
            Log.Debug($"[AudioBus] routing {_routedTargets.Count} channel(s): {labels}", this);
        }

        // One nested submix per FX-declaring node currently flowing into this bus.
        private sealed class FxGroup
        {
            public int Submix;
            public int LastAliveFrame;
        }

        private FxGroup? EnsureFxGroup(AudioGraphNode fxNode)
        {
            if (_fxGroups.TryGetValue(fxNode, out var group))
                return group;

            var submix = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
            if (submix == 0)
            {
                Log.Warning($"[AudioBus] failed to create FX submix: {Bass.LastError}", this);
                return null;
            }

            if (!BassMix.MixerAddChannel(_submix, submix, BassFlags.MixerChanBuffer))
            {
                Log.Error($"[AudioBus] failed to add FX submix to bus: {Bass.LastError}", this);
                Bass.StreamFree(submix);
                return null;
            }

            group = new FxGroup { Submix = submix, LastAliveFrame = Playback.FrameCount };
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
                Log.Warning($"[AudioBus] failed to create bus submix: {Bass.LastError}", this);
                return;
            }

            if (!BassMix.MixerAddChannel(AudioMixerManager.OperatorMixerHandle, _submix, BassFlags.MixerChanBuffer))
                Log.Error($"[AudioBus] failed to add bus to operator mixer: {Bass.LastError}", this);
        }

        ~AudioBus()
        {
            foreach (var group in _fxGroups.Values)
                Bass.StreamFree(group.Submix);

            for (var i = 0; i < _pendingFrees.Count; i++)
                Bass.StreamFree(_pendingFrees[i].Submix);

            if (_submix != 0)
            {
                AudioBusRegistry.Unregister(_submix);
                Bass.StreamFree(_submix);
                _submix = 0;
            }
        }

        private const int FxRetireFadeMs = 400;

        private readonly List<AudioGraphNode.CollectedSource> _collected = new();
        private readonly Dictionary<int, int> _desiredTargets = new();  // channel → target mixer
        private readonly HashSet<int> _externallyManaged = new();
        private readonly Dictionary<int, int> _routedTargets = new();   // channel → mixer it sits in
        private readonly List<int> _toRemove = new();
        private readonly Dictionary<AudioGraphNode, FxGroup> _fxGroups = new();
        private readonly List<AudioGraphNode> _fxGroupsToRetire = new();
        private readonly List<(int Submix, double FreeAfter)> _pendingFrees = new();
        private int _submix;

        [Input(Guid = "b7e0d240-0002-4c8a-9f31-0ab1cd2e0100")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "b7e0d240-0003-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<float> Volume = new();
    }
}

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
    internal sealed class AudioBus : Instance<AudioBus>, IAudioExportSource
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

        // Reports the level measured during Update. BassMix.ChannelGetLevel consumes the data window since
        // the last call, so the submix must be measured exactly once per frame — a second reader would
        // steal the window and read ~0. Only meaningful while the bus is evaluated.
        private void UpdateLevel(EvaluationContext context)
        {
            Level.Value = _measuredLevel;
        }

        // Single measurement point for the submix level (post source gains, pre master Volume) — see UpdateLevel.
        // Uses the buffer-inspecting Ex variant: the plain ChannelGetLevel only sees data taken since the last
        // call, and the device pulls the mixer chain in coarse chunks, so per-frame calls mostly read 0.
        private void MeasureLevel()
        {
            if (_lastLevelFrame == Playback.FrameCount)
                return;

            _lastLevelFrame = Playback.FrameCount;

            if (BassMix.ChannelGetLevel(_submix, _levelPair, 0.05f, 0) == -1)
                return;

            _measuredLevel = Math.Min(Math.Max(_levelPair[0], _levelPair[1]), 1f);
        }

        // Render-export only evaluates the exported op-chain — register so a bus that was live (e.g.
        // driven by a pinned view) keeps being evaluated per exported frame instead of going stale-silent.
        bool IAudioExportSource.IsActiveForExport => Playback.FrameCount - _lastEvaluationFrame <= 10;

        // Effects carry their own tail: a reverb or echo still ringing from live playback would fade out over
        // the first exported frames, and would differ between two renders of the same range. FXReset clears an
        // effect's internal state; passing the channel resets every effect on it, so the bus doesn't need to
        // know what the FX ops applied.
        void IAudioExportSource.ResetForExport()
        {
            foreach (var (_, group) in _fxGroups)
            {
                if (group.Submix != 0)
                    Bass.FXReset(group.Submix);
            }

            if (_submix != 0)
                Bass.FXReset(_submix);
        }

        private void Update(EvaluationContext context)
        {
            _lastEvaluationFrame = Playback.FrameCount;
            AudioExportSourceRegistry.Register(this);

            // A device change tears down BASS entirely — every cached mixer handle is dead. Drop all
            // routing state so the submix and FX groups rebuild from scratch (inserts re-Apply on the
            // new submixes); without this the reconciler retries dead handles every frame.
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

            EnsureSubmix();
            if (_submix == 0)
                return;

            // Heartbeat: if this bus stops being evaluated, the engine pauses its submix (instead of
            // leaving it playing a frozen last state that ignores upstream parameter changes).
            AudioBusRegistry.MarkAlive(_submix);

            _collected.Clear();
            _fxEdges.Clear();
            var inputs = Input.GetCollectedTypedInputs(true);
            for (var i = 0; i < inputs.Count; i++)
                inputs[i]?.GetValue(context)?.Collect(_collected, 1f, _fxEdges);
            Input.DirtyFlag.Clear();

            if (AutoCollectClips.GetValue(context))
                CollectLooseClips(context);

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
                    Log.Warning($"[AudioBus] failed to route channel {ch}: {Bass.LastError}", this);
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
            // Render-export steps time with PlaybackSpeed 0, so recording counts as running.
            var transportStopped = context.Playback.PlaybackSpeed == 0 && !AudioRendering.IsRecording;
            BassMix.ChannelFlags(_submix, transportStopped ? BassFlags.MixerChanPause : 0, BassFlags.MixerChanPause);

            MeasureLevel();

            if (!changed)
                return;

            var labels = string.Join(", ", _collected.ConvertAll(c => c.FxNode == null ? $"{c.Leaf}×{c.Gain:0.00}" : $"{c.Leaf}×{c.Gain:0.00}→{c.FxNode}"));
            Log.Debug($"[AudioBus] routing {_routedTargets.Count} channel(s): {labels}", this);
        }

        // Auto-collect: [AudioClip] siblings whose AudioReference isn't wired anywhere route through this
        // bus as if they were — evaluating their reference output doubles as the playback heartbeat.
        // Only one op per composition should auto-collect (two would contend for the same channels).
        private void CollectLooseClips(EvaluationContext context)
        {
            var looseOutputs = _looseClipScanner.GetLooseClipOutputs(Parent);
            for (var i = 0; i < looseOutputs.Count; i++)
                looseOutputs[i].GetValue(context)?.Collect(_collected, 1f, _fxEdges);
        }

        // One nested submix per FX-declaring node currently flowing into this bus. ParentMixer is the
        // submix of the enclosing FX group (or the bus submix) — chained inserts nest.
        private sealed class FxGroup
        {
            public int Submix;
            public int ParentMixer;
            public int LastAliveFrame;
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
                        Log.Warning($"[AudioBus] failed to re-parent FX submix: {Bass.LastError}", this);
                }

                return group;
            }

            var submix = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
            if (submix == 0)
            {
                Log.Warning($"[AudioBus] failed to create FX submix: {Bass.LastError}", this);
                return null;
            }

            if (!BassMix.MixerAddChannel(parentMixer, submix, BassFlags.MixerChanBuffer))
            {
                Log.Error($"[AudioBus] failed to add FX submix to bus: {Bass.LastError}", this);
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
                Log.Warning($"[AudioBus] failed to create bus submix: {Bass.LastError}", this);
                return;
            }

            if (!BassMix.MixerAddChannel(AudioMixerManager.OperatorMixerHandle, _submix, BassFlags.MixerChanBuffer))
                Log.Error($"[AudioBus] failed to add bus to operator mixer: {Bass.LastError}", this);
        }

        ~AudioBus()
        {
            AudioExportSourceRegistry.Unregister(this);

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
        private readonly LooseAudioClipScanner _looseClipScanner = new();
        private readonly Dictionary<AudioGraphNode, FxGroup> _fxGroups = new();
        private readonly List<AudioGraphNode.FxEdge> _fxEdges = new();
        private readonly List<AudioGraphNode> _fxGroupsToRetire = new();
        private readonly List<(int Submix, double FreeAfter)> _pendingFrees = new();
        private int _submix;
        private int _mixerGeneration;
        private int _lastEvaluationFrame = -100;
        private float _measuredLevel;
        private int _lastLevelFrame = -1;
        private readonly float[] _levelPair = new float[2];

        [Input(Guid = "b7e0d240-0002-4c8a-9f31-0ab1cd2e0100")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "b7e0d240-0003-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "b7e0d240-0005-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<bool> AutoCollectClips = new();
    }
}

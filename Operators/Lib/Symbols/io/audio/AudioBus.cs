#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Audio.Graph;

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
            _router = new AudioBusRouter(yieldsToOtherBuses: false, logSource: this);
        }

        // Reports the level measured during Update. BassMix.ChannelGetLevel consumes the data window since
        // the last call, so the submix is measured exactly once per frame, by the router. Only meaningful
        // while the bus is evaluated.
        private void UpdateLevel(EvaluationContext context)
        {
            Level.Value = _router.Level;
        }

        // Render-export only evaluates the exported op-chain — register so a bus that was live (e.g.
        // driven by a pinned view) keeps being evaluated per exported frame instead of going stale-silent.
        bool IAudioExportSource.IsActiveForExport => Playback.FrameCount - _lastEvaluationFrame <= 10;

        // Effects carry their own tail: a reverb or echo still ringing from live playback would fade out over
        // the first exported frames, and would differ between two renders of the same range.
        void IAudioExportSource.ResetForExport() => _router.ResetEffects();

        private void Update(EvaluationContext context)
        {
            _lastEvaluationFrame = Playback.FrameCount;
            AudioExportSourceRegistry.Register(this);

            if (!_router.BeginFrame())
                return;

            var inputs = Input.GetCollectedTypedInputs(true);
            for (var i = 0; i < inputs.Count; i++)
                _router.Add(inputs[i]?.GetValue(context));
            Input.DirtyFlag.Clear();

            if (AutoCollectClips.GetValue(context))
                CollectLooseClips(context);

            // Transport gating: graph audio follows the transport, matching soundtrack-clip behaviour.
            // Render-export steps time with PlaybackSpeed 0, so recording counts as running.
            var transportStopped = context.Playback.PlaybackSpeed == 0 && !AudioRendering.IsRecording;
            var changed = _router.Route(Volume.GetValue(context), transportStopped);

            if (!changed)
                return;

            if (!Log.Gated.AudioEnabled)
                return;

            var labels = string.Join(", ", _router.Collected.Select(c => c.FxNode == null ? $"{c.Leaf}×{c.Gain:0.00}" : $"{c.Leaf}×{c.Gain:0.00}→{c.FxNode}"));
            Log.Gated.Audio($"[AudioBus] routing {_router.Collected.Count} source(s): {labels}");
        }

        // Auto-collect: [AudioClip] siblings whose AudioReference isn't wired anywhere route through this
        // bus as if they were — evaluating their reference output doubles as the playback heartbeat.
        // Only one op per composition should auto-collect (two would contend for the same channels).
        private void CollectLooseClips(EvaluationContext context)
        {
            var looseOutputs = _looseClipScanner.GetLooseClipOutputs(Parent);
            for (var i = 0; i < looseOutputs.Count; i++)
                _router.Add(looseOutputs[i].GetValue(context));
        }

        ~AudioBus()
        {
            AudioExportSourceRegistry.Unregister(this);
            _router.FreeHandles();
        }

        private readonly AudioBusRouter _router;
        private readonly LooseAudioClipScanner _looseClipScanner = new();
        private int _lastEvaluationFrame = -100;

        [Input(Guid = "b7e0d240-0002-4c8a-9f31-0ab1cd2e0100")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "b7e0d240-0003-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "b7e0d240-0005-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<bool> AutoCollectClips = new();
    }
}

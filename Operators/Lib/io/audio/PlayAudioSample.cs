using T3.Core.Animation;
using T3.Core.Audio;
// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.audio
{
    [Guid("65e95f77-4743-437f-ab31-f34b831d28d7")]
    internal sealed class PlayAudioSample : Instance<PlayAudioSample>
    {
        [Input(Guid = "505139a0-71ce-4297-8440-5bf84488902e")]
        public readonly InputSlot<string> AudioFile = new();

        [Input(Guid = "726bc4d3-df8b-4abe-a38e-2e09cf44ca10")]
        public readonly InputSlot<bool> PlayAudio = new();

        [Input(Guid = "59b659c6-ca1f-4c2b-8dff-3a1da9abd352")]
        public readonly InputSlot<bool> StopAudio = new();

        [Input(Guid = "7e42f2a8-3c5d-4f6e-9b8a-1d2e3f4a5b6c")]
        public readonly InputSlot<bool> PauseAudio = new();

        [Input(Guid = "c0645e37-db4e-4658-9d65-96478851f6f6")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "1a3f4b7c-12d3-4a5b-9c7d-8e1f2a3b4c5d")]
        public readonly InputSlot<bool> Mute = new();

        [Input(Guid = "53d1622e-b1d5-4b1c-acd0-ebceb7064043")]
        public readonly InputSlot<float> Panning = new();

        [Input(Guid = "d1a11c4c-9526-4f6b-873e-1798b9dd2b48")]
        public readonly InputSlot<float> Speed = new();

        [Input(Guid = "a5de0d72-5924-4f3a-a02f-d5de7c03f07f")]
        public readonly InputSlot<float> Seek = new();

        [Input(Guid = "905d9e01-b1fb-47c0-801c-fc920ed36884", MappedType = typeof(AdsrCalculator.TriggerMode))]
        public readonly InputSlot<int> TriggerMode = new();

        [Input(Guid = "f7a8b9c0-d1e2-4f3a-5b6c-7d8e9f0a1b2c")]
        public readonly InputSlot<float> Duration = new();

        [Input(Guid = "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e")]
        public readonly InputSlot<bool> UseEnvelope = new();
        
        [Input(Guid = "3dbcbbe6-a8b4-4b83-a2c0-e22b24b91b42")]
        public readonly InputSlot<Vector4> Envelope = new();

        /// <summary>
        /// The sampler's audio, as a node in the audio-processing graph — the primary output, since routing the
        /// sound is what this operator is for. Wire it into an [AudioBus] (directly or through a [CombineAudio])
        /// and group volume, effect inserts and ducking all apply; evaluating it is also what drives playback,
        /// so nothing else has to be connected. Left unwired the sampler plays through the operator mixer
        /// exactly as before, driven by <see cref="Result"/>.
        /// </summary>
        [Output(Guid = "894b7295-067f-40fe-b8d3-0b279ec0c791", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
        public readonly Slot<AudioGraphNode> AudioReference = new();

        [Output(Guid = "2433f838-a8ba-4f3a-809e-2d41c404bb84")]
        public readonly Slot<Command> Result = new();

        [Output(Guid = "960aa0a3-89b4-4eff-8b52-36ff6965cf8f")]
        public readonly Slot<bool> IsPlaying = new();

        [Output(Guid = "b09d215a-bcf0-479a-a649-56f9c698ecb1")]
        public readonly Slot<float> GetLevel = new();

        private Guid _operatorId;
        private bool _wasPausedLastFrame;
        private bool _previousPlayTrigger;
        private int _lastPlaybackFrame = int.MinValue / 2;
        private readonly AdsrCalculator _calculator = new();
        private readonly AudioGraphNode _node;

        public PlayAudioSample()
        {
            Result.UpdateAction += UpdatePlayback;
            IsPlaying.UpdateAction += UpdateStatus;
            GetLevel.UpdateAction += UpdateStatus;

            // The engine owns the stream's lifetime, triggers and position; routed into a bus, the graph owns
            // only its mixer membership and gain. Un-buffered, so a trigger stays as responsive as before.
            _node = new AudioGraphNode(this) { ExternallyManagedChannel = true };
            AudioReference.Value = _node;
            AudioReference.UpdateAction += UpdateAudioReference;
        }

        // Wired into the graph, or auto-collected by a bus/combine (which stamps the node) — either way the
        // graph has taken the channel, and the engine must stand down from level and mixer membership.
        private bool IsRoutedToGraph()
            => IsAudioReferenceWired() || Playback.FrameCount - _node.LastCollectedFrame <= GraphFrameSlack;

        private bool IsAudioReferenceWired()
        {
            var connections = Parent?.Symbol.Connections;
            if (connections == null)
                return false;

            for (var i = 0; i < connections.Count; i++)
            {
                var c = connections[i];
                if (c.SourceParentOrChildId == SymbolChildId && c.SourceSlotId == AudioReference.Id)
                    return true;
            }

            return false;
        }

        // Evaluation by a bus is itself the drive: wiring the sampler into the graph is enough to play it,
        // without also routing Result into a command chain. Unlike a video, a sampler has no picture whose
        // absence would justify silence — the audio is the whole point of the operator.
        private void UpdateAudioReference(EvaluationContext context)
        {
            UpdatePlayback(context);
            _node.Update(context);
            AudioReference.DirtyFlag.Clear();
        }

        // A bus may evaluate the reference output before the playback path runs in the same frame.
        private const int GraphFrameSlack = 2;

        private void EnsureOperatorId()
        {
            if (_operatorId != Guid.Empty)
                return;

            _operatorId = AudioPlayerUtils.ComputeInstanceGuid(InstancePath);
            Log.Gated.Audio($"[PlayAudioSample] Initialized: {_operatorId}");
        }

        private void UpdateStatus(EvaluationContext context)
        {
            EnsureOperatorId();
            IsPlaying.Value = AudioEngine.IsOperatorStreamPlaying(_operatorId);
            GetLevel.Value = AudioEngine.GetOperatorLevel(_operatorId);
        }

        private void UpdatePlayback(EvaluationContext context)
        {
            // Both Result and AudioReference drive this, so it can be reached twice in one frame. Running twice
            // would consume the same PlayAudio edge twice and desync the envelope's attack/release triggers.
            if (_lastPlaybackFrame == Playback.FrameCount)
                return;

            _lastPlaybackFrame = Playback.FrameCount;
            EnsureOperatorId();

            string filePath = AudioFile.GetValue(context);
            bool shouldPlay = PlayAudio.GetValue(context);

            var shouldStop = StopAudio.GetValue(context) || !shouldPlay;
            var shouldPause = PauseAudio.GetValue(context);
            var volume = Volume.GetValue(context);
            var mute = Mute.GetValue(context);
            var panning = Panning.GetValue(context);
            var speed = Speed.GetValue(context);
            var seek = Seek.GetValue(context);
            var triggerMode = (AdsrCalculator.TriggerMode)TriggerMode.GetValue(context);
            var duration = Duration.GetValue(context);
            var useEnvelope = UseEnvelope.GetValue(context);
            var envelope = Envelope.GetValue(context);

            // Apply defaults
            if (duration <= 0) duration = float.MaxValue;

            // Extract ADSR from Vector4: X=Attack, Y=Decay, Z=Sustain, W=Release
            var attack = envelope.X > 0 ? envelope.X : 0.01f;
            var decay = envelope.Y > 0 ? envelope.Y : 0.1f;
            var sustain = envelope.Z >= 0 ? Math.Clamp(envelope.Z, 0f, 1f) : 0.7f;
            var release = envelope.W > 0 ? envelope.W : 0.3f;

            // Update ADSR calculator parameters
            _calculator.SetParameters(attack, decay, sustain, release);
            _calculator.SetMode(triggerMode);
            _calculator.SetDuration(duration);

            // Detect play trigger edges for ADSR
            var risingEdge = shouldPlay && !_previousPlayTrigger;
            var fallingEdge = !shouldPlay && _previousPlayTrigger;
            _previousPlayTrigger = shouldPlay;

            if (useEnvelope)
            {
                if (risingEdge)
                {
                    _calculator.TriggerAttack();
                }
                else if (fallingEdge && triggerMode == AdsrCalculator.TriggerMode.Gate)
                {
                    _calculator.TriggerRelease();
                }

                // Update envelope (frame-based for UI display)
                _calculator.Update(shouldPlay, context.LocalFxTime, attack, decay, sustain, release, triggerMode, duration);
            }

            // Apply envelope to volume only if UseEnvelope is enabled
            var envelopeModulatedVolume = useEnvelope ? volume * _calculator.Value : volume;

            // Handle pause/resume transitions
            if (shouldPause != _wasPausedLastFrame)
            {
                if (shouldPause)
                    AudioEngine.PauseOperator(_operatorId);
                else
                    AudioEngine.ResumeOperator(_operatorId);
            }
            _wasPausedLastFrame = shouldPause;

            var routedToGraph = IsRoutedToGraph();

            AudioEngine.UpdateStereoOperatorPlayback(
                operatorId: _operatorId,
                filePath: filePath,
                shouldPlay: shouldPlay,
                shouldStop: shouldStop,
                volume: envelopeModulatedVolume,
                mute: mute,
                panning: panning,
                speed: speed,
                seek: seek,
                routedToGraph: routedToGraph);

            // The graph applies the level the engine just stood down from, envelope included, so a group
            // volume or a duck scales the sampler the same way it scales any other source.
            _node.Gain = mute ? 0f : envelopeModulatedVolume;
            _node.SourceChannel = AudioEngine.TryGetOperatorChannel(_operatorId, out var channel) ? channel : 0;

            IsPlaying.Value = AudioEngine.IsOperatorStreamPlaying(_operatorId);
            GetLevel.Value = AudioEngine.GetOperatorLevel(_operatorId);
        }

        ~PlayAudioSample()
        {
            if (_operatorId != Guid.Empty)
                AudioEngine.UnregisterOperator(_operatorId);
        }
    }
}
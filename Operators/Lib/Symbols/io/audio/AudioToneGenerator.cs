#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Audio.Native;
using T3.Core.Audio.Graph;

namespace Lib.io.audio
{
    /// <summary>
    /// Generates a tone (sine, square, saw, triangle, noise) as an audio-graph source with a sample-accurate
    /// ADSR envelope. Wire <see cref="AudioReference"/> into an [AudioBus] (directly or through [CombineAudio])
    /// for routing and mixing. Unlike [AudioClip] (which sounds by being on the timeline), a tone generator is
    /// only meaningful driven by animated parameters — so it deliberately does not auto-play while unwired
    /// (no <see cref="IAudioSource"/>), and breaking its connection silences it immediately.
    ///
    /// Trigger modes: a pulse (0→1) starts playback for <see cref="Duration"/>, or Gate keeps the tone
    /// sounding while <see cref="Trigger"/> is true.
    /// </summary>
    [Guid("7c8f3a2e-9d4b-4e1f-8a5c-6b2d9f7e4c3a")]
    internal sealed class AudioToneGenerator : Instance<AudioToneGenerator>
    {
        [Output(Guid = "7c8f3a2e-0001-4e1f-8a5c-6b2d9f7e4c3a", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
        public readonly Slot<AudioGraphNode> AudioReference = new();

        [Output(Guid = "b7e2c1a4-5d3f-4e8a-9c2f-1e4b7a6c3d8f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Command> Result = new();

        [Output(Guid = "960aa0a3-89b4-4eff-8b52-36ff6965cf8f")]
        public readonly Slot<bool> IsPlaying = new();

        [Output(Guid = "b09d215a-bcf0-479a-a649-56f9c698ecb1")]
        public readonly Slot<float> GetLevel = new();

        public AudioToneGenerator()
        {
            _node = new AudioGraphNode(this);
            AudioReference.Value = _node;
            AudioReference.UpdateAction += UpdateAudioReference;
            Result.UpdateAction += UpdatePlayback;
            IsPlaying.UpdateAction += UpdateStatus;
            GetLevel.UpdateAction += UpdateStatus;
        }

        private void UpdateAudioReference(EvaluationContext context)
        {
            UpdatePlayback(context);
            _node.Update(context);
        }

        private void UpdatePlayback(EvaluationContext context)
        {
            ApplyParams(Trigger.GetValue(context),
                        Frequency.GetValue(context),
                        Duration.GetValue(context),
                        Volume.GetValue(context),
                        Mute.GetValue(context),
                        Panning.GetValue(context),
                        WaveformType.GetValue(context),
                        TriggerMode.GetValue(context),
                        Envelope.GetValue(context));
        }

        // Frame-keyed so evaluating several outputs in one frame doesn't double-run the trigger edge
        // detection; after an evaluation gap (unwired, then rewired) the envelope restarts from silence
        // instead of firing a phantom edge from the stale trigger state.
        private void ApplyParams(bool shouldPlay, float frequency, float duration, float volume, bool mute, float panning,
                                 int waveformType, int triggerModeValue, Vector4 envelope)
        {
            if (_lastApplyFrame == Playback.FrameCount)
                return;

            var resumedAfterGap = _lastApplyFrame >= 0 && Playback.FrameCount - _lastApplyFrame > 2;
            _lastApplyFrame = Playback.FrameCount;

            EnsureToneStream();
            if (_toneStream == null)
                return;

            var triggerMode = (AdsrCalculator.TriggerMode)triggerModeValue;
            var attack = envelope.X > 0 ? envelope.X : 0.01f;
            var decay = envelope.Y > 0 ? envelope.Y : 0.1f;
            var sustain = envelope.Z >= 0 ? Math.Clamp(envelope.Z, 0f, 1f) : 0.7f;
            var release = envelope.W > 0 ? envelope.W : 0.3f;

            if (frequency <= 0)
            {
                frequency = 440f;
            }

            // The duration only limits the Trigger mode; a Gate sustains until it closes.
            var durationSecs = triggerMode == AdsrCalculator.TriggerMode.Trigger && duration > 0 ? duration : -1f;
            _toneStream.SetParameters(frequency, waveformType, attack, decay, sustain, release, durationSecs);

            if (resumedAfterGap)
            {
                _previousTrigger = shouldPlay;
                _toneStream.Reset();
            }

            var risingEdge = shouldPlay && !_previousTrigger;
            var fallingEdge = !shouldPlay && _previousTrigger;
            _previousTrigger = shouldPlay;

            if (risingEdge)
            {
                _toneStream.TriggerAttack();
            }
            else if (fallingEdge)
            {
                if (triggerMode == AdsrCalculator.TriggerMode.Gate)
                {
                    _toneStream.TriggerRelease();
                }
            }
            else if (!shouldPlay && !_toneStream.IsActive)
            {
                _toneStream.Reset();
            }

            // Volume/mute live on the node's gain (applied by whichever bus routes the channel), so group
            // and bus gain folds compose with it; the stream itself only renders waveform × envelope.
            _toneStream.SetPanning(Math.Clamp(panning, -1f, 1f));
            _node.SourceChannel = _toneStream.Handle;
            _node.Gain = mute ? 0f : volume;
            _node.SourceLabel = $"{frequency:0}Hz";

            IsPlaying.Value = _toneStream.IsActive;
            GetLevel.Value = _toneStream.IsActive ? _toneStream.Level : 0f;
        }

        private void UpdateStatus(EvaluationContext context)
        {
            if (_toneStream == null)
            {
                IsPlaying.Value = false;
                GetLevel.Value = 0;
                return;
            }

            IsPlaying.Value = _toneStream.IsActive;
            GetLevel.Value = _toneStream.IsActive ? _toneStream.Level : 0f;
        }

        private void EnsureToneStream()
        {
            if (_toneStream != null || _streamCreationFailed)
                return;

            AudioMixerManager.Initialize();
            _toneStream = ToneGeneratorSource.TryCreate();
            if (_toneStream == null)
                _streamCreationFailed = true;
        }

        ~AudioToneGenerator()
        {
            _toneStream?.Dispose();
        }

        internal enum WaveformTypes
        {
            Sine = 0,
            Square = 1,
            Sawtooth = 2,
            Triangle = 3,
            WhiteNoise = 4,
            PinkNoise = 5
        }

        private readonly AudioGraphNode _node;
        private ToneGeneratorSource? _toneStream;
        private bool _streamCreationFailed;
        private bool _previousTrigger;
        private int _lastApplyFrame = -1;

        [Input(Guid = "3e9a7f2c-4d8b-4c1f-9e5a-2b7d6f8c4a5e")]
        public readonly InputSlot<bool> Trigger = new();

        [Input(Guid = "8f4a2e9c-7d3b-4e1f-8c5a-9b2d6f7c3a4e")]
        public readonly InputSlot<float> Frequency = new();

        [Input(Guid = "2c9f4e7a-3d8b-4a1f-9e5c-6b2d7f8c4a3e")]
        public readonly InputSlot<float> Duration = new();

        [Input(Guid = "c0645e37-db4e-4658-9d65-96478851f6f6")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "1a3f4b7c-12d3-4a5b-9c7d-8e1f2a3b4c5d")]
        public readonly InputSlot<bool> Mute = new();

        [Input(Guid = "5b3c95f4-b415-4497-b7d1-72fe10b1d720")]
        public readonly InputSlot<float> Panning = new();

        [Input(Guid = "5a7e9f2c-8d4b-4c1f-9a5e-3b2d6f7c8a4e", MappedType = typeof(WaveformTypes))]
        public readonly InputSlot<int> WaveformType = new();

        [Input(Guid = "d4e5f6a7-b8c9-4d0e-1f2a-3b4c5d6e7f8a", MappedType = typeof(AdsrCalculator.TriggerMode))]
        public readonly InputSlot<int> TriggerMode = new();

        // ADSR envelope: X=Attack, Y=Decay, Z=Sustain, W=Release (seconds; sustain 0..1)
        [Input(Guid = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
        public readonly InputSlot<Vector4> Envelope = new();
    }
}

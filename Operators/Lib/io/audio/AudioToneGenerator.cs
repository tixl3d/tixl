#nullable enable
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.Audio;

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
                        WaveformType.GetValue(context),
                        TriggerMode.GetValue(context),
                        Envelope.GetValue(context));
        }

        // Frame-keyed so evaluating several outputs in one frame doesn't double-run the trigger edge
        // detection; after an evaluation gap (unwired, then rewired) the envelope restarts from silence
        // instead of firing a phantom edge from the stale trigger state.
        private void ApplyParams(bool shouldPlay, float frequency, float duration, float volume, bool mute,
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

            if (duration <= 0)
            {
                duration = float.MaxValue;
            }

            _toneStream.Frequency = frequency;
            _toneStream.WaveformType = (WaveformTypes)waveformType;
            _toneStream.Adsr.SetParameters(attack, decay, sustain, release);
            _toneStream.Adsr.SetMode(triggerMode);
            _toneStream.Adsr.SetDuration(duration);

            if (resumedAfterGap)
            {
                _previousTrigger = shouldPlay;
                _toneStream.Adsr.Reset();
            }

            var risingEdge = shouldPlay && !_previousTrigger;
            var fallingEdge = !shouldPlay && _previousTrigger;
            _previousTrigger = shouldPlay;

            if (risingEdge)
            {
                _toneStream.Adsr.TriggerAttack();
            }
            else if (fallingEdge)
            {
                if (triggerMode == AdsrCalculator.TriggerMode.Gate)
                {
                    _toneStream.Adsr.TriggerRelease();
                }
            }
            else if (!shouldPlay && !_toneStream.Adsr.IsActive)
            {
                _toneStream.Adsr.Reset();
            }

            // Volume/mute live on the node's gain (applied by whichever bus routes the channel), so group
            // and bus gain folds compose with it; the stream itself only renders waveform × envelope.
            _node.SourceChannel = _toneStream.StreamHandle;
            _node.Gain = mute ? 0f : volume;
            _node.SourceLabel = $"{frequency:0}Hz";

            IsPlaying.Value = _toneStream.Adsr.IsActive;
            GetLevel.Value = _toneStream.GetLevel();
        }

        private void UpdateStatus(EvaluationContext context)
        {
            if (_toneStream == null)
            {
                IsPlaying.Value = false;
                GetLevel.Value = 0;
                return;
            }

            IsPlaying.Value = _toneStream.Adsr.IsActive;
            GetLevel.Value = _toneStream.GetLevel();
        }

        private void EnsureToneStream()
        {
            if (_toneStream != null || _streamCreationFailed)
                return;

            if (!AudioMixerManager.IsInitialized)
            {
                AudioMixerManager.Initialize();
                if (!AudioMixerManager.IsInitialized)
                    return;
            }

            _toneStream = ProceduralToneStream.Create();
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

        /// <summary>
        /// Renders waveform × ADSR into an unrouted BASS decode stream; the audio graph (an [AudioBus] or the
        /// implicit collector) routes the channel and applies gain.
        /// </summary>
        private sealed class ProceduralToneStream
        {
            public int StreamHandle { get; private set; }
            public readonly AdsrCalculator Adsr = new();

            public float Frequency
            {
                get => _frequency;
                set => _frequency = Math.Clamp(value, 20f, 20000f);
            }

            public WaveformTypes WaveformType
            {
                get => (WaveformTypes)_waveformType;
                set => _waveformType = (int)value;
            }

            public static ProceduralToneStream? Create()
            {
                var instance = new ProceduralToneStream();
                instance._gcHandle = GCHandle.Alloc(instance);

                var streamHandle = Bass.CreateStream(AudioConfig.MixerFrequency,
                                                     Channels,
                                                     BassFlags.Float | BassFlags.Decode,
                                                     instance._streamProc,
                                                     GCHandle.ToIntPtr(instance._gcHandle));
                if (streamHandle == 0)
                {
                    Log.Error($"[AudioToneGenerator] Failed to create tone stream: {Bass.LastError}");
                    instance._gcHandle.Free();
                    return null;
                }

                instance.StreamHandle = streamHandle;
                return instance;
            }

            public float GetLevel()
            {
                if (!Adsr.IsActive)
                    return 0f;

                // Buffer-inspecting Ex variant — the plain ChannelGetLevel only sees data taken since the
                // last call and mostly reads 0 between the device's coarse pulls.
                if (BassMix.ChannelGetLevel(StreamHandle, _levelPair, 0.05f, 0) == -1)
                    return _lastLevel;

                _lastLevel = Math.Min(Math.Max(_levelPair[0], _levelPair[1]), 1f);
                return _lastLevel;
            }

            public void Dispose()
            {
                Adsr.Reset();
                BassMix.MixerRemoveChannel(StreamHandle);
                Bass.StreamFree(StreamHandle);

                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();
            }

            private ProceduralToneStream()
            {
                _streamProc = StreamCallback;
                _sampleRate = AudioConfig.MixerFrequency;
                Adsr.SetSampleRate(_sampleRate);
            }

            private int StreamCallback(int handle, IntPtr buffer, int length, IntPtr user)
            {
                var floatCount = length / sizeof(float);
                if (_sampleBuffer.Length < floatCount)
                    _sampleBuffer = new float[floatCount];

                var phaseIncrement = 2.0 * Math.PI * _frequency / _sampleRate;
                var waveType = _waveformType;

                for (var i = 0; i < floatCount; i++)
                {
                    var envelopeGain = Adsr.UpdateSample();
                    _sampleBuffer[i] = GenerateSample(_phase, waveType) * envelopeGain;
                    _phase += phaseIncrement;

                    if (_phase >= 2.0 * Math.PI)
                        _phase -= 2.0 * Math.PI;
                }

                Marshal.Copy(_sampleBuffer, 0, buffer, floatCount);
                return length;
            }

            private float GenerateSample(double phase, int waveType)
            {
                var t = phase / (2.0 * Math.PI);

                switch (waveType)
                {
                    case (int)WaveformTypes.Sine:
                        return (float)Math.Sin(phase);
                    case (int)WaveformTypes.Square:
                        return t < 0.5 ? 0.8f : -0.8f;
                    case (int)WaveformTypes.Sawtooth:
                        return (float)(2.0 * t - 1.0) * 0.8f;
                    case (int)WaveformTypes.Triangle:
                        return (float)(4.0 * Math.Abs(t - 0.5) - 1.0) * 0.8f;
                    case (int)WaveformTypes.WhiteNoise:
                        return (float)(_noiseRng.NextDouble() * 2.0 - 1.0) * 0.5f;
                    case (int)WaveformTypes.PinkNoise:
                    {
                        var white = _noiseRng.NextDouble() * 2.0 - 1.0;
                        _pinkB0 = 0.99765 * _pinkB0 + white * 0.0990460;
                        _pinkB1 = 0.96300 * _pinkB1 + white * 0.2965164;
                        _pinkB2 = 0.57000 * _pinkB2 + white * 1.0526913;
                        var pink = _pinkB0 + _pinkB1 + _pinkB2 + white * 0.1848;
                        return (float)(pink * 0.15);
                    }
                    default:
                        return (float)Math.Sin(phase);
                }
            }

            private const int Channels = 1;
            private readonly StreamProcedure _streamProc;
            private readonly int _sampleRate;
            private GCHandle _gcHandle;
            private volatile float _frequency = 440f;
            private volatile int _waveformType;
            private double _phase;
            private float[] _sampleBuffer = [];
            private float _lastLevel;
            private readonly float[] _levelPair = new float[2];
            private readonly Random _noiseRng = new();
            private double _pinkB0, _pinkB1, _pinkB2;
        }

        private readonly AudioGraphNode _node;
        private ProceduralToneStream? _toneStream;
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

        [Input(Guid = "5a7e9f2c-8d4b-4c1f-9a5e-3b2d6f7c8a4e", MappedType = typeof(WaveformTypes))]
        public readonly InputSlot<int> WaveformType = new();

        [Input(Guid = "d4e5f6a7-b8c9-4d0e-1f2a-3b4c5d6e7f8a", MappedType = typeof(AdsrCalculator.TriggerMode))]
        public readonly InputSlot<int> TriggerMode = new();

        // ADSR envelope: X=Attack, Y=Decay, Z=Sustain, W=Release (seconds; sustain 0..1)
        [Input(Guid = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
        public readonly InputSlot<Vector4> Envelope = new();
    }
}

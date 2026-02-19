#nullable enable
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Audio;
// ReSharper disable UnusedMember.Local
// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.audio
{
    /// <summary>
    /// Generates test tones procedurally in real-time via the operator mixer.
    /// No external files are created - audio is synthesized on-the-fly.
    /// 
    /// Supports two input modes:
    /// - Trigger: A pulse (0→1) starts playback for the specified duration
    /// - Gate: Sound plays while input is true, releases when false (duration ignored)
    /// 
    /// Uses sample-accurate AdsrCalculator for envelope shaping in the audio thread.
    /// </summary>
    [Guid("7c8f3a2e-9d4b-4e1f-8a5c-6b2d9f7e4c3a")]
    internal sealed class AudioToneGenerator : Instance<AudioToneGenerator>
    {
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

        // ADSR Envelope as Vector4: X=Attack, Y=Decay, Z=Sustain, W=Release
        [Input(Guid = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
        public readonly InputSlot<Vector4> Envelope = new();

        [Output(Guid = "b7e2c1a4-5d3f-4e8a-9c2f-1e4b7a6c3d8f")]
        public readonly Slot<Command> Result = new();

        [Output(Guid = "960aa0a3-89b4-4eff-8b52-36ff6965cf8f")]
        public readonly Slot<bool> IsPlaying = new();

        [Output(Guid = "b09d215a-bcf0-479a-a649-56f9c698ecb1")]
        public readonly Slot<float> GetLevel = new();

        private Guid _operatorId;
        private ProceduralToneStream? _toneStream;
        private bool _previousTrigger;

        public AudioToneGenerator()
        {
            Result.UpdateAction += Update;
            IsPlaying.UpdateAction += Update;
            
            // Do not update on GetLevel - it overrides stale state when result is not evaluating
            //GetLevel.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            if (_operatorId == Guid.Empty)
            {
                _operatorId = AudioPlayerUtils.ComputeInstanceGuid(InstancePath);
                Log.Gated.Audio($"[TestToneGenerator] Initialized: {_operatorId}");
            }

            var trigger = Trigger.GetValue(context);
            var frequency = Frequency.GetValue(context);
            var duration = Duration.GetValue(context);
            var volume = Volume.GetValue(context);
            var mute = Mute.GetValue(context);
            var waveformType = WaveformType.GetValue(context);
            var triggerMode = (AdsrCalculator.TriggerMode)TriggerMode.GetValue(context);
            var envelope = Envelope.GetValue(context);

            // Extract ADSR from Vector4: X=Attack, Y=Decay, Z=Sustain, W=Release
            var attack = envelope.X > 0 ? envelope.X : 0.01f;
            var decay = envelope.Y > 0 ? envelope.Y : 0.1f;
            var sustain = envelope.Z >= 0 ? Math.Clamp(envelope.Z, 0f, 1f) : 0.7f;
            var release = envelope.W > 0 ? envelope.W : 0.3f;

            // Apply defaults
            if (frequency <= 0) frequency = 440f;
            if (duration <= 0) duration = float.MaxValue;

            // Ensure stream is created
            EnsureToneStream();

            if (_toneStream == null)
            {
                IsPlaying.Value = false;
                GetLevel.Value = 0;
                return;
            }

            // Update audio parameters (thread-safe)
            _toneStream.Frequency = frequency;
            _toneStream.WaveformType = (WaveformTypes)waveformType;
            _toneStream.SetVolume(volume, mute);
            
            // Update ADSR parameters on the shared calculator (thread-safe)
            _toneStream.Adsr.SetParameters(attack, decay, sustain, release);
            _toneStream.Adsr.SetMode(triggerMode);
            _toneStream.Adsr.SetDuration(duration);

            // Detect edges and send trigger/release signals to audio thread
            var risingEdge = trigger && !_previousTrigger;
            var fallingEdge = !trigger && _previousTrigger;
            _previousTrigger = trigger;

            if (risingEdge)
            {
                _toneStream.Adsr.TriggerAttack();
                Log.Gated.Audio($"[TestToneGenerator] ▶ Triggered @ {frequency}Hz");
            }
            else if (fallingEdge && triggerMode == AdsrCalculator.TriggerMode.Gate)
            {
                _toneStream.Adsr.TriggerRelease();
                Log.Gated.Audio($"[TestToneGenerator] ~ Release triggered");
            }

            bool isActive = _toneStream.Adsr.IsActive;

            IsPlaying.Value = isActive;
            GetLevel.Value = _toneStream.GetLevel();
        }

        private void EnsureToneStream()
        {
            if (_toneStream != null) return;

            if (!AudioMixerManager.IsInitialized)
            {
                AudioMixerManager.Initialize();
                if (AudioMixerManager.OperatorMixerHandle == 0)
                {
                    // Only log once, not every frame
                    if (!_mixerWarningLogged)
                    {
                        Log.Warning("[TestToneGenerator] Mixer not available");
                        _mixerWarningLogged = true;
                    }
                    return;
                }
            }

            _toneStream = ProceduralToneStream.Create(AudioMixerManager.OperatorMixerHandle);
            if (_toneStream != null)
                Log.Gated.Audio($"[TestToneGenerator] Created procedural tone stream");
        }
        
        private bool _mixerWarningLogged;

        ~AudioToneGenerator()
        {

            _toneStream?.Dispose();
        }

        private enum WaveformTypes
        {
            Sine = 0,
            Square = 1,
            Sawtooth = 2,
            Triangle = 3,
            WhiteNoise = 4,
            PinkNoise = 5
        }

        /// <summary>
        /// Procedural tone stream that generates waveforms in real-time via BASS callback stream.
        /// Uses the shared AdsrCalculator for sample-accurate envelope calculation.
        /// </summary>
        private sealed class ProceduralToneStream
        {
            private const int Channels = 1;

            // Thread-safe audio parameters
            private volatile float _frequency = 440f;
            private volatile int _waveformType;
            private volatile float _currentVolume = 1.0f;
            private volatile bool _isMuted;

            // Shared ADSR calculator (used by audio thread)
            public readonly AdsrCalculator Adsr = new();

            // Sample rate from AudioConfig (set at creation)
            private readonly int _sampleRate;

            public float Frequency
            {
                get => _frequency;
                set => _frequency = Math.Max(20f, Math.Min(20000f, value));
            }

            public WaveformTypes WaveformType
            {
                get => (WaveformTypes)_waveformType;
                set => _waveformType = (int)value;
            }

            private int _streamHandle;
            // ReSharper disable once NotAccessedField.Local
            private readonly int _mixerHandle;
            private double _phase;

            private readonly List<float> _waveformBuffer = new();
            private readonly List<float> _spectrumBuffer = new();
            private float _lastLevel;

            private readonly StreamProcedure _streamProc;
            private GCHandle _gcHandle;

            private ProceduralToneStream(int mixerHandle)
            {
                _mixerHandle = mixerHandle;
                _streamProc = StreamCallback;
                _sampleRate = AudioConfig.MixerFrequency;
                Adsr.SetSampleRate(_sampleRate);
            }

            public static ProceduralToneStream? Create(int mixerHandle)
            {
                var instance = new ProceduralToneStream(mixerHandle);
                instance._gcHandle = GCHandle.Alloc(instance);

                var streamHandle = Bass.CreateStream(
                    AudioConfig.MixerFrequency,
                    Channels,
                    BassFlags.Float | BassFlags.Decode,
                    instance._streamProc,
                    GCHandle.ToIntPtr(instance._gcHandle));

                if (streamHandle == 0)
                {
                    Log.Error($"[ProceduralToneStream] Failed to create stream: {Bass.LastError}");
                    instance._gcHandle.Free();
                    return null;
                }

                instance._streamHandle = streamHandle;

                if (!BassMix.MixerAddChannel(mixerHandle, streamHandle, BassFlags.MixerChanBuffer))
                {
                    Log.Error($"[ProceduralToneStream] Failed to add to mixer: {Bass.LastError}");
                    Bass.StreamFree(streamHandle);
                    instance._gcHandle.Free();
                    return null;
                }

                Bass.ChannelSetAttribute(streamHandle, ChannelAttribute.Volume, 1f);
                return instance;
            }

            private int StreamCallback(int handle, IntPtr buffer, int length, IntPtr user)
            {
                int floatCount = length / sizeof(float);
                int sampleCount = floatCount; // Mono: 1 sample per float

                var floatBuffer = new float[floatCount];

                // Read thread-safe parameters
                float freq = _frequency;
                int waveType = _waveformType;
                float baseVol = _isMuted ? 0f : _currentVolume;
                double phaseIncrement = 2.0 * Math.PI * freq / _sampleRate;

                // Generate mono samples with sample-accurate envelope
                for (int i = 0; i < sampleCount; i++)
                {
                    // Get envelope value for this sample (advances internal state)
                    float envelopeGain = Adsr.UpdateSample();

                    float sample = GenerateSample(_phase, waveType) * baseVol * envelopeGain;
                    _phase += phaseIncrement;

                    if (_phase >= 2.0 * Math.PI)
                        _phase -= 2.0 * Math.PI;

                    floatBuffer[i] = sample;
                }

                Marshal.Copy(floatBuffer, 0, buffer, floatCount);
                return length;
            }

            // Instance-level noise state to avoid static issues with multiple instances
            private readonly Random _noiseRng = new();
            private double _pinkB0, _pinkB1, _pinkB2;

            private float GenerateSample(double phase, int waveType)
            {
                double normalizedPhase = phase - Math.Floor(phase / (2.0 * Math.PI)) * 2.0 * Math.PI;
                double t = normalizedPhase / (2.0 * Math.PI);

                switch (waveType)
                {
                    case 0: // Sine
                        return (float)Math.Sin(normalizedPhase);
                    case 1: // Square
                        return t < 0.5 ? 0.8f : -0.8f;
                    case 2: // Sawtooth
                        return (float)(2.0 * t - 1.0) * 0.8f;
                    case 3: // Triangle
                        return (float)(4.0 * Math.Abs(t - 0.5) - 1.0) * 0.8f;
                    case 4: // WhiteNoise
                        return (float)(_noiseRng.NextDouble() * 2.0 - 1.0) * 0.5f;
                    case 5: // PinkNoise
                        {
                            double white = _noiseRng.NextDouble() * 2.0 - 1.0;
                            _pinkB0 = 0.99765 * _pinkB0 + white * 0.0990460;
                            _pinkB1 = 0.96300 * _pinkB1 + white * 0.2965164;
                            _pinkB2 = 0.57000 * _pinkB2 + white * 1.0526913;
                            double pink = _pinkB0 + _pinkB1 + _pinkB2 + white * 0.1848;
                            return (float)(pink * 0.15);
                        }
                    default:
                        return (float)Math.Sin(normalizedPhase);
                }
            }

            public void SetVolume(float volume, bool mute)
            {
                _currentVolume = Math.Clamp(volume, 0f, 1f);
                _isMuted = mute;
            }

            public float GetLevel()
            {
                if (!Adsr.IsActive) return 0f;

                var level = BassMix.ChannelGetLevel(_streamHandle);
                if (level == -1) return _lastLevel;

                var left = (level & 0xFFFF) / 32768f;
                var right = ((level >> 16) & 0xFFFF) / 32768f;
                _lastLevel = Math.Min(Math.Max(left, right), 1f);
                return _lastLevel;
            }

            public void Dispose()
            {
                Adsr.Reset();
                BassMix.MixerRemoveChannel(_streamHandle);
                Bass.StreamFree(_streamHandle);

                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();
            }
        }
    }
}

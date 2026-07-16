#nullable enable
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Audio;

namespace Lib.io.audio
{
    /// <summary>
    /// THROWAWAY SPIKE (2b) — a leaf audio source. Generates a sine tone as a BASS decode stream and
    /// exposes it (unrouted) on its <see cref="AudioGraphNode"/>; the downstream [_AudioRootSpike] routes
    /// it into a bus. Proves the join: R1 collection drives R2 routing — you hear which sources a root
    /// collected. Delete after real ops land.
    /// </summary>
    [Guid("e2b0c1d8-5e42-4c8a-9f31-0ab1cd2ef100")]
    internal sealed class _AudioSourceSpike : Instance<_AudioSourceSpike>
    {
        [Output(Guid = "e2b0c1d8-0001-4c8a-9f31-0ab1cd2ef100")]
        public readonly Slot<AudioGraphNode> Result = new();

        public _AudioSourceSpike()
        {
            _node = new AudioGraphNode(this);
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _frequency = Frequency.GetValue(context);
            EnsureStream();

            _node.SourceChannel = _stream;
            _node.Gain = Volume.GetValue(context);
            var label = Label.GetValue(context);
            _node.SourceLabel = string.IsNullOrEmpty(label) ? $"{_frequency:0}Hz" : label;
            _node.Update(context);
        }

        private void EnsureStream()
        {
            if (_stream != 0)
                return;

            if (!AudioMixerManager.IsInitialized)
            {
                AudioMixerManager.Initialize();
                if (!AudioMixerManager.IsInitialized)
                    return;
            }

            _sampleRate = AudioConfig.MixerFrequency;
            _proc = StreamCallback;
            _gcHandle = GCHandle.Alloc(this);
            _stream = Bass.CreateStream(_sampleRate, 1, BassFlags.Float | BassFlags.Decode, _proc, GCHandle.ToIntPtr(_gcHandle));
            if (_stream == 0)
            {
                Log.Warning($"[_AudioSourceSpike] failed to create tone stream: {Bass.LastError}", this);
                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();
            }
        }

        // Generates a raw sine at _frequency; the root applies gain on the channel, so this is unity (-headroom).
        private int StreamCallback(int handle, IntPtr buffer, int length, IntPtr user)
        {
            var floatCount = length / sizeof(float);
            var buf = new float[floatCount];
            var inc = 2.0 * Math.PI * _frequency / _sampleRate;
            for (var i = 0; i < floatCount; i++)
            {
                buf[i] = (float)Math.Sin(_phase) * 0.3f;
                _phase += inc;
                if (_phase >= 2.0 * Math.PI)
                    _phase -= 2.0 * Math.PI;
            }

            Marshal.Copy(buf, 0, buffer, floatCount);
            return length;
        }

        ~_AudioSourceSpike()
        {
            if (_stream != 0)
            {
                Bass.StreamFree(_stream); // also detaches it from any mixer the root added it to
                _stream = 0;
            }

            if (_gcHandle.IsAllocated)
                _gcHandle.Free();
        }

        private readonly AudioGraphNode _node;
        private int _stream;
        private StreamProcedure? _proc;
        private GCHandle _gcHandle;
        private volatile float _frequency = 440f;
        private double _phase;
        private int _sampleRate;

        [Input(Guid = "e2b0c1d8-0002-4c8a-9f31-0ab1cd2ef100")]
        public readonly InputSlot<string> Label = new();

        [Input(Guid = "e2b0c1d8-0003-4c8a-9f31-0ab1cd2ef100")]
        public readonly InputSlot<float> Frequency = new();

        [Input(Guid = "e2b0c1d8-0004-4c8a-9f31-0ab1cd2ef100")]
        public readonly InputSlot<float> Volume = new();
    }
}

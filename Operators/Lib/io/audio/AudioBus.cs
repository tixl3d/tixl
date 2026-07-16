#nullable enable
using ManagedBass;
using ManagedBass.Mix;
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

        public AudioBus()
        {
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            EnsureSubmix();
            if (_submix == 0)
                return;

            _collected.Clear();
            var inputs = Input.GetCollectedTypedInputs(true);
            for (var i = 0; i < inputs.Count; i++)
                inputs[i]?.GetValue(context)?.Collect(_collected, 1f);
            Input.DirtyFlag.Clear();

            _desired.Clear();
            for (var i = 0; i < _collected.Count; i++)
            {
                var src = _collected[i];
                if (src.Leaf.Routing == AudioGraphNode.RoutingKind.Direct)
                    continue; // spatial/HW-3D bypasses the bus

                var ch = src.Leaf.SourceChannel;
                if (ch == 0)
                    continue;

                _desired.Add(ch);
                Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, src.Gain);
            }

            var changed = false;
            foreach (var ch in _desired)
            {
                if (_routed.Contains(ch))
                    continue;

                if (BassMix.MixerAddChannel(_submix, ch, BassFlags.MixerChanBuffer))
                {
                    _routed.Add(ch);
                    changed = true;
                }
            }

            _toRemove.Clear();
            foreach (var ch in _routed)
                if (!_desired.Contains(ch))
                    _toRemove.Add(ch);

            for (var i = 0; i < _toRemove.Count; i++)
            {
                BassMix.MixerRemoveChannel(_toRemove[i]);
                _routed.Remove(_toRemove[i]);
                changed = true;
            }

            Bass.ChannelSetAttribute(_submix, ChannelAttribute.Volume, Volume.GetValue(context));

            if (!changed)
                return;

            var labels = string.Join(", ", _collected.ConvertAll(c => $"{c.Leaf}×{c.Gain:0.00}"));
            Log.Debug($"[AudioBus] routing {_routed.Count} channel(s): {labels}", this);
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
            if (_submix != 0)
            {
                Bass.StreamFree(_submix);
                _submix = 0;
            }
        }

        private readonly List<AudioGraphNode.CollectedSource> _collected = new();
        private readonly HashSet<int> _desired = new();
        private readonly HashSet<int> _routed = new();
        private readonly List<int> _toRemove = new();
        private int _submix;

        [Input(Guid = "b7e0d240-0002-4c8a-9f31-0ab1cd2e0100")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "b7e0d240-0003-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<float> Volume = new();
    }
}

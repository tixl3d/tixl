#nullable enable
using T3.Core.Audio;
using T3.Core.Audio.Native;

namespace Lib.io.audio
{
    /// <summary>
    /// Adds a low-pass or high-pass filter insert to the audio flowing through it: the routing [AudioBus] realises
    /// this node as its own bus with a filter in it, so everything wired into <see cref="Input"/> shares the effect
    /// (and the <see cref="Volume"/> fold, like a [CombineAudio]).
    /// </summary>
    [Guid("58e37f7f-602d-4567-9155-c193a9d9a0ee")]
    internal sealed class AudioFilter : Instance<AudioFilter>
    {
        [Output(Guid = "77108a3f-7ae8-4005-bdc6-a4e9ff372757")]
        public readonly Slot<AudioGraphNode> Result = new();

        public AudioFilter()
        {
            _node = new AudioGraphNode(this, Input)
                        {
                            FxInsert = new AudioGraphNode.AudioFxInsert
                                           {
                                               Effect = AudioEffectKind.Filter,
                                               Apply = _ => { },
                                               UpdateParams = ApplyParameters,
                                               Remove = _ => { },
                                           }
                        };
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.Gain = Volume.GetValue(context);
            _isHighPass = (FilterModes)Mode.GetValue(context) == FilterModes.HighPass;
            _cutoffHz = Math.Clamp(Cutoff.GetValue(context), MinCutoffHz, MaxCutoffHz);
            _resonance = Math.Clamp(Resonance.GetValue(context), MinResonance, MaxResonance);
            _node.Update(context);
        }

        private void ApplyParameters(AudioHandle bus) => AudioMixerManager.SetFilter(bus, _isHighPass, _cutoffHz, _resonance);

        private enum FilterModes
        {
            LowPass = 0,
            HighPass = 1,
        }

        private const float MinCutoffHz = 20f;
        private const float MaxCutoffHz = 20000f;
        private const float MinResonance = 0.1f;
        private const float MaxResonance = 20f;

        private readonly AudioGraphNode _node;
        private bool _isHighPass;
        private float _cutoffHz;
        private float _resonance;

        [Input(Guid = "40ddd8d4-571c-41d4-98a6-06cc486c9d9f")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "7c65cd76-b026-461c-b01a-629553c015d2")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "d70b95b5-a547-4b1e-9e29-5879319d84aa", MappedType = typeof(FilterModes))]
        public readonly InputSlot<int> Mode = new();

        [Input(Guid = "32ee6dca-f277-49f5-8a6f-2f6c00bcdb7d")]
        public readonly InputSlot<float> Cutoff = new();

        [Input(Guid = "eecb4b94-5069-4a81-b1b8-79b7b9fabd91")]
        public readonly InputSlot<float> Resonance = new();
    }
}

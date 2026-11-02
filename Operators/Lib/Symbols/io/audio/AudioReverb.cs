#nullable enable
using T3.Core.Audio;
using T3.Core.Audio.Native;

namespace Lib.io.audio
{
    /// <summary>
    /// Adds a reverb insert to the audio flowing through it: the routing [AudioBus] realises this node as its
    /// own bus with a reverb in it, so everything wired into <see cref="Input"/> shares the effect (and the
    /// <see cref="Volume"/> fold, like a [CombineAudio]). Unwiring lets the tail ring out and fade.
    /// </summary>
    [Guid("f2b43ea0-1e42-4c8a-9f31-0ab1cd2e0400")]
    internal sealed class AudioReverb : Instance<AudioReverb>
    {
        [Output(Guid = "f2b43ea0-0001-4c8a-9f31-0ab1cd2e0400")]
        public readonly Slot<AudioGraphNode> Result = new();

        public AudioReverb()
        {
            _node = new AudioGraphNode(this, Input)
                        {
                            FxInsert = new AudioGraphNode.AudioFxInsert
                                           {
                                               Effect = AudioEffectKind.Reverb,
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
            _mix = Math.Clamp(Mix.GetValue(context), 0f, 1f);
            _decaySecs = Math.Clamp(Time.GetValue(context), 0.001f, 3f);
            _highFrequencyRatio = Math.Clamp(HighFreqRatio.GetValue(context), 0.001f, 0.999f);
            _node.Update(context);
        }

        private void ApplyParameters(AudioHandle bus) => AudioMixerManager.SetReverb(bus, _mix, _decaySecs, _highFrequencyRatio);

        private readonly AudioGraphNode _node;
        private float _mix;
        private float _decaySecs;
        private float _highFrequencyRatio;

        [Input(Guid = "f2b43ea0-0002-4c8a-9f31-0ab1cd2e0400")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "f2b43ea0-0003-4c8a-9f31-0ab1cd2e0400")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "f2b43ea0-0004-4c8a-9f31-0ab1cd2e0400")]
        public readonly InputSlot<float> Mix = new();

        [Input(Guid = "f2b43ea0-0005-4c8a-9f31-0ab1cd2e0400")]
        public readonly InputSlot<float> Time = new();

        [Input(Guid = "f2b43ea0-0006-4c8a-9f31-0ab1cd2e0400")]
        public readonly InputSlot<float> HighFreqRatio = new();
    }
}

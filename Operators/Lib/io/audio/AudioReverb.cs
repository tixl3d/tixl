#nullable enable
using ManagedBass;
using ManagedBass.DirectX8;

namespace Lib.io.audio
{
    /// <summary>
    /// Adds a reverb insert to the audio flowing through it: the routing [AudioBus] realises this node as its
    /// own submix and applies a DX8 reverb there, so everything wired into <see cref="Input"/> shares the
    /// effect (and the <see cref="Volume"/> fold, like a [CombineAudio]). Unwiring fades the tail out.
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
                                               Apply = ApplyFx,
                                               UpdateParams = UpdateFxParams,
                                               Remove = RemoveFx,
                                           }
                        };
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.Gain = Volume.GetValue(context);

            // DX8 reverb wants dB for the wet mix; map the 0..1 Mix input onto a musical -96..0 dB range.
            var mix = Mix.GetValue(context);
            _parameters.fReverbMix = mix <= 0.001f ? -96f : 20f * MathF.Log10(Math.Min(mix, 1f));
            _parameters.fReverbTime = Math.Clamp(Time.GetValue(context), 0.001f, 3f) * 1000f;
            _parameters.fHighFreqRTRatio = Math.Clamp(HighFreqRatio.GetValue(context), 0.001f, 0.999f);

            _node.Update(context);
        }

        // Bus-side callbacks — invoked per realised submix (a node can be realised by more than one bus).
        private void ApplyFx(int submix)
        {
            var fxHandle = Bass.ChannelSetFX(submix, EffectType.DXReverb, 0);
            if (fxHandle == 0)
            {
                Log.Warning($"[AudioReverb] failed to set reverb: {Bass.LastError}", this);
                return;
            }

            _fxHandles[submix] = fxHandle;
            Bass.FXSetParameters(fxHandle, _parameters);
        }

        private void UpdateFxParams(int submix)
        {
            if (_fxHandles.TryGetValue(submix, out var fxHandle))
                Bass.FXSetParameters(fxHandle, _parameters);
        }

        private void RemoveFx(int submix)
        {
            _fxHandles.Remove(submix); // freeing the submix frees the effect with it
        }

        private readonly AudioGraphNode _node;
        private readonly DXReverbParameters _parameters = new();
        private readonly Dictionary<int, int> _fxHandles = new(); // submix → fx handle

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

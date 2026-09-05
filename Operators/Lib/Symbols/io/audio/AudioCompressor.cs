#nullable enable
using ManagedBass;
using ManagedBass.DirectX8;

namespace Lib.io.audio
{
    /// <summary>
    /// Adds a compressor insert to the audio flowing through it: the routing [AudioBus] realises this node as
    /// its own submix and applies a DX8 compressor there, evening out the dynamics of everything wired into
    /// <see cref="Input"/> (with the <see cref="Volume"/> fold of a [CombineAudio]).
    /// </summary>
    [Guid("b8d65fc0-1e42-4c8a-9f31-0ab1cd2e0600")]
    internal sealed class AudioCompressor : Instance<AudioCompressor>
    {
        [Output(Guid = "b8d65fc0-0001-4c8a-9f31-0ab1cd2e0600")]
        public readonly Slot<AudioGraphNode> Result = new();

        public AudioCompressor()
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

            // DX8 wants the threshold in dB; the input is linear amplitude like the meter ops.
            var threshold = Threshold.GetValue(context);
            _parameters.fThreshold = threshold <= 0.001f ? -60f : Math.Max(-60f, 20f * MathF.Log10(Math.Min(threshold, 1f)));
            _parameters.fRatio = Math.Clamp(Ratio.GetValue(context), 1f, 100f);
            _parameters.fAttack = Math.Clamp(Attack.GetValue(context), 0.0001f, 0.5f) * 1000f;
            _parameters.fRelease = Math.Clamp(Release.GetValue(context), 0.05f, 3f) * 1000f;
            _parameters.fGain = Math.Clamp(MakeupGainDb.GetValue(context), -60f, 60f);

            _node.Update(context);
        }

        // Bus-side callbacks — invoked per realised submix (a node can be realised by more than one bus).
        private void ApplyFx(int submix)
        {
            var fxHandle = Bass.ChannelSetFX(submix, NativeDx8Compressor, 0);
            if (fxHandle == 0)
            {
                Log.Warning($"[AudioCompressor] failed to set compressor: {Bass.LastError}", this);
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

        // ManagedBass's EffectType misnumbers the DX8 effects (its DXCompressor=4 is natively FLANGER).
        // BASS_FX_DX8_COMPRESSOR is 1.
        private const EffectType NativeDx8Compressor = (EffectType)1;

        private readonly AudioGraphNode _node;
        private readonly DXCompressorParameters _parameters = new();
        private readonly Dictionary<int, int> _fxHandles = new(); // submix → fx handle

        [Input(Guid = "b8d65fc0-0002-4c8a-9f31-0ab1cd2e0600")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "b8d65fc0-0003-4c8a-9f31-0ab1cd2e0600")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "b8d65fc0-0004-4c8a-9f31-0ab1cd2e0600")]
        public readonly InputSlot<float> Threshold = new();

        [Input(Guid = "b8d65fc0-0005-4c8a-9f31-0ab1cd2e0600")]
        public readonly InputSlot<float> Ratio = new();

        [Input(Guid = "b8d65fc0-0006-4c8a-9f31-0ab1cd2e0600")]
        public readonly InputSlot<float> Attack = new();

        [Input(Guid = "b8d65fc0-0007-4c8a-9f31-0ab1cd2e0600")]
        public readonly InputSlot<float> Release = new();

        [Input(Guid = "b8d65fc0-0008-4c8a-9f31-0ab1cd2e0600")]
        public readonly InputSlot<float> MakeupGainDb = new();
    }
}

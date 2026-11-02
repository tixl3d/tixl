#nullable enable
using T3.Core.Audio;
using T3.Core.Audio.Native;

namespace Lib.io.audio
{
    /// <summary>
    /// Adds a compressor insert to the audio flowing through it: the routing [AudioBus] realises this node as
    /// its own bus with a compressor in it, evening out the dynamics of everything wired into <see cref="Input"/>
    /// (with the <see cref="Volume"/> fold of a [CombineAudio]).
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
                                               Effect = AudioEffectKind.Compressor,
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
            // The threshold input is linear amplitude like the meter ops.
            var threshold = Threshold.GetValue(context);
            _thresholdDb = threshold <= 0.001f ? -60f : Math.Max(-60f, 20f * MathF.Log10(Math.Min(threshold, 1f)));
            _ratio = Math.Clamp(Ratio.GetValue(context), 1f, 100f);
            _attackSecs = Math.Clamp(Attack.GetValue(context), 0.0001f, 0.5f);
            _releaseSecs = Math.Clamp(Release.GetValue(context), 0.05f, 3f);
            _makeupGainDb = Math.Clamp(MakeupGainDb.GetValue(context), -60f, 60f);
            _node.Update(context);
        }

        private void ApplyParameters(AudioHandle bus) => AudioMixerManager.SetCompressor(bus, _thresholdDb, _ratio, _attackSecs, _releaseSecs, _makeupGainDb);

        private readonly AudioGraphNode _node;
        private float _thresholdDb;
        private float _ratio;
        private float _attackSecs;
        private float _releaseSecs;
        private float _makeupGainDb;

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

#nullable enable

namespace Lib.io.audio
{
    /// <summary>
    /// Adds a reverb insert to the audio flowing through it, so everything wired into <see cref="Input"/> shares
    /// the effect (and the <see cref="Volume"/> fold, like a [CombineAudio]). The effect itself isn't available
    /// at the moment: the routing [AudioBus] realises this node as its own bus and the audio passes through it
    /// unchanged. The inputs stay, so existing graphs keep their values for when it returns.
    /// </summary>
    [Guid("f2b43ea0-1e42-4c8a-9f31-0ab1cd2e0400")]
    internal sealed class AudioReverb : Instance<AudioReverb>, IStatusProvider
    {
        [Output(Guid = "f2b43ea0-0001-4c8a-9f31-0ab1cd2e0400")]
        public readonly Slot<AudioGraphNode> Result = new();

        public AudioReverb()
        {
            _node = new AudioGraphNode(this, Input)
                        {
                            FxInsert = new AudioGraphNode.AudioFxInsert
                                           {
                                               Apply = _ => { },
                                               UpdateParams = _ => { },
                                               Remove = _ => { },
                                           }
                        };
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.Gain = Volume.GetValue(context);
            _node.Update(context);
        }

        IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => IStatusProvider.StatusLevel.Warning;

        string IStatusProvider.GetStatusMessage() => "This effect isn't available at the moment; the audio passes through unchanged.";

        private readonly AudioGraphNode _node;

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

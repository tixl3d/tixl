#nullable enable

namespace Lib.io.audio
{
    /// <summary>
    /// Adds a compressor insert to the audio flowing through it, so everything wired into <see cref="Input"/> shares
    /// the effect (and the <see cref="Volume"/> fold, like a [CombineAudio]). The effect itself isn't available
    /// at the moment: the routing [AudioBus] realises this node as its own bus and the audio passes through it
    /// unchanged. The inputs stay, so existing graphs keep their values for when it returns.
    /// </summary>
    [Guid("b8d65fc0-1e42-4c8a-9f31-0ab1cd2e0600")]
    internal sealed class AudioCompressor : Instance<AudioCompressor>, IStatusProvider
    {
        [Output(Guid = "b8d65fc0-0001-4c8a-9f31-0ab1cd2e0600")]
        public readonly Slot<AudioGraphNode> Result = new();

        public AudioCompressor()
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

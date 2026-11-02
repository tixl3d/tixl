#nullable enable

namespace Lib.io.audio
{
    /// <summary>
    /// Adds an echo/delay insert to the audio flowing through it, so everything wired into <see cref="Input"/> shares
    /// the effect (and the <see cref="Volume"/> fold, like a [CombineAudio]). The effect itself isn't available
    /// at the moment: the routing [AudioBus] realises this node as its own bus and the audio passes through it
    /// unchanged. The inputs stay, so existing graphs keep their values for when it returns.
    /// </summary>
    [Guid("a7c54fb0-1e42-4c8a-9f31-0ab1cd2e0500")]
    internal sealed class AudioEcho : Instance<AudioEcho>, IStatusProvider
    {
        [Output(Guid = "a7c54fb0-0001-4c8a-9f31-0ab1cd2e0500")]
        public readonly Slot<AudioGraphNode> Result = new();

        public AudioEcho()
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

        [Input(Guid = "a7c54fb0-0002-4c8a-9f31-0ab1cd2e0500")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "a7c54fb0-0003-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "a7c54fb0-0004-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Mix = new();

        [Input(Guid = "a7c54fb0-0005-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Delay = new();

        [Input(Guid = "a7c54fb0-0006-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<float> Feedback = new();

        [Input(Guid = "a7c54fb0-0007-4c8a-9f31-0ab1cd2e0500")]
        public readonly InputSlot<bool> PingPong = new();
    }
}

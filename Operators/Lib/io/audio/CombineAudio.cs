#nullable enable

namespace Lib.io.audio
{
    /// <summary>
    /// Combines audio sources into a single audio reference and applies a <see cref="Volume"/> that folds into
    /// every source it contains. Wire audio sources (or nested combines) into <see cref="Input"/> and route the
    /// output into an audio bus. Part of the audio processing graph (the AudioReference wire type).
    /// </summary>
    [Guid("a5d10c30-1e42-4c8a-9f31-0ab1cd2e0100")]
    internal sealed class CombineAudio : Instance<CombineAudio>
    {
        [Output(Guid = "a5d10c30-0001-4c8a-9f31-0ab1cd2e0100")]
        public readonly Slot<AudioGraphNode> Result = new();

        public CombineAudio()
        {
            _node = new AudioGraphNode(this, Input);
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.Gain = Volume.GetValue(context); // folded into descendants by AudioGraphNode.Collect
            _node.Update(context);
        }

        private readonly AudioGraphNode _node;

        [Input(Guid = "a5d10c30-0002-4c8a-9f31-0ab1cd2e0100")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "a5d10c30-0003-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<float> Volume = new();
    }
}

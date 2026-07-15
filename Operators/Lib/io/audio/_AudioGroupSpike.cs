#nullable enable

namespace Lib.io.audio
{
    /// <summary>
    /// THROWAWAY SPIKE (2a) — an audio-graph combinator. Folds its input <see cref="AudioGraphNode"/>s into
    /// one node so a [_AudioRootSpike] collects sources recursively *through* it — proving the reference graph
    /// is a real tree, not a flat depth-1 collection. No audio yet. Delete after 2b.
    /// </summary>
    [Guid("e2b0c1d8-5e42-4c8a-9f31-0ab1cd2ef200")]
    internal sealed class _AudioGroupSpike : Instance<_AudioGroupSpike>
    {
        [Output(Guid = "e2b0c1d8-0001-4c8a-9f31-0ab1cd2ef200")]
        public readonly Slot<AudioGraphNode> Result = new();

        public _AudioGroupSpike()
        {
            _node = new AudioGraphNode(this, Input);
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.Update(context);
        }

        private readonly AudioGraphNode _node;

        [Input(Guid = "e2b0c1d8-0002-4c8a-9f31-0ab1cd2ef200")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();
    }
}

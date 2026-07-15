#nullable enable

namespace Lib.io.audio
{
    /// <summary>
    /// THROWAWAY SPIKE (2a) — a leaf audio-graph source. Emits an <see cref="AudioGraphNode"/> carrying a
    /// Label so a downstream [_AudioRootSpike] can prove it collected this source recursively, off the
    /// per-frame update path. No audio — this proves structural collection only. Delete after 2b.
    /// </summary>
    [Guid("e2b0c1d8-5e42-4c8a-9f31-0ab1cd2ef100")]
    internal sealed class _AudioSourceSpike : Instance<_AudioSourceSpike>
    {
        [Output(Guid = "e2b0c1d8-0001-4c8a-9f31-0ab1cd2ef100")]
        public readonly Slot<AudioGraphNode> Result = new();

        public _AudioSourceSpike()
        {
            _node = new AudioGraphNode(this);
            Result.Value = _node;
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _node.SourceLabel = Label.GetValue(context);
            _node.Update(context);
        }

        private readonly AudioGraphNode _node;

        [Input(Guid = "e2b0c1d8-0002-4c8a-9f31-0ab1cd2ef100")]
        public readonly InputSlot<string> Label = new();
    }
}

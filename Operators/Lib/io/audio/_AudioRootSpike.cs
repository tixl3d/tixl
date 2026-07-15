#nullable enable

namespace Lib.io.audio
{
    /// <summary>
    /// THROWAWAY SPIKE (2a) — the audio-graph root. Collects every leaf source reachable through its input
    /// (recursively, through [_AudioGroupSpike]s) off the per-frame dirty path, and logs the set when it
    /// changes. Proves R1: structural collection driven by the root, independent of the sources' own
    /// evaluation. No BASS yet — 2b feeds the collected sources into the proven routing. Wire Result into
    /// your render chain (or pin it) so it evaluates. Delete after 2b.
    /// </summary>
    [Guid("e2b0c1d8-5e42-4c8a-9f31-0ab1cd2ef300")]
    internal sealed class _AudioRootSpike : Instance<_AudioRootSpike>
    {
        [Output(Guid = "e2b0c1d8-0001-4c8a-9f31-0ab1cd2ef300", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
        public readonly Slot<Command> Result = new();

        public _AudioRootSpike()
        {
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            _leaves.Clear();

            var inputs = Input.GetCollectedTypedInputs(true);
            for (var i = 0; i < inputs.Count; i++)
            {
                var node = inputs[i].GetValue(context);
                node?.CollectLeafSources(_leaves);
            }

            Input.DirtyFlag.Clear();

            // Log only when the collected set changes, so the console stays readable each frame.
            var signature = _leaves.Count == 0 ? "none" : string.Join(", ", _leaves);
            if (signature == _lastSignature)
                return;

            _lastSignature = signature;
            Log.Debug($"[_AudioRootSpike] Collected {_leaves.Count} source(s): {signature}", this);
        }

        private readonly List<AudioGraphNode> _leaves = new();
        private string? _lastSignature;

        [Input(Guid = "e2b0c1d8-0002-4c8a-9f31-0ab1cd2ef300")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();
    }
}

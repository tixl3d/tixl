#nullable enable
using T3.Core.Audio;

namespace Lib.io.audio
{
    /// <summary>
    /// A level-meter tap for the audio graph: passes the wired sources through unchanged and outputs their
    /// combined last-frame <see cref="Level"/> as a float for reactive control (e.g. driving a
    /// [DuckAudioLevel] or visuals). Insert it between sources and an [AudioBus], or branch a source into it.
    ///
    /// The measured level trails the audible signal by roughly a frame — inherent to metering and fine for
    /// ducking/reactive use.
    /// </summary>
    [Guid("d4f21c80-1e42-4c8a-9f31-0ab1cd2e0200")]
    internal sealed class AudioLevel : Instance<AudioLevel>
    {
        [Output(Guid = "d4f21c80-0001-4c8a-9f31-0ab1cd2e0200")]
        public readonly Slot<AudioGraphNode> Result = new();

        [Output(Guid = "d4f21c80-0002-4c8a-9f31-0ab1cd2e0200", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
        public readonly Slot<float> Level = new();

        public AudioLevel()
        {
            _node = new AudioGraphNode(this, Input);
            _node.DeclareAnalysisTap();
            Result.Value = _node;
            Result.UpdateAction += UpdatePassThrough;
            Level.UpdateAction += UpdateLevel;
        }

        private void UpdatePassThrough(EvaluationContext context)
        {
            _node.Update(context);
        }

        private void UpdateLevel(EvaluationContext context)
        {
            _node.Update(context);

            // Inline (source → AudioLevel → bus) this meters the realised bus, post per-source gains.
            // As a side branch nothing is realised, so it meters the leaf sources directly.
            var maxLevel = 0f;
            if (_node.RealisedSubmixes.Count > 0)
            {
                foreach (var bus in _node.RealisedSubmixes)
                    maxLevel = Math.Max(maxLevel, AudioMixerManager.GetLevel(bus));
            }
            else
            {
                _collected.Clear();
                _node.Collect(_collected, 1f);
                for (var i = 0; i < _collected.Count; i++)
                    maxLevel = Math.Max(maxLevel, AudioMixerManager.GetLevel(_collected[i].Leaf.SourceChannel) * _collected[i].Gain);
            }

            Level.Value = Math.Min(maxLevel, 1f);
        }

        private readonly AudioGraphNode _node;
        private readonly List<AudioGraphNode.CollectedSource> _collected = new();

        [Input(Guid = "d4f21c80-0003-4c8a-9f31-0ab1cd2e0200")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();
    }
}

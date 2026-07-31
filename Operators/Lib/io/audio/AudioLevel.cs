#nullable enable
using ManagedBass.Mix;

namespace Lib.io.audio
{
    /// <summary>
    /// A level-meter tap for the audio graph: passes the wired sources through unchanged and outputs their
    /// combined last-frame <see cref="Level"/> as a float for reactive control (e.g. driving a [Duck] or
    /// visuals). Insert it between sources and an [AudioBus], or branch a source into it.
    ///
    /// Metering reads the level BASS measured when the routing mixer last pulled the channel, so it is one
    /// frame behind the audible signal — inherent to metering and fine for ducking/reactive use.
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

            _collected.Clear();
            _node.Collect(_collected, 1f);

            // A channel only meters while some mixer is pulling it (buffered); unrouted or unbuffered
            // channels fail the call and are skipped. Gain is folded in so the value approximates what's
            // audible. The buffer-inspecting Ex variant is required: the plain ChannelGetLevel only sees
            // data taken since the last call and mostly reads 0 between the device's coarse pulls.
            var maxLevel = 0f;
            for (var i = 0; i < _collected.Count; i++)
            {
                if (BassMix.ChannelGetLevel(_collected[i].Leaf.SourceChannel, _levelPair, 0.05f, 0) == -1)
                    continue;

                var level = Math.Max(_levelPair[0], _levelPair[1]) * _collected[i].Gain;
                if (level > maxLevel)
                    maxLevel = level;
            }

            Level.Value = Math.Min(maxLevel, 1f);
        }

        private readonly AudioGraphNode _node;
        private readonly List<AudioGraphNode.CollectedSource> _collected = new();
        private readonly float[] _levelPair = new float[2];

        [Input(Guid = "d4f21c80-0003-4c8a-9f31-0ab1cd2e0200")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();
    }
}

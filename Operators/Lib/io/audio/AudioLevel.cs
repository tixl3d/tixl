#nullable enable
using ManagedBass.Mix;

namespace Lib.io.audio
{
    /// <summary>
    /// A level-meter tap for the audio graph: passes the wired sources through unchanged and outputs their
    /// combined last-frame <see cref="Level"/> as a float for reactive control (e.g. driving a
    /// [DuckAudioLevel] or visuals). Insert it between sources and an [AudioBus], or branch a source into it.
    ///
    /// The tap declares an (effect-less) insert, so the routing bus realises it as its own submix — that's
    /// what makes metering reliable for every source type, including engine-owned [AudioClip] channels that
    /// can't be metered individually. The measured level trails the audible signal by roughly a frame —
    /// inherent to metering and fine for ducking/reactive use.
    /// </summary>
    [Guid("d4f21c80-1e42-4c8a-9f31-0ab1cd2e0200")]
    internal sealed class AudioLevel : Instance<AudioLevel>, IStatusProvider
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

            // Inline usage (source → AudioLevel → bus): meter the realised submix(es) — post per-source
            // gains, and reliable for every source type including engine-owned [AudioClip] channels.
            // The buffer-inspecting Ex variant is required: the plain ChannelGetLevel only sees data taken
            // since its last call and mostly reads 0 between the device's coarse pulls.
            var maxLevel = 0f;
            if (_node.RealisedSubmixes.Count > 0)
            {
                foreach (var submix in _node.RealisedSubmixes)
                {
                    if (BassMix.ChannelGetLevel(submix, _levelPair, 0.05f, 0) == -1)
                        continue;

                    var level = Math.Max(_levelPair[0], _levelPair[1]);
                    if (level > maxLevel)
                        maxLevel = level;
                }
            }
            else
            {
                // Side-branch usage (tap not wired into a bus): no submix is realised, so meter the leaf
                // channels directly. Works for buffered graph-owned sources (tones etc.); engine-owned
                // [AudioClip] channels are un-buffered and can't be metered this way — wire the tap inline
                // for those.
                _collected.Clear();
                _node.Collect(_collected, 1f);
                for (var i = 0; i < _collected.Count; i++)
                {
                    if (BassMix.ChannelGetLevel(_collected[i].Leaf.SourceChannel, _levelPair, 0.05f, 0) == -1)
                        continue;

                    var level = Math.Max(_levelPair[0], _levelPair[1]) * _collected[i].Gain;
                    if (level > maxLevel)
                        maxLevel = level;
                }
            }

            Level.Value = Math.Min(maxLevel, 1f);
        }

        // A side-branch tap can meter generator sources, but engine-owned channels ([AudioClip], video audio)
        // are un-buffered and silently read 0 there — a trap that cost real debugging time; warn instead.
        IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
            _node.RealisedSubmixes.Count == 0 && _node.HasExternallyManagedLeaf()
                ? IStatusProvider.StatusLevel.Warning
                : IStatusProvider.StatusLevel.Success;

        string IStatusProvider.GetStatusMessage() =>
            "As a side branch this tap can't meter timeline clips or video audio — their Level reads 0.\n"
            + "Wire it inline: source → AudioLevel → bus (directly or through combines/effects).";

        private readonly AudioGraphNode _node;
        private readonly List<AudioGraphNode.CollectedSource> _collected = new();
        private readonly float[] _levelPair = new float[2];

        [Input(Guid = "d4f21c80-0003-4c8a-9f31-0ab1cd2e0200")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();
    }
}

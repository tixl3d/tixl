#nullable enable
using T3.Core.Audio;

namespace Lib.io.audio
{
    /// <summary>
    /// Combines audio sources into a single audio reference and applies a <see cref="Volume"/> that folds into
    /// every source it contains. Wire audio sources (or nested combines) into <see cref="Input"/> and route the
    /// output into an audio bus. With <see cref="AutoCollectClips"/> on, unwired [AudioClip]s of the same
    /// composition join the group implicitly — they inherit the group volume, taps and FX like wired sources.
    /// Part of the audio processing graph (the AudioReference wire type).
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
            var autoCollect = AutoCollectClips.GetValue(context);

            // While auto-collecting there may be no wired inputs at all — nothing would mark this output
            // dirty, starving the collection and the clips' playback heartbeat. Re-evaluate per frame then;
            // purely wired combines stay demand-driven.
            Result.DirtyFlag.Trigger = autoCollect ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;

            _node.Gain = Volume.GetValue(context); // folded into descendants by AudioGraphNode.Collect
            _node.Update(context);

            // Auto-collected clips join this node's inputs after the wired ones — the node refresh above is
            // frame-deduped, so within a frame this append happens exactly once. Evaluating the clips'
            // reference outputs doubles as their playback heartbeat. Only one op per composition should
            // auto-collect (two would contend for the same channels).
            if (!autoCollect)
                return;

            var looseOutputs = _looseClipScanner.GetLooseClipOutputs(Parent);
            for (var i = 0; i < looseOutputs.Count; i++)
            {
                var node = looseOutputs[i].GetValue(context);
                if (node != null)
                    _node.InputNodes.Add(node);
            }
        }

        private readonly AudioGraphNode _node;
        private readonly LooseAudioClipScanner _looseClipScanner = new();

        [Input(Guid = "a5d10c30-0002-4c8a-9f31-0ab1cd2e0100")]
        public readonly MultiInputSlot<AudioGraphNode> Input = new();

        [Input(Guid = "a5d10c30-0003-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "a5d10c30-0004-4c8a-9f31-0ab1cd2e0100")]
        public readonly InputSlot<bool> AutoCollectClips = new();
    }
}

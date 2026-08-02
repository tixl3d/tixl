#nullable enable
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace T3.Core.Audio;

/// <summary>
/// Implemented by audio-graph <b>source</b> operators (ops that emit an <see cref="AudioGraphNode"/> leaf with a
/// playable BASS channel). Lets <see cref="AudioGraphCollector"/> realise a <i>loose</i> source — one whose
/// output isn't wired into any bus/combinator — from static input values, without an <c>EvaluationContext</c>.
/// Mirrors how <see cref="IAudioClipProvider"/> exposes clips to <see cref="AudioClipCollector"/>.
/// </summary>
public interface IAudioSource
{
    /// <summary>The op's AudioGraphNode output. Its <c>Id</c> detects whether the source is wired; its <c>Value</c> is the node.</summary>
    Slot<AudioGraphNode> AudioReferenceOutput { get; }

    /// <summary>Ensures the source's channel exists — created from <b>static</b> input values — and updates its node. Context-free.</summary>
    void EnsureChannelFromStaticInputs();
}

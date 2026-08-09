#nullable enable
using System.Collections.Generic;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Audio;

/// <summary>
/// Finds the audio sources of a composition whose <c>AudioReference</c> output isn't wired anywhere —
/// the set an auto-collecting op ([AudioBus] / [CombineAudio]) routes implicitly. That covers timeline
/// [AudioClip]s and any op exposing a graph source, so a [VideoClip]'s or [PlayVideo]'s sound joins the
/// group on the same terms. Any outgoing connection on the reference output excludes a source: an explicit
/// wire is the single source of truth for its routing. Ops that deliberately stay graph-only (the tone
/// generator) implement neither interface and are never swept up.
/// The scan is rebuilt only when the composition's connection structure changes.
/// Sibling of <see cref="AudioClipCollector"/> / <see cref="AudioGraphCollector"/> — one instance per
/// auto-collecting op.
/// </summary>
public sealed class LooseAudioClipScanner
{
    public IReadOnlyList<Slot<AudioGraphNode>> GetLooseClipOutputs(Instance? composition)
    {
        if (composition == null)
        {
            _looseClipOutputs.Clear();
            _cachedComposition = null;
            return _looseClipOutputs;
        }

        if (ReferenceEquals(_cachedComposition, composition) && _cachedStructureVersion == composition.Symbol.VersionCounter)
            return _looseClipOutputs;

        _cachedComposition = composition;
        _cachedStructureVersion = composition.Symbol.VersionCounter;
        _looseClipOutputs.Clear();

        var connections = composition.Symbol.Connections;
        foreach (var child in composition.Children.Values)
        {
            if (child is not (IAudioClipProvider or IAudioSource))
                continue;

            Slot<AudioGraphNode>? referenceOutput = null;
            foreach (var output in child.Outputs)
            {
                if (output is Slot<AudioGraphNode> slot)
                {
                    referenceOutput = slot;
                    break;
                }
            }

            if (referenceOutput == null)
                continue;

            var wired = false;
            for (var i = 0; i < connections.Count; i++)
            {
                var c = connections[i];
                if (c.SourceParentOrChildId == child.SymbolChildId && c.SourceSlotId == referenceOutput.Id)
                {
                    wired = true;
                    break;
                }
            }

            if (!wired)
                _looseClipOutputs.Add(referenceOutput);
        }

        return _looseClipOutputs;
    }

    // Slot references stay valid until the structure version changes; hot reload recreates the owning op.
    private Instance? _cachedComposition;
    private int _cachedStructureVersion = -1;
    private readonly List<Slot<AudioGraphNode>> _looseClipOutputs = new();
}

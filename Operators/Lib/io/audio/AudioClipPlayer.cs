#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Audio;

namespace Lib.io.audio;

/// <summary>
/// Registers timeline <c>[AudioClip]</c>s for playback. Collects clips two ways: wired into
/// <see cref="AudioClips"/>, and — with <see cref="AutoCollect"/> on — every sibling <c>[AudioClip]</c>
/// in the same composition (deduped). Each active clip (playhead inside its TimeRange) is registered with
/// the <see cref="AudioEngine"/>. Unlike the editor's AutoPlay registrar, this op is evaluated in the
/// graph, so it also drives nested compositions and <b>exported</b> playback. Mirrors <c>[VideoClipPlayer]</c>
/// on the audio side — but it registers instead of compositing, so it's a single op, not a symbol graph.
/// </summary>
[Guid("3f78a8a8-2c0b-4486-813e-f052e01e4248")]
internal sealed class AudioClipPlayer : Instance<AudioClipPlayer>
{
    [Output(Guid = "59cf1d38-1b57-4fd2-b4ed-1b9854bd4537", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Command> Output = new();

    public AudioClipPlayer()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var timeInBars = context.LocalTime;
        var timeInSecs = context.Playback.SecondsFromBars(timeInBars);

        _seenChildIds.Clear();

        // Wired clips (the multi-input). Reached via each collected slot's Parent op.
        var clips = AudioClips.CollectedInputs;
        for (var i = 0; i < clips.Count; i++)
        {
            if (clips[i].Parent is IAudioClipProvider provider)
            {
                _seenChildIds.Add(clips[i].Parent!.SymbolChildId);
                Drive(provider, timeInBars, timeInSecs);
            }
        }

        // Auto-collected sibling [AudioClip]s in the same composition, deduped against the wired set.
        if (AutoCollect.GetValue(context) && Parent != null)
        {
            // Rebuild the sibling-clip list only on a Parent change (hot-reload → new instance) or a graph
            // structure change — not every frame. Avoids the per-frame Children.Values walk + locked
            // per-child lookup, which is a frame-drop source on large graphs.
            if (!ReferenceEquals(_cachedParent, Parent) || _cachedStructureVersion != Parent.Symbol.VersionCounter)
            {
                _cachedParent = Parent;
                _cachedStructureVersion = Parent.Symbol.VersionCounter;
                _autoCollected.Clear();
                foreach (var child in Parent.Children.Values)
                {
                    if (child is IAudioClipProvider)
                        _autoCollected.Add(child);
                }
            }

            for (var i = 0; i < _autoCollected.Count; i++)
            {
                var child = _autoCollected[i];
                if (_seenChildIds.Add(child.SymbolChildId) && child is IAudioClipProvider provider)
                    Drive(provider, timeInBars, timeInSecs);
            }
        }

        AudioClips.DirtyFlag.Clear();
    }

    // Mark every managed clip (so its status reads "played", even while it sits in a gap between
    // its in/out points), then register only the ones active at the current playhead.
    private static void Drive(IAudioClipProvider provider, double timeInBars, double timeInSecs)
    {
        provider.MarkManaged();
        AudioClipCollector.RegisterIfActive(provider, timeInBars, timeInSecs);
    }

    // Reused across frames (Update runs on the single graph-eval thread); cleared, not reallocated.
    private readonly HashSet<Guid> _seenChildIds = new();

    // AutoCollect scan cache — rebuilt only when Parent changes (reload) or the composition's
    // VersionCounter bumps (any edit), not every frame.
    private Instance? _cachedParent;
    private int _cachedStructureVersion = -1;
    private readonly List<Instance> _autoCollected = new();

    [Input(Guid = "98702b93-b904-4a19-8e58-90809aaa5c42")]
    public readonly MultiInputSlot<Command> AudioClips = new();

    [Input(Guid = "301f0b3a-9958-4510-9f6b-b418f99cd1e0")]
    public readonly InputSlot<bool> AutoCollect = new();
}

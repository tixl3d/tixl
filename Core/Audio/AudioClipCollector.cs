#nullable enable
using System.Collections.Generic;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Per-frame registrar for op-backed audio clips. Scans a composition's children for
/// <see cref="IAudioClipProvider"/>s with <c>AutoPlay</c> on and registers the ones whose
/// <c>TimeRange</c> contains the playhead with the <see cref="AudioEngine"/> — the
/// graph-independent "drop a clip and hear it" path that doesn't need an <c>[AudioClipPlayer]</c>.
/// </summary>
/// <remarks>
/// Registration is a heartbeat: <see cref="AudioEngine.UseSoundtrackClip"/> creates the BASS stream
/// on first call, seeks / pauses / volumes it via <c>SoundtrackClipStream</c>, and frees it the first
/// frame it isn't called again (stale eviction). So this method only declares "active now" — it never
/// starts, stops, seeks, or frees streams itself. Called from the editor playback update and the
/// player main loop.
/// </remarks>
public static class AudioClipCollector
{
    public static void RegisterAutoPlayClips(Instance? composition, double timeInBars, double timeInSecs)
    {
        if (composition == null)
            return;

        // Rebuild the cached provider list only when the composition instance changes (focus switch /
        // hot-reload → a new instance) or the graph structure changes (Symbol.StructureVersion). This
        // avoids the per-frame Children.Values walk + locked per-child lookup, which is a frame-drop
        // source on large graphs (InstanceChildren's own TODO flags it).
        if (!ReferenceEquals(_cachedComposition, composition) || _cachedStructureVersion != composition.Symbol.VersionCounter)
        {
            _cachedComposition = composition;
            _cachedStructureVersion = composition.Symbol.VersionCounter;
            _cachedProviders.Clear();
            foreach (var child in composition.Children.Values)
            {
                if (child is IAudioClipProvider provider)
                    _cachedProviders.Add(provider);
            }
        }

        // AutoPlay is a per-frame input value, so it's read each frame (not cached).
        for (var i = 0; i < _cachedProviders.Count; i++)
        {
            var provider = _cachedProviders[i];
            if (provider.AutoPlay)
                RegisterIfActive(provider, timeInBars, timeInSecs);
        }
    }

    private static Instance? _cachedComposition;
    private static int _cachedStructureVersion = -1;
    private static readonly List<IAudioClipProvider> _cachedProviders = new();

    /// <summary>
    /// Registers a single clip with the engine for this frame if the playhead is inside its TimeRange.
    /// Shared by the AutoPlay registrar and <c>[AudioClipPlayer]</c>. Does not mark the clip as managed —
    /// the player does that separately (for all clips it drives, active or not) to feed its status hint.
    /// </summary>
    public static void RegisterIfActive(IAudioClipProvider provider, double timeInBars, double timeInSecs)
    {
        var timeClip = provider.TimeClip;
        if (timeClip == null)
            return;

        // Exclusive end matches TimeClipSlot's own range test, so adjacent clips sharing a cut
        // boundary don't both register on that frame. End <= Start is the "no explicit end yet"
        // sentinel (e.g. a freshly created soundtrack before its file length is known) — such clips
        // play from Start so their stream loads, which lets the op size the clip to the content.
        var hasExplicitEnd = timeClip.TimeRange.End > timeClip.TimeRange.Start;
        if (timeInBars < timeClip.TimeRange.Start || (hasExplicitEnd && timeInBars >= timeClip.TimeRange.End))
            return;

        AudioEngine.UseSoundtrackClip(provider.GetResourceHandle(), timeInSecs);
    }
}

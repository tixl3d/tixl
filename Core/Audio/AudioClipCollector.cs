#nullable enable
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

        // PERF (deferred): this rescans Children every frame, and InstanceChildren.Values is a yield
        // iterator that allocates + does a locked per-child lookup (its class TODO flags it as a
        // frame-drop source on big graphs). The fix — cache the filtered provider list, rebuilt on an
        // atomic Symbol structure-version counter (none in Core yet) — is shared with VideoClip's
        // identical AutoCollect scan.
        foreach (var child in composition.Children.Values)
        {
            if (child is IAudioClipProvider provider && provider.AutoPlay)
                RegisterIfActive(provider, timeInBars, timeInSecs);
        }
    }

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
        // boundary don't both register on that frame.
        if (timeInBars < timeClip.TimeRange.Start || timeInBars >= timeClip.TimeRange.End)
            return;

        AudioEngine.UseSoundtrackClip(provider.GetResourceHandle(), timeInSecs);
    }
}

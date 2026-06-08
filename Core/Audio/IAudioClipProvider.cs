#nullable enable
using T3.Core.Animation;

namespace T3.Core.Audio;

/// <summary>
/// Implemented by the <c>[AudioClip]</c> operator so the audio-clip registrar
/// (<see cref="AudioClipCollector"/>) and a future <c>[AudioClipPlayer]</c> can drive it for
/// playback. Lives in Core — not the operator package — because the registrar runs editor- and
/// player-side while the op is in a dynamically-loaded package, the same reason
/// <see cref="AudioClipResourceHandle"/> and <see cref="T3.Core.Operator.Slots.ITimeClipProvider"/>
/// are Core types.
/// </summary>
public interface IAudioClipProvider
{
    /// <summary>Timeline placement (TimeRange / LayerIndex / SourceRange). Null before the clip's TimeClip exists.</summary>
    TimeClip? TimeClip { get; }

    /// <summary>When true the clip registers itself for playback with no <c>[AudioClipPlayer]</c> required.</summary>
    bool AutoPlay { get; }

    /// <summary>Full source-file duration in seconds; 0 until known (populated on first playback). Used to
    /// map the clip's <c>SourceRange</c> onto the waveform image so it isn't stretched.</summary>
    double SourceLengthInSeconds { get; }

    /// <summary>
    /// Syncs the clip's playback data from its current input values and returns the engine handle.
    /// Called once per frame by the registrar / player for clips that should sound this frame.
    /// </summary>
    AudioClipResourceHandle GetResourceHandle();

    /// <summary>Stamped each frame by whatever drives the clip, for a "not played by anything" status hint.</summary>
    void MarkManaged();
}

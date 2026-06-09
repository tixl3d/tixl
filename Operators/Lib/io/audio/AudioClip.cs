#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;

namespace Lib.io.audio;

/// <summary>
/// A timeline audio clip as an operator. Placed via its <see cref="TimeSlot"/> (so it gets all the
/// standard op-clip interactions — drag / trim / split / snap / delete), it plays through the
/// <see cref="AudioEngine"/> while the playhead is inside its <c>TimeRange</c>. With
/// <see cref="AutoPlay"/> on (default) the clip registers itself for playback with no
/// <c>[AudioClipPlayer]</c> required; otherwise a player drives it.
/// </summary>
/// <remarks>
/// Mirrors <c>[VideoClip]</c> on the audio side. Reuses the existing playback machinery: it owns a
/// <see cref="TimelineAudioClip"/> data carrier synced from its inputs, and the engine plays it via
/// <see cref="AudioClipResourceHandle"/> + <c>SoundtrackClipStream</c>. The op never starts/stops the
/// stream — registration (heartbeat) is the engine's start/stop signal (see <see cref="AudioClipCollector"/>).
/// </remarks>
[Guid("f0008b50-091d-4e9f-91eb-baa212acfa20")]
internal sealed class AudioClip : Instance<AudioClip>, IAudioClipProvider, IContentTimeClip, IStatusProvider, IDescriptiveFilename
{
    [Output(Guid = "5fb7a174-9ab2-4688-89a0-7fbcbf831dcf", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TimeClipSlot<Command> TimeSlot = new();

    public AudioClip()
    {
        TimeSlot.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Command passthrough so the op can sit in a command chain (e.g. wired into a player).
        Command.GetValue(context);

        // When the op is actually evaluated (wired / driven by a player), sync from the live context
        // so animated Volume / Mute take effect. The AutoPlay registrar covers the unwired case via
        // GetResourceHandle() reading static input values.
        SyncClip(context.Playback, Path.GetValue(context), Volume.GetValue(context), Mute.GetValue(context));

        TimeSlot.DirtyFlag.Clear();
    }

    TimeClip? IAudioClipProvider.TimeClip => TimeSlot.TimeClip;

    bool IAudioClipProvider.AutoPlay => AutoPlay.TypedInputValue.Value;

    double IAudioClipProvider.SourceLengthInSeconds => _clip.LengthInSeconds;

    AudioClipResourceHandle IAudioClipProvider.GetResourceHandle()
    {
        // Registrar path: no EvaluationContext, so read static input values. Animated inputs on an
        // unwired clip aren't evaluated — acceptable for v1.
        SyncClip(Playback.Current, Path.TypedInputValue.Value, Volume.TypedInputValue.Value, Mute.TypedInputValue.Value);
        return _handle ??= new AudioClipResourceHandle(_clip, this);
    }

    // Drives the timeline clip label (filename, or "RenamedName (file)") and the waveform body renderer.
    InputSlot<string> IDescriptiveFilename.SourcePathSlot => Path;

    void IAudioClipProvider.MarkManaged() => _lastManagedFrame = Playback.FrameCount;
    private bool IsManaged => Playback.FrameCount - _lastManagedFrame <= ManagedFrameSlack;
    private const int ManagedFrameSlack = 2;

    private void SyncClip(Playback? playback, string? path, float volume, bool mute)
    {
        _clip.AssetPath = path ?? string.Empty;
        _clip.Volume = volume;
        _clip.IsMuted = mute;

        var timeClip = TimeSlot.TimeClip;
        _clip.TimeRange = timeClip.TimeRange;
        _clip.LayerIndex = timeClip.LayerIndex;

        // Source trim: SourceRange is file-time in bars; the engine seeks in seconds → map via BPM.
        // Interim mapping — refine when start-handle source trim editing lands.
        if (playback != null)
            _clip.SourceOffsetSecs = playback.SecondsFromBars(timeClip.SourceRange.Start);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        if (string.IsNullOrEmpty(_clip.AssetPath))
            return IStatusProvider.StatusLevel.Warning;

        // AutoPlay clips drive themselves via the registrar; otherwise they rely on an [AudioClipPlayer].
        if (AutoPlay.TypedInputValue.Value || IsManaged)
            return IStatusProvider.StatusLevel.Success;

        return IStatusProvider.StatusLevel.Notice;
    }

    public string? GetStatusMessage()
    {
        if (string.IsNullOrEmpty(_clip.AssetPath))
            return "No audio file set.";

        if (AutoPlay.TypedInputValue.Value || IsManaged)
            return null;

        return "Not played — enable AutoPlay or add an [AudioClipPlayer] that collects this clip.";
    }

    private readonly TimelineAudioClip _clip = new() { IsMainSoundtrack = false };
    private AudioClipResourceHandle? _handle;
    private int _lastManagedFrame;

    [Input(Guid = "97948c5e-10d5-4e18-824e-aea17eb4eb2a")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "625951af-5f99-4171-b5b0-c97413121f56")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "06b8b927-ec47-4392-bb67-b9a140cc852b")]
    public readonly InputSlot<float> Volume = new();

    [Input(Guid = "4ad8fba6-6e13-4698-b3c6-bd5c808724ab")]
    public readonly InputSlot<bool> Mute = new();

    [Input(Guid = "260b61ae-7605-4f06-a3fb-793ae5a23646")]
    public readonly InputSlot<bool> AutoPlay = new();
}

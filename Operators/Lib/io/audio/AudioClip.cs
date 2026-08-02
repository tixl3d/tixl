#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;

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
    /// <summary>
    /// AudioReference for the audio-processing graph. Wire this into an [AudioBus] (directly or through a
    /// [CombineAudio]) to route the clip through the graph — the bus then owns its level and mixing, and its
    /// evaluation doubles as the playback heartbeat (no AutoPlay or player needed). Leave it unwired and the
    /// clip plays through the soundtrack path as before.
    /// </summary>
    [Output(Guid = "4c9e7a20-3f81-4d5a-b6e2-1a2b3c4d5e6f", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<AudioGraphNode> AudioReference = new();
    
    [Output(Guid = "5fb7a174-9ab2-4688-89a0-7fbcbf831dcf", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TimeClipSlot<Command> TimeSlot = new();



    public AudioClip()
    {
        TimeSlot.UpdateAction += Update;

        _node = new AudioGraphNode(this) { ExternallyManagedChannel = true };
        AudioReference.Value = _node;
        AudioReference.UpdateAction += UpdateAudioReference;
    }

    private void Update(EvaluationContext context)
    {
        // Command passthrough so the op can sit in a command chain (e.g. wired into a player).
        Command.GetValue(context);

        // When the op is actually evaluated (wired / driven by a player), sync from the live context
        // so animated Volume / Mute take effect. The AutoPlay registrar covers the unwired case via
        // GetResourceHandle() reading static input values.
        SyncClip(context.Playback, Path.GetValue(context), Volume.GetValue(context), Mute.GetValue(context),
                 Display.GetValue(context), Style.GetValue(context), Loop.GetValue(context));
        RefreshGraphRouting();

        TimeSlot.DirtyFlag.Clear();
    }

    // Off the render path: refreshes the AudioReference node so a bus can route this clip's live channel.
    private void UpdateAudioReference(EvaluationContext context)
    {
        // Evaluation by a bus is itself a playback heartbeat: wiring the clip into the graph is enough to
        // play it — no AutoPlay or player needed. RegisterIfActive syncs the clip + routing flag (via
        // GetResourceHandle) and the engine creates / position-syncs / stale-frees the stream as usual,
        // only while the playhead is inside the clip's TimeRange.
        AudioClipCollector.RegisterIfActive(this, context.Playback.TimeInBars, context.Playback.TimeInSecs);
        _lastManagedFrame = Playback.FrameCount;

        // The stream is created lazily on the first heartbeat, so the channel can be 0 for a frame — the
        // bus routes it on the next. Position stays engine-synced; the node carries membership + gain only.
        _node.SourceChannel = AudioEngine.TryGetSoundtrackChannel(_handle, out var ch) ? ch : 0;
        _node.Gain = Mute.GetValue(context) ? 0f : Volume.GetValue(context);
        _node.SourceLabel = string.IsNullOrEmpty(_clip.AssetPath)
                                ? null
                                : System.IO.Path.GetFileNameWithoutExtension(_clip.AssetPath);
        _node.Update(context);
    }

    // The clip hands its channel to the graph while its AudioReference is wired into a bus, or while a bus
    // auto-collects it (detected via the node's collected-frame stamp; 2-frame slack covers update order).
    private void RefreshGraphRouting() =>
        _clip.IsRoutedToGraph = IsAudioReferenceWired() || Playback.FrameCount - _node.LastCollectedFrame <= 2;

    private bool IsAudioReferenceWired()
    {
        var connections = Parent?.Symbol.Connections;
        if (connections == null)
            return false;

        for (var i = 0; i < connections.Count; i++)
        {
            var c = connections[i];
            if (c.SourceParentOrChildId == SymbolChildId && c.SourceSlotId == AudioReference.Id)
                return true;
        }

        return false;
    }

    TimeClip? IAudioClipProvider.TimeClip => TimeSlot.TimeClip;

    bool IAudioClipProvider.AutoPlay => AutoPlay.TypedInputValue.Value;

    double IAudioClipProvider.SourceLengthInSeconds => _clip.LengthInSeconds;

    AudioClipResourceHandle IAudioClipProvider.GetResourceHandle()
    {
        // Registrar path: no EvaluationContext, so read static input values. Animated inputs on an
        // unwired clip aren't evaluated
        SyncClip(Playback.Current, Path.TypedInputValue.Value, Volume.TypedInputValue.Value, Mute.TypedInputValue.Value,
                 Display.TypedInputValue.Value, Style.TypedInputValue.Value, Loop.TypedInputValue.Value);
        RefreshGraphRouting();
        return _handle ??= new AudioClipResourceHandle(_clip, this);
    }

    // Drives the timeline clip label (filename, or "RenamedName (file)") and the waveform body renderer.
    InputSlot<string> IDescriptiveFilename.SourcePathSlot => Path;

    void IAudioClipProvider.MarkManaged() => _lastManagedFrame = Playback.FrameCount;
    private bool IsManaged => Playback.FrameCount - _lastManagedFrame <= ManagedFrameSlack;
    private const int ManagedFrameSlack = 2;

    private void SyncClip(Playback? playback, string? path, float volume, bool mute, int display, int style, bool loop)
    {
        _clip.AssetPath = path ?? string.Empty;
        _clip.Volume = volume;
        _clip.IsMuted = mute;
        _clip.IsLooping = loop;

        // Display = BackgroundImage doubles as the main-soundtrack designation (timeline background,
        // FFT routing, export) — see CompositionSettings.TryGetMainSoundtrack's op-clip union.
        _clip.Display = (AudioClipDisplay)display;
        _clip.Style = (AudioClipStyle)style;
        _clip.IsMainSoundtrack = _clip.Display == AudioClipDisplay.BackgroundImage;

        var timeClip = TimeSlot.TimeClip;

        // A clip created without a known duration (e.g. via the settings window's Create Soundtrack)
        // carries the End <= Start sentinel. Once the engine knows the file's length, size the clip to
        // its content so it becomes a visible, draggable block.
        if (timeClip.TimeRange.End <= timeClip.TimeRange.Start && _clip.LengthInSeconds > 0 && playback != null)
        {
            var durationBars = (float)playback.BarsFromSeconds(_clip.LengthInSeconds);
            timeClip.TimeRange = new TimeRange(timeClip.TimeRange.Start, timeClip.TimeRange.Start + durationBars);
            timeClip.SourceRange = new TimeRange(0, durationBars);
        }

        // Background mode: the main soundtrack always spans its full source content — trims don't
        // apply, and the timeline hides its clip block. Switch Display back to Clip to edit placement.
        if (_clip.Display == AudioClipDisplay.BackgroundImage && _clip.LengthInSeconds > 0 && playback != null)
        {
            var durationBars = (float)playback.BarsFromSeconds(_clip.LengthInSeconds);
            var start = timeClip.TimeRange.Start;
            if (Math.Abs(timeClip.TimeRange.End - (start + durationBars)) > 0.0001f
                || Math.Abs(timeClip.SourceRange.Start) > 0.0001f
                || Math.Abs(timeClip.SourceRange.End - durationBars) > 0.0001f)
            {
                timeClip.TimeRange = new TimeRange(start, start + durationBars);
                timeClip.SourceRange = new TimeRange(0, durationBars);
            }
        }

        _clip.TimeRange = timeClip.TimeRange;
        _clip.LayerIndex = timeClip.LayerIndex;

        // Source trim: SourceRange is file-time in bars; the engine seeks in seconds → map via BPM.
        // The source window's duration doubles as the loop length when Loop is on.
        if (playback != null)
        {
            _clip.SourceOffsetSecs = playback.SecondsFromBars(timeClip.SourceRange.Start);
            _clip.SourceDurationSecs = Math.Max(0, playback.SecondsFromBars(timeClip.SourceRange.End - timeClip.SourceRange.Start));
        }

        _syncedClip = true;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        if (!_syncedClip)
            return IStatusProvider.StatusLevel.Success;
        
        if (string.IsNullOrEmpty(_clip.AssetPath))
            return IStatusProvider.StatusLevel.Warning;

        // AutoPlay clips drive themselves via the registrar; otherwise they rely on an [AudioClipPlayer].
        if (AutoPlay.TypedInputValue.Value || IsManaged)
            return IStatusProvider.StatusLevel.Success;

        return IStatusProvider.StatusLevel.Notice;
    }

    public string? GetStatusMessage()
    {
        if (!_syncedClip)
            return string.Empty;
        
        if (string.IsNullOrEmpty(_clip.AssetPath))
            return "No audio file set.";

        if (AutoPlay.TypedInputValue.Value || IsManaged)
            return null;

        return "Not played — enable AutoPlay or add an [AudioClipPlayer] that collects this clip.";
    }

    private readonly TimelineAudioClip _clip = new();
    private readonly AudioGraphNode _node;
    private AudioClipResourceHandle? _handle;
    private int _lastManagedFrame;
    private bool _syncedClip;

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

    [Input(Guid = "8f2e6b10-4c5d-4e8f-9a1b-2c3d4e5f6a70", MappedType = typeof(AudioClipDisplay))]
    public readonly InputSlot<int> Display = new();

    [Input(Guid = "9a3f7c20-5d6e-4f9a-8b2c-3d4e5f6a7b80", MappedType = typeof(AudioClipStyle))]
    public readonly InputSlot<int> Style = new();

    [Input(Guid = "6c5d9e40-7f8a-4b1c-8d2e-3f4a5b6c7d90")]
    public readonly InputSlot<bool> Loop = new();
}

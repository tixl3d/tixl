#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Serialization;

namespace T3.Core.Audio;

/// <summary>
/// A wrapper that provides access to file resources of <see cref="TimelineAudioClip"/> and their
/// filepath. This is then used by the <see cref="AudioEngine"/> to generate
/// <see cref="SoundtrackClipStream"/>s for playback.
/// </summary>
public sealed record AudioClipResourceHandle(TimelineAudioClip Clip, IResourceConsumer? Owner)
{
    public bool TryGetFileResource([NotNullWhen(true)] out FileResource? file)
    {
        if (string.IsNullOrEmpty(Clip.AssetPath))
        {
            file = null;
            LoadingAttemptFailed = false;
            return false;
        }

        if (FileResource.TryGetFileResource(Clip.AssetPath, Owner, out file))
        {
            LoadingAttemptFailed = false;
            return true;
        }

        file = null;
        LoadingAttemptFailed = true;
        return false;
    }

    /// <summary>
    /// Only applies a newly entered filepath if it can be loaded.
    /// Otherwise, keeps original value and return false.
    /// </summary>
    public bool TryToApplyFilePath(string newFilePath, Instance composition)
    {
        if (!AssetRegistry.TryResolveAddress(newFilePath, composition, out _, out _))
        {
            Clip.AssetPath = string.Empty;
            return false;
        }

        LoadingAttemptFailed = false;
        Clip.AssetPath = newFilePath;
        return true;
    }

    /// <summary>
    /// Keep flag to prevent multiple error messages
    /// </summary>
    public bool LoadingAttemptFailed;
}

/// <summary>
/// A single audio clip placed on a composition's timeline. Owned by the symbol's
/// <c>CompositionSettings.Playback.AudioClips</c> list and rendered directly in
/// <c>LayersArea</c> — no operator instance is required.
///
/// Audio plays at native rate; <see cref="TimeRange"/> is the timeline window in bars,
/// while <see cref="SourceOffsetSecs"/> and <see cref="SourceDurationSecs"/> describe
/// what part of the source file is audible (in seconds). BPM changes never pitch-shift
/// the audio — they only shift the bar-to-seconds mapping for the timeline.
/// </summary>
public sealed class TimelineAudioClip
{
    #region serialized attributes
    public Guid Id = Guid.NewGuid();
    public string? AssetPath;

    /// <summary>
    /// Where on the timeline the clip is audible, in bars. <see cref="TimeRange.End"/>
    /// less than or equal to <see cref="TimeRange.Start"/> means "no explicit end on
    /// the timeline; play until the source content runs out."
    /// </summary>
    public TimeRange TimeRange;

    /// <summary>
    /// Offset into the source file where playback starts, in seconds.
    /// </summary>
    public double SourceOffsetSecs;

    /// <summary>
    /// How much of the source file (starting at <see cref="SourceOffsetSecs"/>) is
    /// audible, in seconds. <c>0</c> means "until the end of the file."
    /// </summary>
    public double SourceDurationSecs;

    public int LayerIndex;
    public float Volume = 1.0f;

    /// <summary>
    /// Per-clip mute. Silences playback (the audio engine multiplies the channel volume
    /// by 0) and the clip body renders faded so the user can tell at a glance that the
    /// clip won't be heard.
    /// </summary>
    public bool IsMuted;

    /// <summary>
    /// Marks the project's "main soundtrack" clip. This single flag currently bundles three concerns:
    /// (1) the clip is drawn as the timeline-background waveform, (2) it gets FFT routing for
    /// audio-reactive effects, and (3) it's wired into the export pipeline. Only one clip per
    /// project should set this — see <c>AudioEngine.ProcessSoundtrackClips</c>'s
    /// <c>handledMainSoundtrack</c> guard.
    ///
    /// The overload is acknowledged technical debt and may be split in a future change.
    /// </summary>
    public bool IsMainSoundtrack;
    #endregion

    /// <summary>
    /// Initialized by <see cref="SoundtrackClipStream.TryLoadSoundtrackClip"/> after BASS reports it.
    /// </summary>
    public double LengthInSeconds;

    /// <summary>How an op-provided clip is drawn on the timeline: an ordinary clip block, or the
    /// full-timeline-width background image behind the tracks (what the main soundtrack renders as).
    /// Transient — the [AudioClip] op syncs it from its Display input each frame.</summary>
    public AudioClipDisplay Display = AudioClipDisplay.Clip;

    /// <summary>How the clip's audio content is visualised (clip body / background image).
    /// Transient — the [AudioClip] op syncs it from its Style input each frame.</summary>
    public AudioClipStyle Style = AudioClipStyle.Spectrum;

    /// <summary>When true the clip repeats its source window (<see cref="SourceOffsetSecs"/> +
    /// <see cref="SourceDurationSecs"/>) for as long as its <see cref="TimeRange"/> lasts.
    /// Transient — the [AudioClip] op syncs it from its Loop input each frame.</summary>
    public bool IsLooping;

    /// <summary>
    /// Transient (not serialized): set each frame by the [AudioClip] op when its <c>AudioReference</c> output is
    /// wired into the audio-processing graph. While true, the graph owns this clip's channel — it moves it out of
    /// the SoundtrackMixer into a bus submix and applies its own gain — so <see cref="SoundtrackClipStream"/>
    /// keeps position-syncing the channel but stops setting its volume and stops managing its mixer membership.
    /// The engine still creates and frees the stream; only routing + level hand off to the graph.
    /// </summary>
    public bool IsRoutedToGraph;

    /// <summary>
    /// Returns a resource handle for this clip, cached per owner. Per-frame callers must reuse the
    /// same handle so the engine's load/fail state (<see cref="AudioClipResourceHandle.LoadingAttemptFailed"/>)
    /// and stream-dictionary key stay stable — a fresh handle each frame would re-attempt a missing
    /// file and re-log the failure on every frame, and allocate in the hot path.
    /// </summary>
    public AudioClipResourceHandle GetResourceHandle(IResourceConsumer? owner)
    {
        if (_handle == null || !ReferenceEquals(_handle.Owner, owner))
            _handle = new AudioClipResourceHandle(this, owner);

        return _handle;
    }

    private AudioClipResourceHandle? _handle;

    /// <summary>
    /// Read-only mirror of a pre-rewrite project's per-clip <c>Bpm</c> field. Populated only by
    /// <see cref="TryFromJson"/> for back-compat with old JSON. Consumed by
    /// <c>CompositionSettings</c> to copy into <c>Playback.Bpm</c> once on load, then ignored.
    /// Not serialized. New code should not read or write this — BPM lives on Playback now.
    /// </summary>
    [JsonIgnore]
    internal float LegacyBpmForMigration;

    #region serialization
    internal static bool TryFromJson(JToken jToken, [NotNullWhen(true)] out TimelineAudioClip? newClip)
    {
        if (!JsonUtils.TryGetGuid(jToken[nameof(Id)], out var clipId))
        {
            Log.Warning("Missing or malformed id in TimelineAudioClip.");
            newClip = null;
            return false;
        }

        // Heal old data that was saved with Guid.Empty (the "Add soundtrack" button used
        // to construct clips without initializing Id). Silent — the assignment is fully
        // backward-compatible and gets persisted on the next save; logging once per clip
        // floods the console on project load when many legacy clips are present.
        if (clipId == Guid.Empty)
        {
            clipId = Guid.NewGuid();
        }

        // Back-compat: pre-rewrite projects use FilePath / StartTime / EndTime / Bpm /
        // DiscardAfterUse / IsSoundtrack. Read the new names first; fall through to the old.

        var assetPath = jToken[nameof(AssetPath)]?.Value<string>()
                        ?? jToken["FilePath"]?.Value<string>();

        TimeRange timeRange;
        var timeRangeToken = jToken[nameof(TimeRange)];
        if (timeRangeToken != null)
        {
            timeRange = new TimeRange(
                timeRangeToken["Start"]?.Value<float>() ?? 0f,
                timeRangeToken["End"]?.Value<float>() ?? 0f);
        }
        else
        {
            // Legacy: StartTime / EndTime (both in bars).
            var legacyStart = (float)(jToken["StartTime"]?.Value<double>() ?? 0);
            var legacyEnd = (float)(jToken["EndTime"]?.Value<double>() ?? 0);
            timeRange = new TimeRange(legacyStart, legacyEnd);
        }

        var legacyBpm = jToken["Bpm"]?.Value<float>() ?? 0f;

        var isMainSoundtrack = jToken[nameof(IsMainSoundtrack)]?.Value<bool>()
                               ?? jToken["IsSoundtrack"]?.Value<bool>()
                               ?? true;

        newClip = new TimelineAudioClip
                      {
                          Id = clipId,
                          AssetPath = assetPath,
                          TimeRange = timeRange,
                          SourceOffsetSecs = jToken[nameof(SourceOffsetSecs)]?.Value<double>() ?? 0,
                          SourceDurationSecs = jToken[nameof(SourceDurationSecs)]?.Value<double>() ?? 0,
                          LayerIndex = jToken[nameof(LayerIndex)]?.Value<int>() ?? 0,
                          Volume = jToken[nameof(Volume)]?.Value<float>() ?? 1f,
                          IsMuted = jToken[nameof(IsMuted)]?.Value<bool>() ?? false,
                          IsMainSoundtrack = isMainSoundtrack,
                          LegacyBpmForMigration = legacyBpm,
                      };

        return true;
    }

    internal void ToJson(JsonTextWriter writer)
    {
        if (string.IsNullOrEmpty(AssetPath))
            return;

        writer.WriteStartObject();
        {
            writer.WriteValue(nameof(Id), Id);
            writer.WriteObject(nameof(AssetPath), AssetPath);

            writer.WritePropertyName(nameof(TimeRange));
            writer.WriteStartObject();
            writer.WriteValue("Start", TimeRange.Start);
            writer.WriteValue("End", TimeRange.End);
            writer.WriteEndObject();

            if (SourceOffsetSecs > 0)
                writer.WriteValue(nameof(SourceOffsetSecs), SourceOffsetSecs);

            if (SourceDurationSecs > 0)
                writer.WriteValue(nameof(SourceDurationSecs), SourceDurationSecs);

            if (LayerIndex != 0)
                writer.WriteValue(nameof(LayerIndex), LayerIndex);

            if (Math.Abs(Volume - 1.0f) > 0.001f)
                writer.WriteValue(nameof(Volume), Volume);

            // Only emit the mute flag when set — keeps the JSON lean for the common case.
            if (IsMuted)
                writer.WriteValue(nameof(IsMuted), IsMuted);

            writer.WriteValue(nameof(IsMainSoundtrack), IsMainSoundtrack);
        }
        writer.WriteEndObject();
    }
    #endregion
}

/// <summary>How an audio clip is drawn on the timeline.</summary>
public enum AudioClipDisplay
{
    /// <summary>An ordinary timeline clip block.</summary>
    Clip = 0,

    /// <summary>The full-timeline-width background image behind the tracks — the main-soundtrack look.
    /// Also designates the clip as the project's main soundtrack (FFT / export).</summary>
    BackgroundImage = 1,
}

/// <summary>How an audio clip's content is visualised in its clip body or background image.</summary>
public enum AudioClipStyle
{
    /// <summary>FFT spectrogram (the only renderer available today).</summary>
    Spectrum = 0,

    /// <summary>Amplitude waveform (renderer pending).</summary>
    Waveform = 1,

    /// <summary>Filled volume-level envelope (renderer pending).</summary>
    VolumeLevel = 2,
}

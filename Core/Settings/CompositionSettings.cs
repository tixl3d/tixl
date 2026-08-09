#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Serialization;
using T3.Core.Resource;
using T3.Core.Video;

namespace T3.Core.Settings;

/// <summary>
/// Per-symbol project settings including playback configuration (soundtrack, BPM, audio input).
/// Stored in .t3 files. Found via breadcrumb traversal up the graph hierarchy.
/// </summary>
public sealed class CompositionSettings
{
    /// <summary>
    /// Provides default values when no project settings are active.
    /// </summary>
    public static CompositionSettings Defaults { get; } = new();

    /// <summary>
    /// Returns the active project settings from the current playback, falling back to defaults.
    /// This is the primary access path for project-level configuration.
    /// </summary>
    public static CompositionSettings Current => Animation.Playback.Current?.Settings ?? Defaults;

    public bool Enabled { get; set; }

    public PlaybackConfig Playback { get; init; } = new();
    public AudioMixConfig Audio { get; init; } = new();
    public ExportConfig Export { get; init; } = new();
    public ProxyConfig Proxy { get; init; } = new();

    public bool TryGetMainSoundtrack(IResourceConsumer? instance, [NotNullWhen(true)] out AudioClipResourceHandle? soundtrack)
    {
        if (Enabled)
        {
            foreach (var clip in Playback.AudioClips)
            {
                if (!clip.IsMainSoundtrack)
                    continue;

                soundtrack = clip.GetResourceHandle(instance);
                return true;
            }
        }

        // Union with op-provided clips: an [AudioClip] whose Display is BackgroundImage flags its clip as
        // main soundtrack. Keeps background waveform / FFT / export working for projects whose soundtrack
        // is an op rather than a settings-list entry (the settings list is migration-source-only).
        // Deliberately not gated on Enabled — the op is graph content and exists regardless of whether
        // the composition ever opened its project settings.
        if (instance is Operator.Instance settingsOwner)
        {
            foreach (var child in settingsOwner.Children.Values)
            {
                if (child is not IAudioClipProvider provider)
                    continue;

                var handle = provider.GetResourceHandle();
                if (handle.Clip.IsMainSoundtrack)
                {
                    soundtrack = handle;
                    return true;
                }
            }
        }

        soundtrack = null;
        return false;
    }

    #region Enums

    public enum AudioSources
    {
        ProjectSoundTrack,
        ExternalDevice,
    }

    public enum SyncModes
    {
        Timeline,
        Tapping,
    }

    #endregion

    #region Nested config classes

    public sealed class AudioMixConfig
    {
        public bool SoundtrackMute = false;
        public float SoundtrackVolume = 0.5f;
        public bool OperatorMute = false;
        public float OperatorVolume = 1;
        public float AudioResyncThreshold = 0.04f;
    }

    public sealed class ExportConfig
    {
        public WindowMode DefaultWindowMode = WindowMode.Fullscreen;
        public bool EnablePlaybackControlWithKeyboard = true;
    }

    /// <summary>Preview-proxy preferences for this project: how seek-proxies are transcoded and whether the engine
    /// substitutes them for source clips during preview. Per-project so the choice travels with the project (the
    /// proxy *files* are still local). Proxy codecs are all-intra/LGPL — never H.264/HEVC (libx264 = GPL).</summary>
    public sealed class ProxyConfig
    {
        public VideoExportCodec Format = VideoExportCodec.ProRes; // all-intra, no GPL
        public float Resolution = 0.5f;                           // fraction of the source resolution
        public bool UseForPreview = true;                         // open a clip's proxy when one exists (never while rendering)
    }

    public sealed class PlaybackConfig
    {
        public float Bpm = 120;
        public List<TimelineAudioClip> AudioClips { get; internal init; } = [];
        public AudioSources AudioSource;
        public SyncModes Syncing;

        /// <summary>
        /// True when the clock is driven by manual beat tapping rather than by the timeline. Both fields have to
        /// agree: <see cref="Syncing"/> can hold a stale <see cref="SyncModes.Tapping"/> because the project
        /// settings only expose it for <see cref="AudioSources.ExternalDevice"/>.
        /// </summary>
        public bool UsesBeatTapping => AudioSource == AudioSources.ExternalDevice && Syncing == SyncModes.Tapping;

        public string AudioInputDeviceName = string.Empty;
        public float AudioGainFactor = 1;
        public float AudioDecayFactor = 0.9f;

        public bool EnableAudioBeatLocking = true;
        public float BeatLockAudioOffsetSec;
    }

    #endregion

    #region Serialization

    internal void WriteToJson(JsonTextWriter writer)
    {
        var hasSettings = Enabled || Playback.AudioClips.Count > 0 || ProxyIsNonDefault();
        if (!hasSettings)
            return;

        writer.WritePropertyName("ProjectSettings");

        writer.WriteStartObject();
        {
            writer.WriteValue(nameof(Enabled), Enabled);

            // Playback section
            writer.WritePropertyName("Playback");
            writer.WriteStartObject();
            {
                writer.WriteValue(nameof(PlaybackConfig.Bpm), Playback.Bpm);
                writer.WriteValue(nameof(PlaybackConfig.AudioSource), Playback.AudioSource);
                writer.WriteValue(nameof(PlaybackConfig.Syncing), Playback.Syncing);
                writer.WriteValue(nameof(PlaybackConfig.AudioDecayFactor), Playback.AudioDecayFactor);
                writer.WriteValue(nameof(PlaybackConfig.AudioGainFactor), Playback.AudioGainFactor);
                writer.WriteObject(nameof(PlaybackConfig.AudioInputDeviceName), Playback.AudioInputDeviceName);
                writer.WriteObject(nameof(PlaybackConfig.EnableAudioBeatLocking), Playback.EnableAudioBeatLocking);
                writer.WriteObject(nameof(PlaybackConfig.BeatLockAudioOffsetSec), Playback.BeatLockAudioOffsetSec);

                if (Playback.AudioClips.Count != 0)
                {
                    writer.WritePropertyName("AudioClips");
                    writer.WriteStartArray();
                    foreach (var audioClip in Playback.AudioClips)
                    {
                        audioClip.ToJson(writer);
                    }
                    writer.WriteEndArray();
                }
            }
            writer.WriteEndObject();

            // Audio mix section
            writer.WritePropertyName("Audio");
            writer.WriteStartObject();
            {
                writer.WriteValue(nameof(AudioMixConfig.SoundtrackMute), Audio.SoundtrackMute);
                writer.WriteValue(nameof(AudioMixConfig.SoundtrackVolume), Audio.SoundtrackVolume);
                writer.WriteValue(nameof(AudioMixConfig.OperatorMute), Audio.OperatorMute);
                writer.WriteValue(nameof(AudioMixConfig.OperatorVolume), Audio.OperatorVolume);
                writer.WriteValue(nameof(AudioMixConfig.AudioResyncThreshold), Audio.AudioResyncThreshold);
            }
            writer.WriteEndObject();

            // Export section
            writer.WritePropertyName("Export");
            writer.WriteStartObject();
            {
                writer.WriteValue(nameof(ExportConfig.DefaultWindowMode), Export.DefaultWindowMode);
                writer.WriteValue(nameof(ExportConfig.EnablePlaybackControlWithKeyboard), Export.EnablePlaybackControlWithKeyboard);
            }
            writer.WriteEndObject();

            // Proxy section
            writer.WritePropertyName("Proxy");
            writer.WriteStartObject();
            {
                writer.WriteValue(nameof(ProxyConfig.Format), Proxy.Format);
                writer.WriteValue(nameof(ProxyConfig.Resolution), Proxy.Resolution);
                writer.WriteValue(nameof(ProxyConfig.UseForPreview), Proxy.UseForPreview);
            }
            writer.WriteEndObject();

        }

        writer.WriteEndObject();
    }

    private bool ProxyIsNonDefault()
        => Proxy.Format != Defaults.Proxy.Format
           || Proxy.Resolution != Defaults.Proxy.Resolution
           || Proxy.UseForPreview != Defaults.Proxy.UseForPreview;

    internal static CompositionSettings? ReadFromJson(JToken symbolToken)
    {
        // Try new format first, then fall back to legacy "PlaybackSettings" key
        var jSettingsToken = symbolToken["ProjectSettings"] ?? symbolToken["PlaybackSettings"];
        if (jSettingsToken == null)
            return null;

        var settingsToken = (JObject)jSettingsToken;

        // Check if this is the new nested format (has "Playback" sub-object)
        var playbackToken = settingsToken["Playback"] as JObject;
        if (playbackToken != null)
        {
            return ReadNewFormat(settingsToken, playbackToken);
        }

        // Legacy flat format — all fields at root level
        return ReadLegacyFormat(symbolToken, settingsToken);
    }

    private static CompositionSettings ReadNewFormat(JObject settingsToken, JObject playbackToken)
    {
        var clips = GetClips(playbackToken).ToList();
        var audioToken = settingsToken["Audio"] as JObject;
        var exportToken = settingsToken["Export"] as JObject;
        var proxyToken = settingsToken["Proxy"] as JObject;
        var debugToken = settingsToken["Debug"] as JObject; // legacy compat

        var settings = new CompositionSettings
                       {
                           Enabled = JsonUtils.ReadValueSafe(settingsToken, nameof(Enabled), false),
                           Playback = new PlaybackConfig
                                      {
                                          AudioClips = clips,
                                          Bpm = JsonUtils.ReadValueSafe(playbackToken, nameof(PlaybackConfig.Bpm), 120f),
                                          AudioSource = JsonUtils.ReadEnum<AudioSources>(playbackToken, nameof(PlaybackConfig.AudioSource)),
                                          Syncing = JsonUtils.ReadEnum<SyncModes>(playbackToken, nameof(PlaybackConfig.Syncing)),
                                          AudioDecayFactor = JsonUtils.ReadValueSafe(playbackToken, nameof(PlaybackConfig.AudioDecayFactor), 0.5f),
                                          AudioGainFactor = JsonUtils.ReadValueSafe(playbackToken, nameof(PlaybackConfig.AudioGainFactor), 1f),
                                          AudioInputDeviceName = JsonUtils.ReadValueSafe<string>(playbackToken, nameof(PlaybackConfig.AudioInputDeviceName)) ?? string.Empty,
                                          EnableAudioBeatLocking = JsonUtils.ReadValueSafe(playbackToken, nameof(PlaybackConfig.EnableAudioBeatLocking), false),
                                          BeatLockAudioOffsetSec = JsonUtils.ReadValueSafe(playbackToken, nameof(PlaybackConfig.BeatLockAudioOffsetSec), 0f),
                                      },
                           Audio = audioToken != null
                               ? new AudioMixConfig
                                 {
                                     SoundtrackMute = JsonUtils.ReadValueSafe(audioToken, nameof(AudioMixConfig.SoundtrackMute), Defaults.Audio.SoundtrackMute),
                                     SoundtrackVolume = JsonUtils.ReadValueSafe(audioToken, nameof(AudioMixConfig.SoundtrackVolume), Defaults.Audio.SoundtrackVolume),
                                     OperatorMute = JsonUtils.ReadValueSafe(audioToken, nameof(AudioMixConfig.OperatorMute), Defaults.Audio.OperatorMute),
                                     OperatorVolume = JsonUtils.ReadValueSafe(audioToken, nameof(AudioMixConfig.OperatorVolume), Defaults.Audio.OperatorVolume),
                                     AudioResyncThreshold = JsonUtils.ReadValueSafe(audioToken, nameof(AudioMixConfig.AudioResyncThreshold), Defaults.Audio.AudioResyncThreshold),
                                 }
                               : new AudioMixConfig(),
                           Export = exportToken != null
                               ? new ExportConfig
                                 {
                                     DefaultWindowMode = JsonUtils.ReadEnum<WindowMode>(exportToken, nameof(ExportConfig.DefaultWindowMode)),
                                     EnablePlaybackControlWithKeyboard = JsonUtils.ReadValueSafe(exportToken, nameof(ExportConfig.EnablePlaybackControlWithKeyboard), Defaults.Export.EnablePlaybackControlWithKeyboard),
                                 }
                               : new ExportConfig(),
                           Proxy = proxyToken != null
                               ? new ProxyConfig
                                 {
                                     // ReadEnum returns the enum's zero value when the key is missing, which isn't the
                                     // proxy default (ProRes) — so fall back explicitly when "Format" is absent.
                                     Format = proxyToken[nameof(ProxyConfig.Format)] != null
                                                  ? JsonUtils.ReadEnum<VideoExportCodec>(proxyToken, nameof(ProxyConfig.Format))
                                                  : Defaults.Proxy.Format,
                                     Resolution = JsonUtils.ReadValueSafe(proxyToken, nameof(ProxyConfig.Resolution), Defaults.Proxy.Resolution),
                                     UseForPreview = JsonUtils.ReadValueSafe(proxyToken, nameof(ProxyConfig.UseForPreview), Defaults.Proxy.UseForPreview),
                                 }
                               : new ProxyConfig(),
                       };

        return settings;
    }

    private static CompositionSettings ReadLegacyFormat(JToken symbolToken, JObject settingsToken)
    {
        var clips = GetClips(symbolToken).ToList(); // Support legacy json format with clips at symbol root

        var settings = new CompositionSettings
                       {
                           Enabled = JsonUtils.ReadValueSafe(settingsToken, nameof(Enabled), false),
                           Playback = new PlaybackConfig
                                      {
                                          AudioClips = clips,
                                          Bpm = JsonUtils.ReadValueSafe(settingsToken, "Bpm", 120f),
                                          AudioSource = JsonUtils.ReadEnum<AudioSources>(settingsToken, "AudioSource"),
                                          Syncing = JsonUtils.ReadEnum<SyncModes>(settingsToken, "Syncing"),
                                          AudioDecayFactor = JsonUtils.ReadValueSafe(settingsToken, "AudioDecayFactor", 0.5f),
                                          AudioGainFactor = JsonUtils.ReadValueSafe(settingsToken, "AudioGainFactor", 1f),
                                          AudioInputDeviceName = JsonUtils.ReadValueSafe<string>(settingsToken, "AudioInputDeviceName") ?? string.Empty,
                                          EnableAudioBeatLocking = JsonUtils.ReadValueSafe(settingsToken, "EnableAudioBeatLocking", false),
                                          BeatLockAudioOffsetSec = JsonUtils.ReadValueSafe(settingsToken, "BeatLockAudioOffsetSec", 0f),
                                      }
                       };

        settings.Playback.AudioClips.AddRange(GetClips(settingsToken)); // Support clips inside settings token

        // BPM migration: pre-rewrite projects stored BPM on the clip itself. The new model
        // keeps BPM only on Playback. If the legacy field was present on the loaded clip,
        // copy it once into Playback.Bpm.
        if (settings.Playback.Bpm == 0)
        {
            var soundtrack = settings.Playback.AudioClips.FirstOrDefault(c => c.IsMainSoundtrack);
            if (soundtrack != null && soundtrack.LegacyBpmForMigration > 0)
            {
                settings.Playback.Bpm = soundtrack.LegacyBpmForMigration;
                settings.Enabled = true;
            }
        }

        return settings;
    }

    private static IEnumerable<TimelineAudioClip> GetClips(JToken token)
    {
        var jClipsToken = token["AudioClips"];
        if (jClipsToken == null)
            yield break;

        var jAudioClipArray = (JArray)jClipsToken;
        foreach (var c in jAudioClipArray)
        {
            if (!TimelineAudioClip.TryFromJson(c, out var clip))
                continue;

            if (IsUnmanageableMissingClip(clip))
            {
                // Non-main clips have no UI to remove them; drop dead references on load so
                // they stop being re-registered (and re-logged) every frame. Persisted out on next save.
                Log.Warning($"Removing audio clip with missing file '{clip.AssetPath}' from composition settings.");
                continue;
            }

            yield return clip;
        }
    }

    /// <summary>
    /// True for a non-main audio clip whose absolute file path no longer exists. Restricted to
    /// absolute paths so package-relative clips (resolved later against a resource consumer) and
    /// the user-manageable main soundtrack are never dropped.
    /// </summary>
    private static bool IsUnmanageableMissingClip(TimelineAudioClip clip)
    {
        if (clip.IsMainSoundtrack || string.IsNullOrEmpty(clip.AssetPath))
            return false;

        return Path.IsPathRooted(clip.AssetPath) && !File.Exists(clip.AssetPath);
    }

    #endregion
}

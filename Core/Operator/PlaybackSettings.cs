#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Audio;
using T3.Serialization;
using T3.Core.Resource;

namespace T3.Core.Operator;

/// <summary>
/// Defines playback related settings for a symbol like its primary soundtrack or audio input device,
/// BPM rate and other settings.
/// </summary>
///  todo - treat AudioClips the same way as timeline clips - soundtracks might be a special case
public sealed class PlaybackSettings
{
    public bool Enabled { get; set; }
    public float Bpm = 120;
    public List<AudioClipDefinition> AudioClips { get; private init; } = [];
    public AudioSources AudioSource;
    public SyncModes Syncing;

    public string AudioInputDeviceName = string.Empty;
    public float AudioGainFactor = 1;
    public float AudioDecayFactor = 0.9f;
    
    public bool EnableAudioBeatLocking = false;
    public float BeatLockAudioOffsetSec;

    public bool TryGetMainSoundtrack(IResourceConsumer? instance, [NotNullWhen(true)] out AudioClipResourceHandle? soundtrack)
    {
        if (!Enabled)
        {
            soundtrack = null;
            return false;
        }
        
        foreach (var clip in AudioClips)
        {
            if (!clip.IsSoundtrack)
                continue;

            soundtrack = new AudioClipResourceHandle(clip, instance);
            return true;
        }

        soundtrack = null;
        return false;
    }

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

    internal void WriteToJson(JsonTextWriter writer)
    {
        var hasSettingsForClips = Enabled || AudioClips.Count > 0;
        if (!hasSettingsForClips)
            return;

        writer.WritePropertyName(nameof(PlaybackSettings));

        writer.WriteStartObject();
        {
            //writer.WriteEndArray();

            writer.WriteValue(nameof(Enabled), Enabled);
            writer.WriteValue(nameof(Bpm), Bpm);
            writer.WriteValue(nameof(AudioSource), AudioSource);
            writer.WriteValue(nameof(Syncing), Syncing);
            writer.WriteValue(nameof(AudioDecayFactor), AudioDecayFactor);
            writer.WriteValue(nameof(AudioGainFactor), AudioGainFactor);
            writer.WriteObject(nameof(AudioInputDeviceName), AudioInputDeviceName);
            writer.WriteObject(nameof(EnableAudioBeatLocking), EnableAudioBeatLocking);
            writer.WriteObject(nameof(BeatLockAudioOffsetSec), BeatLockAudioOffsetSec);

            // Write audio clips
            if (AudioClips.Count != 0)
            {
                writer.WritePropertyName("AudioClips");
                writer.WriteStartArray();
                foreach (var audioClip in AudioClips)
                {
                    audioClip.ToJson(writer);
                }

                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
    }

    internal static PlaybackSettings? ReadFromJson(JToken symbolToken)
    {
        var jSettingsToken = symbolToken[nameof(Symbol.PlaybackSettings)];
        if (jSettingsToken == null)
            return null;
        
        var settingsToken = (JObject)jSettingsToken;
        
        var clips = GetClips(symbolToken).ToList(); // Support legacy json format
        // if (clips.Count == 0)
        //     return null;

        var newSettings = new PlaybackSettings
                              {
                                  AudioClips = clips,
                                  Enabled = JsonUtils.ReadValueSafe(settingsToken, nameof(Enabled), false),
                                  Bpm = JsonUtils.ReadValueSafe(settingsToken, nameof(Bpm), 120f),
                                  AudioSource = JsonUtils.ReadEnum<AudioSources>(settingsToken, nameof(AudioSource)),
                                  Syncing = JsonUtils.ReadEnum<SyncModes>(settingsToken, nameof(Syncing)),
                                  AudioDecayFactor = JsonUtils.ReadValueSafe(settingsToken, nameof(AudioDecayFactor), 0.5f),
                                  AudioGainFactor = JsonUtils.ReadValueSafe(settingsToken, nameof(AudioGainFactor), 1f),
                                  AudioInputDeviceName = JsonUtils.ReadValueSafe<string>(settingsToken, nameof(AudioInputDeviceName))?? string.Empty,
                                  EnableAudioBeatLocking = JsonUtils.ReadValueSafe(settingsToken, nameof(EnableAudioBeatLocking), false),
                                  BeatLockAudioOffsetSec = JsonUtils.ReadValueSafe(settingsToken, nameof(BeatLockAudioOffsetSec), 0f),
                              };

        newSettings.AudioClips.AddRange(GetClips(settingsToken)); // Support correct format

        if (newSettings.Bpm == 0)
        {
            var soundtrack = newSettings.AudioClips.FirstOrDefault(c => c.IsSoundtrack);
            if (soundtrack != null)
            {
                newSettings.Bpm = soundtrack.Bpm;
                newSettings.Enabled = true;
            }
        }

        return newSettings;
    }

    private static IEnumerable<AudioClipDefinition> GetClips(JToken settingsToken)
    {
        var jClipsToken = settingsToken[nameof(Symbol.PlaybackSettings.AudioClips)];
        if(jClipsToken == null)
            yield break;
        
        var jAudioClipArray = (JArray)jClipsToken;
        foreach (var c in jAudioClipArray)
        {
            if (AudioClipDefinition.TryFromJson(c, out var clip))
            {
                yield return clip;
            }
        }
    }
}
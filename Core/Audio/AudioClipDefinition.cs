#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Serialization;

namespace T3.Core.Audio;

/// <summary>
/// A wrapper that provides access to file resources of <see cref="AudioClipDefinition"/> and their
/// and their filepath .This is then used by the <see cref="AudioEngine"/> to generate
/// <see cref="AudioClipStream"/>s for playback.
/// </summary>
public sealed record AudioClipResourceHandle(AudioClipDefinition Clip, IResourceConsumer? Owner)
{
    public bool TryGetFileResource([NotNullWhen(true)] out FileResource? file)
    {
        if (string.IsNullOrEmpty(Clip.FilePath))
        {
            file = null;
            LoadingAttemptFailed = false;
            return false;
        }

        if (FileResource.TryGetFileResource(Clip.FilePath, Owner, out file))
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
        if (!ResourceManager.TryResolveRelativePath(newFilePath, composition, out var absolutePath, out var resourceContainer))
        {
            Clip.FilePath = string.Empty;
            return false;
        }
        
        LoadingAttemptFailed = false;
        Clip.FilePath = newFilePath;
        return true;
    }

    /// <summary>
    /// Keep flag to prevent multiple error messages
    /// </summary>
    public bool LoadingAttemptFailed;
}

/// <summary>
/// Defines a single audio clip within a timeline.
/// </summary>
public sealed class AudioClipDefinition
{
    #region serialized attributes
    public Guid Id;
    public string? FilePath;
    public double StartTime;
    public double EndTime;
    public float Bpm = 120;
    public bool DiscardAfterUse = true;
    public bool IsSoundtrack;
    public float Volume = 1.0f;
    #endregion

    /// <summary>
    /// Is initialized after loading in <see cref="AudioClipStream"/>
    /// </summary>
    public double LengthInSeconds;

    #region serialization
    internal static bool TryFromJson(JToken jToken, [NotNullWhen(true)] out AudioClipDefinition? newAudioClip)
    {
        if (!JsonUtils.TryGetGuid(jToken[nameof(Id)], out var clipId))
        {
            Log.Warning("Missing or malformed id in AudioClip.");
            newAudioClip = null;
            return false;
        }
        
        newAudioClip = new AudioClipDefinition
                               {
                                   Id = clipId,
                                   FilePath = jToken[nameof(FilePath)]?.Value<string>(),
                                   StartTime = jToken[nameof(StartTime)]?.Value<double>() ?? 0,
                                   EndTime = jToken[nameof(EndTime)]?.Value<double>() ?? 0,
                                   Bpm = jToken[nameof(Bpm)]?.Value<float>() ?? 0,
                                   DiscardAfterUse = jToken[nameof(DiscardAfterUse)]?.Value<bool>() ?? true,
                                   IsSoundtrack = jToken[nameof(IsSoundtrack)]?.Value<bool>() ?? true,
                                   Volume = jToken[nameof(Volume)]?.Value<float>() ?? 1,
                               };
        
        return true;
    }

    internal void ToJson(JsonTextWriter writer)
    {
        if (string.IsNullOrEmpty(FilePath))
            return;
        
        //writer.WritePropertyName(Id.ToString());
        writer.WriteStartObject();
        {
            writer.WriteValue(nameof(Id), Id);
            writer.WriteValue(nameof(StartTime), StartTime);
            writer.WriteValue(nameof(EndTime), EndTime);
            writer.WriteValue(nameof(Bpm), Bpm);
            writer.WriteValue(nameof(DiscardAfterUse), DiscardAfterUse);
            writer.WriteValue(nameof(IsSoundtrack), IsSoundtrack);
            if (string.IsNullOrEmpty(FilePath))
            {
                Log.Warning("Empty file path in AudioClip.");
            }
            else
            {
                writer.WriteObject(nameof(FilePath), FilePath);
            }
            if (Math.Abs(Volume - 1.0f) > 0.001f)
                writer.WriteObject(nameof(Volume), Volume);
        }
        writer.WriteEndObject();
    }
    #endregion
}
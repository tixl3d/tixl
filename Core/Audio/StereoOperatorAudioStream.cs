#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ManagedBass;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Represents a stereo audio stream for operator-based playback.
/// </summary>
public sealed class StereoOperatorAudioStream : OperatorAudioStreamBase
{
    /// <summary>
    /// Private constructor to enforce factory method usage.
    /// </summary>
    private StereoOperatorAudioStream() { }

    /// <summary>
    /// Attempts to load a stereo audio stream from a file.
    /// </summary>
    /// <param name="filePath">The path to the audio file to load.</param>
    /// <param name="mixerHandle">The BASS mixer handle to add the stream to.</param>
    /// <param name="stream">When successful, contains the created stereo audio stream.</param>
    /// <returns><c>true</c> if the stream was successfully loaded; otherwise, <c>false</c>.</returns>
    internal static bool TryLoadStream(string filePath, int mixerHandle, [NotNullWhen(true)] out StereoOperatorAudioStream? stream)
    {
        stream = null;

        if (!TryLoadStreamCore(filePath, mixerHandle, BassFlags.Default,
            out var streamHandle, out var defaultFreq, out var info, out var duration))
        {
            return false;
        }

        stream = new StereoOperatorAudioStream
        {
            StreamHandle = streamHandle,
            MixerStreamHandle = mixerHandle,
            DefaultPlaybackFrequency = defaultFreq,
            Duration = duration,
            FilePath = filePath,
            IsPlaying = false,
            IsPaused = false,
            CachedChannels = info.Channels,
            CachedFrequency = info.Frequency,
            IsStaleStopped = true
        };

        Log.Gated.Audio($"[AudioPlayer] Loaded: '{Path.GetFileName(filePath)}' ({info.Channels}ch, {info.Frequency}Hz, {duration:F2}s)");
        return true;
    }

    /// <summary>
    /// Sets the stereo panning position of the audio stream.
    /// </summary>
    /// <param name="panning">The panning value (-1.0 = full left, 0.0 = center, 1.0 = full right).</param>
    internal void SetPanning(float panning)
    {
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Pan, panning);
    }
}

#nullable enable
using System;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// A BASS push stream carrying one video's decoded audio. Unlike <see cref="OperatorAudioStreamBase"/> and
/// <see cref="SoundtrackClipStream"/> — seekable file streams positioned by byte offset — a push stream has
/// no position of its own: it plays exactly what is fed and nothing else, so the feeder owns sync.
///
/// It deliberately never joins a mixer itself. The channel handle travels on an <c>AudioGraphNode</c> leaf and
/// the audio graph decides membership and gain, so a video source routes, groups and takes FX inserts like any
/// other source.
/// </summary>
public sealed class VideoAudioStream : IDisposable
{
    /// <summary>Everything is fed as interleaved stereo — what the mixers expect.</summary>
    public const int Channels = 2;

    /// <summary>The BASS channel handle for the audio graph to route. Never 0 for a live instance.</summary>
    public int Channel { get; }

    public int SampleRate { get; }

    /// <summary>
    /// True after a device change freed every BASS handle (<see cref="AudioMixerManager.ResetGeneration"/>).
    /// The cached <see cref="Channel"/> is dead; the owner must drop this instance and create a new one.
    /// </summary>
    public bool IsInvalidated => _resetGeneration != AudioMixerManager.ResetGeneration;

    /// <summary>Creates a decode-mode push stream at the mixer's sample rate. Returns null if BASS is unavailable.</summary>
    public static VideoAudioStream? TryCreate()
    {
        if (!AudioMixerManager.IsInitialized)
        {
            AudioMixerManager.Initialize();
            if (!AudioMixerManager.IsInitialized)
                return null;
        }

        var sampleRate = AudioConfig.MixerFrequency;

        // Decode mode: a mixer source must be a decode channel, and it also means an unrouted stream is never
        // pulled — so audio can't leak to the soundcard before the graph has placed it.
        var handle = Bass.CreateStream(sampleRate, Channels, BassFlags.Decode | BassFlags.Float, StreamProcedureType.Push);
        if (handle == 0)
        {
            Log.Warning($"[VideoAudio] Could not create the push stream: {Bass.LastError}");
            return null;
        }

        return new VideoAudioStream(handle, sampleRate);
    }

    /// <summary>
    /// How much audio is queued ahead of the play position. The feeder tops this up toward its target fill and
    /// subtracts it from the fed position to work out what is audible right now.
    /// </summary>
    public double BufferedSeconds
    {
        get
        {
            var queuedBytes = Bass.StreamPutData(Channel, IntPtr.Zero, 0);
            return queuedBytes > 0 ? queuedBytes / (double)(SampleRate * Channels * sizeof(float)) : 0;
        }
    }

    /// <summary>Queues interleaved stereo float samples for playback, continuing from whatever is already queued.</summary>
    public unsafe void Feed(ReadOnlySpan<float> interleaved)
    {
        if (interleaved.IsEmpty)
            return;

        fixed (float* samples = interleaved)
        {
            if (Bass.StreamPutData(Channel, (IntPtr)samples, interleaved.Length * sizeof(float)) < 0)
                Log.Warning($"[VideoAudio] Feeding the push stream failed: {Bass.LastError}");
        }
    }

    /// <summary>Drops everything queued, so the next <see cref="Feed"/> is heard immediately. Used on seek and mute.</summary>
    public void Flush() => Bass.ChannelSetPosition(Channel, 0);

    public void Dispose()
    {
        // The graph may still hold this channel in a submix; leaving it there past the free would corrupt the mix.
        BassMix.MixerRemoveChannel(Channel);
        Bass.StreamFree(Channel);
    }

    private VideoAudioStream(int channel, int sampleRate)
    {
        Channel = channel;
        SampleRate = sampleRate;
        _resetGeneration = AudioMixerManager.ResetGeneration;
    }

    private readonly int _resetGeneration;
}

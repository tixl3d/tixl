#nullable enable
using System;
using T3.Core.Audio.Native;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// A push source carrying one video's decoded audio. Unlike <see cref="OperatorAudioStreamBase"/> and
/// <see cref="SoundtrackClipStream"/> — seekable file sources — it has no position of its own: it plays exactly
/// what is fed and nothing else, so the feeder owns sync.
///
/// It deliberately never routes itself. The handle travels on an <c>AudioGraphNode</c> leaf and the audio graph
/// decides membership and gain, so a video source routes, groups and takes inserts like any other source.
/// </summary>
public sealed class VideoAudioStream : IDisposable
{
    /// <summary>Everything is fed as interleaved stereo — what the mix expects.</summary>
    public const int Channels = 2;

    /// <summary>The source for the audio graph to route.</summary>
    public AudioHandle Channel { get; }

    public int SampleRate { get; }

    /// <summary>Creates a push source at the mix's sample rate. Returns null if audio is unavailable.</summary>
    public static VideoAudioStream? TryCreate()
    {
        AudioMixerManager.Initialize();
        var engine = AudioMixerManager.NativeEngine;
        if (engine == null)
            return null;

        var sampleRate = AudioConfig.MixerFrequency;
        var source = engine.CreatePushSource(Channels, sampleRate, (int)(sampleRate * QueueCapacitySecs));
        if (!source.IsValid)
            return null;

        engine.Play(source);
        return new VideoAudioStream(engine, source, sampleRate);
    }

    /// <summary>
    /// How much audio is queued ahead of the play position. The feeder tops this up toward its target fill and
    /// subtracts it from the fed position to work out what is audible right now.
    /// </summary>
    public double BufferedSeconds => _engine.GetPushQueuedFrames(Channel) / (double)SampleRate;

    /// <summary>Queues interleaved stereo float samples for playback, continuing from whatever is already queued.</summary>
    public void Feed(ReadOnlySpan<float> interleaved)
    {
        if (interleaved.IsEmpty)
            return;

        var frameCount = interleaved.Length / Channels;
        if (_engine.PushWrite(Channel, interleaved, frameCount) < frameCount)
            Log.Warning("[VideoAudio] The audio queue is full; dropped samples.");
    }

    /// <summary>Drops everything queued, so the next <see cref="Feed"/> is heard immediately. Used on seek and mute.</summary>
    public void Flush() => _engine.PushClear(Channel);

    /// <summary>Safe from the feeder thread: the release happens on the main thread's next frame.</summary>
    public void Dispose() => _engine.ReleaseDeferred(Channel);

    private VideoAudioStream(NativeAudioEngine engine, AudioHandle channel, int sampleRate)
    {
        _engine = engine;
        Channel = channel;
        SampleRate = sampleRate;
    }

    /** Far above any feeder's target fill: running out of room would lose audio. */
    private const double QueueCapacitySecs = 4;

    private readonly NativeAudioEngine _engine;
}

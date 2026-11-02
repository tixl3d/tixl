#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace T3.Core.Audio.Native;

/// <summary>
/// Decodes an audio file without playing it, for waveform images and duration probes. Instances don't touch
/// the mixing engine and can run on worker threads, but a single instance isn't thread safe.
/// </summary>
public sealed unsafe class OfflineAudioDecoder : IDisposable
{
    public int Channels { get; }
    public int SampleRate { get; }
    public long FrameCount { get; }
    public double DurationSecs => SampleRate > 0 ? (double)FrameCount / SampleRate : 0;

    /// <summary>False for a missing or undecodable file, and when the native library is unavailable.</summary>
    public static bool TryOpen(string path, [NotNullWhen(true)] out OfflineAudioDecoder? decoder)
    {
        decoder = null;
        try
        {
            var handle = NativeAudio.OfflineOpen(path, out var channels, out var sampleRate, out var frameCount, out _);
            if (handle == IntPtr.Zero)
                return false;

            decoder = new OfflineAudioDecoder(handle, (int)channels, (int)sampleRate, (long)frameCount);
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    public bool Seek(long frame) => NativeAudio.OfflineSeek(_decoder, (ulong)frame) == 0;

    /// <summary>Reads interleaved frames and returns how many arrived; fewer than asked at the end of the file.</summary>
    public int Read(Span<float> interleaved)
    {
        fixed (float* output = interleaved)
        {
            var framesRead = NativeAudio.OfflineRead(_decoder, output, (ulong)(interleaved.Length / Channels));
            return framesRead > 0 ? (int)framesRead : 0;
        }
    }

    public void Dispose()
    {
        if (_decoder == IntPtr.Zero)
            return;

        NativeAudio.OfflineClose(_decoder);
        _decoder = IntPtr.Zero;
    }

    private OfflineAudioDecoder(IntPtr decoder, int channels, int sampleRate, long frameCount)
    {
        _decoder = decoder;
        Channels = channels;
        SampleRate = sampleRate;
        FrameCount = frameCount;
    }

    private IntPtr _decoder;
}

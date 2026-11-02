#nullable enable
using System;
using T3.Core.Logging;

namespace T3.Core.Audio.Native;

/// <summary>
/// One native mixing engine. Call from the main thread only (<see cref="Render"/> also from the export
/// thread); the audio thread never calls back into managed code.
/// </summary>
internal sealed unsafe class NativeAudioEngine : IDisposable
{
    /// <summary>Null when the library is missing or the device can't open, so callers stay silent but running.</summary>
    public static NativeAudioEngine? TryCreate(NativeAudio.Mode mode, NativeAudio.Backend backend = NativeAudio.Backend.Default,
                                               uint sampleRate = 48000, uint periodFrames = 256)
    {
        try
        {
            var engine = NativeAudio.Create(mode, backend, sampleRate, periodFrames, out var result);
            if (engine != IntPtr.Zero)
                return new NativeAudioEngine(engine);

            Log.Warning($"Could not start the audio engine (error {result}); audio stays silent.");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Log.Warning("The native audio library is unavailable; audio stays silent.");
        }

        return null;
    }

    public AudioSourceHandle LoadFile(string path, bool spatial = false)
    {
        var handle = NativeAudio.SoundLoad(_engine, path, spatial ? NativeAudio.LoadSpatial : 0);
        if (handle < 0)
            Log.Warning($"Could not load audio file {path} (error {handle}).");

        return new AudioSourceHandle(handle);
    }

    public void Unload(AudioSourceHandle source) => NativeAudio.SoundUnload(_engine, source.Value);
    public bool Play(AudioSourceHandle source) => NativeAudio.SoundPlay(_engine, source.Value) == 0;
    public bool Pause(AudioSourceHandle source) => NativeAudio.SoundPause(_engine, source.Value) == 0;
    public bool Seek(AudioSourceHandle source, double seconds) => NativeAudio.SoundSeekSeconds(_engine, source.Value, seconds) == 0;
    public double GetLengthSecs(AudioSourceHandle source) => NativeAudio.SoundGetLengthSeconds(_engine, source.Value);
    public void SetVolume(AudioSourceHandle source, float volume) => NativeAudio.SoundSetVolume(_engine, source.Value, volume);
    public void SetPitch(AudioSourceHandle source, float pitch) => NativeAudio.SoundSetPitch(_engine, source.Value, pitch);
    public bool IsPlaying(AudioSourceHandle source) => NativeAudio.SoundIsPlaying(_engine, source.Value) != 0;

    /// <summary>The next source position to be mixed; ahead of what is audible by <see cref="OutputLatencySecs"/>.</summary>
    public double GetCursorSecs(AudioSourceHandle source) => NativeAudio.SoundGetCursorSeconds(_engine, source.Value);

    public bool TryGetFormat(AudioSourceHandle source, out int channels, out int sampleRate)
    {
        var result = NativeAudio.SoundGetFormat(_engine, source.Value, out var nativeChannels, out var nativeRate);
        channels = (int)nativeChannels;
        sampleRate = (int)nativeRate;
        return result == 0;
    }

    /// <summary>How long audio takes from the mix to the device's output; 0 while no device is attached.</summary>
    public double OutputLatencySecs
    {
        get
        {
            NativeAudio.GetInfo(_engine, out var info);
            return info.SampleRate > 0 ? (double)(info.PeriodFrames * info.Periods) / info.SampleRate : 0;
        }
    }

    public void DetachDevice() => NativeAudio.DetachDevice(_engine);

    /// <summary>Reattaches the default output device; false leaves the engine silent but usable.</summary>
    public bool AttachDefaultDevice() => NativeAudio.AttachDevice(_engine, DefaultDevice) == 0;

    /// <summary>The highest output sample since the previous call.</summary>
    public float TakeOutputPeak() => NativeAudio.TapTakePeak(_engine, NativeAudio.OutputTap);

    /// <summary>The newest output samples as a mono mix, zero-padded at the start if fewer exist.</summary>
    public void ReadOutputMono(Span<float> samples)
    {
        fixed (float* destination = samples)
        {
            NativeAudio.TapReadLatest(_engine, NativeAudio.OutputTap, destination, samples.Length);
        }
    }

    /// <summary>The newest output frames as interleaved stereo, zero-padded at the start if fewer exist.</summary>
    public void ReadOutputStereo(Span<float> interleaved)
    {
        fixed (float* destination = interleaved)
        {
            NativeAudio.OutputReadStereo(_engine, destination, interleaved.Length / 2);
        }
    }

    /// <summary>Renders interleaved frames. Only works while no device is attached.</summary>
    public bool Render(Span<float> interleaved, uint frameCount)
    {
        fixed (float* output = interleaved)
        {
            return NativeAudio.Render(_engine, output, frameCount) == 0;
        }
    }

    public void Dispose()
    {
        if (_engine == IntPtr.Zero)
            return;

        NativeAudio.Destroy(_engine);
        _engine = IntPtr.Zero;
    }

    private NativeAudioEngine(IntPtr engine) => _engine = engine;

    private const int DefaultDevice = -1;

    private IntPtr _engine;
}

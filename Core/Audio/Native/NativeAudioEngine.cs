#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
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

    public AudioHandle LoadFile(string path, bool spatial = false)
    {
        var handle = NativeAudio.SoundLoad(_engine, path, spatial ? NativeAudio.LoadSpatial : 0);
        if (handle < 0)
            Log.Warning($"Could not load audio file {path} (error {handle}).");

        return new AudioHandle(handle);
    }

    public void Unload(AudioHandle source)
    {
        _routes.Remove(source);
        NativeAudio.SoundUnload(_engine, source.Value);
    }

    public AudioHandle CreateBus()
    {
        var handle = NativeAudio.BusCreate(_engine);
        if (handle < 0)
            Log.Warning($"Could not create an audio bus (error {handle}).");

        return new AudioHandle(handle);
    }

    public void DestroyBus(AudioHandle bus)
    {
        _routes.Remove(bus);
        NativeAudio.BusDestroy(_engine, bus.Value);
    }

    /// <summary>Releases a source or bus on the main thread's next frame, for finalizers and worker threads.</summary>
    public void ReleaseDeferred(AudioHandle handle) => _deferredReleases.Enqueue(handle);

    /// <summary>Called once per frame on the main thread.</summary>
    public void ProcessDeferredReleases()
    {
        while (_deferredReleases.TryDequeue(out var handle))
        {
            if ((handle.Value & BusHandleBit) != 0)
                DestroyBus(handle);
            else
                Unload(handle);
        }
    }

    /// <summary>Moves a source or bus into a bus or <see cref="AudioHandle.Output"/>; the default handle unroutes it.</summary>
    public bool Route(AudioHandle source, AudioHandle target)
    {
        if (NativeAudio.Route(_engine, source.Value, target.Value) != 0)
            return false;

        if (target.IsValid)
            _routes[source] = target;
        else
            _routes.Remove(source);

        return true;
    }

    /// <summary>Where a source or bus is routed; the default handle while it is unrouted.</summary>
    public AudioHandle GetRouteTarget(AudioHandle source) => _routes.GetValueOrDefault(source);

    public void SetBusVolume(AudioHandle bus, float volume) => NativeAudio.BusSetVolume(_engine, bus.Value, volume);

    /// <summary>A stopped bus no longer pulls its sources, so they keep their position and generators don't advance.</summary>
    public void SetBusPlaying(AudioHandle bus, bool playing) => NativeAudio.BusSetPlaying(_engine, bus.Value, playing ? 1 : 0);

    public void SetOutputGain(float gain) => NativeAudio.SetOutputGain(_engine, gain);

    public void SetPan(AudioHandle source, float pan) => NativeAudio.SoundSetPan(_engine, source.Value, pan);

    /// <summary>The highest recent sample of a source, a bus or the output. Reading doesn't consume anything, so any number of meters can share it.</summary>
    public float GetLevel(AudioHandle handle, float windowSecs = DefaultLevelWindowSecs)
        => NativeAudio.GetLevel(_engine, handle.Value, (int)(windowSecs * AudioConfig.MixerFrequency));

    /// <summary>The newest samples of a source, a bus or the output as a mono mix. False and silence while it isn't playing.</summary>
    public bool ReadLatestMono(AudioHandle handle, Span<float> samples)
    {
        fixed (float* destination = samples)
        {
            return NativeAudio.ReadLatest(_engine, handle.Value, destination, samples.Length) > 0;
        }
    }

    public AudioHandle CreatePushSource(int channels, int sampleRate, int capacityFrames)
    {
        var handle = NativeAudio.SoundCreatePush(_engine, (uint)channels, (uint)sampleRate, (uint)capacityFrames);
        if (handle < 0)
            Log.Warning($"Could not create an audio push source (error {handle}).");

        return new AudioHandle(handle);
    }

    /// <summary>Queues interleaved frames and returns how many fit.</summary>
    public int PushWrite(AudioHandle source, ReadOnlySpan<float> interleaved, int frameCount)
    {
        fixed (float* frames = interleaved)
        {
            return NativeAudio.PushWrite(_engine, source.Value, frames, (uint)frameCount);
        }
    }

    public int GetPushQueuedFrames(AudioHandle source) => NativeAudio.PushQueuedFrames(_engine, source.Value);
    public void PushClear(AudioHandle source) => NativeAudio.PushClear(_engine, source.Value);

    public AudioHandle CreateGenerator()
    {
        var handle = NativeAudio.SoundCreateGenerator(_engine);
        if (handle < 0)
            Log.Warning($"Could not create a tone generator (error {handle}).");

        return new AudioHandle(handle);
    }

    public void SetGenerator(AudioHandle source, float frequency, int waveform, float attackSecs, float decaySecs, float sustainLevel, float releaseSecs, float durationSecs)
        => NativeAudio.GeneratorSet(_engine, source.Value, frequency, waveform, attackSecs, decaySecs, sustainLevel, releaseSecs, durationSecs);

    public void TriggerGeneratorAttack(AudioHandle source) => NativeAudio.GeneratorAttack(_engine, source.Value);
    public void TriggerGeneratorRelease(AudioHandle source) => NativeAudio.GeneratorRelease(_engine, source.Value);
    public void ResetGenerator(AudioHandle source) => NativeAudio.GeneratorReset(_engine, source.Value);
    public bool IsGeneratorActive(AudioHandle source) => NativeAudio.GeneratorIsActive(_engine, source.Value) != 0;

    public bool Play(AudioHandle source) => NativeAudio.SoundPlay(_engine, source.Value) == 0;
    public bool Pause(AudioHandle source) => NativeAudio.SoundPause(_engine, source.Value) == 0;
    public bool Seek(AudioHandle source, double seconds) => NativeAudio.SoundSeekSeconds(_engine, source.Value, seconds) == 0;
    public double GetLengthSecs(AudioHandle source) => NativeAudio.SoundGetLengthSeconds(_engine, source.Value);
    public void SetVolume(AudioHandle source, float volume) => NativeAudio.SoundSetVolume(_engine, source.Value, volume);
    public void SetPitch(AudioHandle source, float pitch) => NativeAudio.SoundSetPitch(_engine, source.Value, pitch);
    public bool IsPlaying(AudioHandle source) => NativeAudio.SoundIsPlaying(_engine, source.Value) != 0;

    /// <summary>The next source position to be mixed; ahead of what is audible by <see cref="OutputLatencySecs"/>.</summary>
    public double GetCursorSecs(AudioHandle source) => NativeAudio.SoundGetCursorSeconds(_engine, source.Value);

    public bool TryGetFormat(AudioHandle source, out int channels, out int sampleRate)
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

    /// <summary>A capture or playback device's name, or null past the end of the list.</summary>
    public string? GetDeviceName(bool capture, int index)
    {
        Span<byte> name = stackalloc byte[MaxDeviceNameBytes];
        fixed (byte* buffer = name)
        {
            var length = NativeAudio.GetDeviceName(_engine, capture ? 1 : 0, index, buffer, name.Length);
            return length > 0 ? Encoding.UTF8.GetString(name[..length]) : null;
        }
    }

    public int GetDeviceCount(bool capture) => NativeAudio.GetDeviceCount(_engine, capture ? 1 : 0);

    /// <summary>Starts capturing as interleaved stereo at the mix rate. Returns the native error code; 0 is success.</summary>
    public int StartCapture(int deviceIndex, bool loopback)
    {
        NativeAudio.CaptureStop(_engine);
        return NativeAudio.CaptureStart(_engine, deviceIndex, loopback ? 1 : 0);
    }

    public void StopCapture() => NativeAudio.CaptureStop(_engine);
    public bool IsCaptureRunning => NativeAudio.CaptureIsRunning(_engine) != 0;

    /// <summary>Counts every frame since capture started; reads address frames by this position.</summary>
    public ulong CaptureFramesWritten => NativeAudio.CaptureFramesWritten(_engine);

    /// <summary>How far a reader can fall behind before frames are overwritten.</summary>
    public static int CaptureRingCapacity => NativeAudio.CaptureRingCapacity();

    /// <summary>Copies interleaved frames from an absolute position and returns how many arrived.</summary>
    public int ReadCapture(ulong startFrame, Span<float> interleaved)
    {
        fixed (float* destination = interleaved)
        {
            return NativeAudio.CaptureRead(_engine, startFrame, destination, interleaved.Length / 2);
        }
    }

    public void DetachDevice() => NativeAudio.DetachDevice(_engine);

    /// <summary>Reattaches the default output device; false leaves the engine silent but usable.</summary>
    public bool AttachDefaultDevice() => NativeAudio.AttachDevice(_engine, DefaultDevice) == 0;

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
    private const int BusHandleBit = 0x40000000;
    private const int MaxDeviceNameBytes = 512;

    /** Long enough that a meter read once per frame can't miss a peak between device callbacks. */
    private const float DefaultLevelWindowSecs = 0.05f;

    private readonly Dictionary<AudioHandle, AudioHandle> _routes = new();
    private readonly ConcurrentQueue<AudioHandle> _deferredReleases = new();

    private IntPtr _engine;
}

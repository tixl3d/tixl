#nullable enable
using System;
using System.Runtime.InteropServices;

namespace T3.Core.Audio.Native;

/// <summary>
/// P/Invoke surface of the tixlaudio library. All arguments are blittable, so per-frame calls neither marshal
/// nor allocate; only the path strings of load and decode calls are converted, and those run on user actions.
/// </summary>
internal static unsafe partial class NativeAudio
{
    internal enum Mode
    {
        Device = 0,
        Offline = 1,
    }

    internal enum Backend
    {
        Default = 0,
        Null = 1,
        PulseAudio = 2,
        Alsa = 3,
        Jack = 4,
    }

    /** Load flag: spatialize the sound. Without it the sound bypasses the spatializer. */
    internal const uint LoadSpatial = 4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Info
    {
        public uint SampleRate;
        public uint Channels;
        public uint PeriodFrames;
        public uint Periods;
        public uint Backend;
    }

    [LibraryImport(Library, EntryPoint = "ta_create")]
    internal static partial IntPtr Create(Mode mode, Backend backend, uint sampleRate, uint periodFrames, out int result);

    [LibraryImport(Library, EntryPoint = "ta_destroy")]
    internal static partial void Destroy(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_switch_device")]
    internal static partial int SwitchDevice(IntPtr engine, int deviceIndex);

    [LibraryImport(Library, EntryPoint = "ta_detach_device")]
    internal static partial void DetachDevice(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_attach_device")]
    internal static partial int AttachDevice(IntPtr engine, int deviceIndex);

    [LibraryImport(Library, EntryPoint = "ta_render")]
    internal static partial int Render(IntPtr engine, float* output, uint frameCount);

    [LibraryImport(Library, EntryPoint = "ta_get_info")]
    internal static partial void GetInfo(IntPtr engine, out Info info);

    [LibraryImport(Library, EntryPoint = "ta_set_output_gain")]
    internal static partial void SetOutputGain(IntPtr engine, float gain);

    [LibraryImport(Library, EntryPoint = "ta_sound_load", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SoundLoad(IntPtr engine, string path, uint flags);

    [LibraryImport(Library, EntryPoint = "ta_sound_unload")]
    internal static partial void SoundUnload(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_play")]
    internal static partial int SoundPlay(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_pause")]
    internal static partial int SoundPause(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_seek_seconds")]
    internal static partial int SoundSeekSeconds(IntPtr engine, int sound, double seconds);

    [LibraryImport(Library, EntryPoint = "ta_sound_get_length_seconds")]
    internal static partial double SoundGetLengthSeconds(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_set_volume")]
    internal static partial void SoundSetVolume(IntPtr engine, int sound, float volume);

    [LibraryImport(Library, EntryPoint = "ta_sound_set_pitch")]
    internal static partial void SoundSetPitch(IntPtr engine, int sound, float pitch);

    [LibraryImport(Library, EntryPoint = "ta_output_read_stereo")]
    internal static partial void OutputReadStereo(IntPtr engine, float* destination, int frameCount);

    [LibraryImport(Library, EntryPoint = "ta_sound_is_playing")]
    internal static partial int SoundIsPlaying(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_get_cursor_seconds")]
    internal static partial double SoundGetCursorSeconds(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_get_format")]
    internal static partial int SoundGetFormat(IntPtr engine, int sound, out uint channels, out uint sampleRate);

    [LibraryImport(Library, EntryPoint = "ta_offline_open", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr OfflineOpen(string path, out uint channels, out uint sampleRate, out ulong frameCount, out int result);

    [LibraryImport(Library, EntryPoint = "ta_offline_seek")]
    internal static partial int OfflineSeek(IntPtr decoder, ulong frame);

    [LibraryImport(Library, EntryPoint = "ta_offline_read")]
    internal static partial long OfflineRead(IntPtr decoder, float* output, ulong frameCount);

    [LibraryImport(Library, EntryPoint = "ta_offline_close")]
    internal static partial void OfflineClose(IntPtr decoder);

    [LibraryImport(Library, EntryPoint = "ta_sound_set_pan")]
    internal static partial void SoundSetPan(IntPtr engine, int sound, float pan);

    [LibraryImport(Library, EntryPoint = "ta_get_level")]
    internal static partial float GetLevel(IntPtr engine, int handle, int frameCount);

    [LibraryImport(Library, EntryPoint = "ta_read_latest")]
    internal static partial int ReadLatest(IntPtr engine, int handle, float* destination, int count);

    [LibraryImport(Library, EntryPoint = "ta_route")]
    internal static partial int Route(IntPtr engine, int source, int target);

    [LibraryImport(Library, EntryPoint = "ta_bus_create")]
    internal static partial int BusCreate(IntPtr engine, AudioEffectKind effectKind);

    [LibraryImport(Library, EntryPoint = "ta_bus_set_effect")]
    internal static partial void BusSetEffect(IntPtr engine, int bus, float p0, float p1, float p2, float p3, float p4);

    [LibraryImport(Library, EntryPoint = "ta_bus_reset_effect")]
    internal static partial void BusResetEffect(IntPtr engine, int bus);

    [LibraryImport(Library, EntryPoint = "ta_bus_fade_out_effect")]
    internal static partial void BusFadeOutEffect(IntPtr engine, int bus, float seconds);

    [LibraryImport(Library, EntryPoint = "ta_bus_destroy")]
    internal static partial void BusDestroy(IntPtr engine, int bus);

    [LibraryImport(Library, EntryPoint = "ta_bus_set_volume")]
    internal static partial void BusSetVolume(IntPtr engine, int bus, float volume);

    [LibraryImport(Library, EntryPoint = "ta_bus_set_playing")]
    internal static partial void BusSetPlaying(IntPtr engine, int bus, int playing);

    [LibraryImport(Library, EntryPoint = "ta_sound_create_push")]
    internal static partial int SoundCreatePush(IntPtr engine, uint channels, uint sampleRate, uint capacityFrames);

    [LibraryImport(Library, EntryPoint = "ta_push_write")]
    internal static partial int PushWrite(IntPtr engine, int sound, float* frames, uint frameCount);

    [LibraryImport(Library, EntryPoint = "ta_push_queued_frames")]
    internal static partial int PushQueuedFrames(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_push_clear")]
    internal static partial void PushClear(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_create_generator")]
    internal static partial int SoundCreateGenerator(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_generator_set")]
    internal static partial void GeneratorSet(IntPtr engine, int sound, float frequency, int waveform, float attackSeconds, float decaySeconds, float sustainLevel, float releaseSeconds, float durationSeconds);

    [LibraryImport(Library, EntryPoint = "ta_generator_attack")]
    internal static partial void GeneratorAttack(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_generator_release")]
    internal static partial void GeneratorRelease(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_generator_reset")]
    internal static partial void GeneratorReset(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_generator_is_active")]
    internal static partial int GeneratorIsActive(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_get_device_count")]
    internal static partial int GetDeviceCount(IntPtr engine, int capture);

    [LibraryImport(Library, EntryPoint = "ta_get_device_name")]
    internal static partial int GetDeviceName(IntPtr engine, int capture, int index, byte* buffer, int bufferLength);

    [LibraryImport(Library, EntryPoint = "ta_capture_start")]
    internal static partial int CaptureStart(IntPtr engine, int deviceIndex, int loopback);

    [LibraryImport(Library, EntryPoint = "ta_capture_stop")]
    internal static partial void CaptureStop(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_capture_is_running")]
    internal static partial int CaptureIsRunning(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_capture_frames_written")]
    internal static partial ulong CaptureFramesWritten(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_capture_ring_capacity")]
    internal static partial int CaptureRingCapacity();

    [LibraryImport(Library, EntryPoint = "ta_capture_read")]
    internal static partial int CaptureRead(IntPtr engine, ulong startFrame, float* destination, int frameCount);

    [LibraryImport(Library, EntryPoint = "ta_sound_set_spatial")]
    internal static partial void SoundSetSpatial(IntPtr engine, int sound,
                                                 float x, float y, float z, float dirX, float dirY, float dirZ, float velX, float velY, float velZ,
                                                 float minDistance, float maxDistance, float rolloff, float dopplerFactor,
                                                 float innerConeDegrees, float outerConeDegrees, float outerConeGain, int mode);

    [LibraryImport(Library, EntryPoint = "ta_set_listener")]
    internal static partial void SetListener(IntPtr engine, float x, float y, float z, float forwardX, float forwardY, float forwardZ,
                                             float upX, float upY, float upZ, float velX, float velY, float velZ);

    private const string Library = "tixlaudio";
}

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

    internal enum NodeKind
    {
        Endpoint = 0,
        Sound = 1,
        Bus = 2,
        Tap = 4,
    }

    /** Tap index of the device output, recorded before the output gain. */
    internal const int OutputTap = 0;

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

    [LibraryImport(Library, EntryPoint = "ta_now_ns")]
    internal static partial ulong NowNs();

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

    [LibraryImport(Library, EntryPoint = "ta_set_tracked_sound")]
    internal static partial void SetTrackedSound(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_get_playback_seconds")]
    internal static partial double GetPlaybackSeconds(IntPtr engine, ulong nowNs);

    [LibraryImport(Library, EntryPoint = "ta_tap_read_latest")]
    internal static partial ulong TapReadLatest(IntPtr engine, int tap, float* destination, int count);

    [LibraryImport(Library, EntryPoint = "ta_decode_peaks", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int DecodePeaks(string path, int bucketCount, float* minMaxPairs, out double lengthSeconds);

    [LibraryImport(Library, EntryPoint = "ta_decode_window", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int DecodeWindow(string path, ulong startFrame, uint frameCount, uint channels, int useSeekTable, float* output, out uint sampleRate);

    private const string Library = "tixlaudio";
}

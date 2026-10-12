using System.Runtime.InteropServices;

namespace T3.Spikes.MiniaudioSpike;

/// <summary>
/// P/Invoke surface of libtixlaudio. All arguments are blittable, so calls in the frame loop don't marshal
/// or allocate; only the path strings of load calls are converted, which happens on user actions.
/// </summary>
internal static unsafe partial class NativeAudio
{
    public enum Mode
    {
        Device = 0,
        Offline = 1,
    }

    public enum Backend
    {
        Default = 0,
        Null = 1,
        PulseAudio = 2,
        Alsa = 3,
        Jack = 4,
    }

    [Flags]
    public enum LoadFlags : uint
    {
        /** Own decoder with an MP3 seek table, decoded on the audio thread like a BASS decode stream. */
        DecoderWithSeekTable = 0,
        StreamResourceManager = 1,
        DecodeToMemory = 2,
        Spatial = 4,
    }

    public enum NodeKind
    {
        Endpoint = 0,
        Sound = 1,
        Bus = 2,
        Echo = 3,
        Tap = 4,
    }

    /** Tap index of the device output, recorded before the output gain. */
    public const int OutputTap = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Stats
    {
        public ulong CallbackCount;
        public ulong LateCallbackCount;
        public ulong MaxCallbackGapNs;
        public ulong MaxCallbackDurationNs;
        public ulong TotalCallbackDurationNs;
        public ulong TotalFramesRendered;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Info
    {
        public uint SampleRate;
        public uint Channels;
        public uint PeriodFrames;
        public uint Periods;
        public uint Backend;
    }

    public static string GetBackendName(in Info info) => new((sbyte*)BackendName(info.Backend));

    [LibraryImport(Library, EntryPoint = "ta_now_ns")]
    public static partial ulong NowNs();

    [LibraryImport(Library, EntryPoint = "ta_create")]
    public static partial IntPtr Create(Mode mode, Backend backend, uint sampleRate, uint periodFrames, out int result);

    [LibraryImport(Library, EntryPoint = "ta_destroy")]
    public static partial void Destroy(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_switch_device")]
    public static partial int SwitchDevice(IntPtr engine, int deviceIndex);

    [LibraryImport(Library, EntryPoint = "ta_detach_device")]
    public static partial void DetachDevice(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_attach_device")]
    public static partial int AttachDevice(IntPtr engine, int deviceIndex);

    [LibraryImport(Library, EntryPoint = "ta_push_clear")]
    public static partial void PushClear(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_render")]
    public static partial int Render(IntPtr engine, float* output, uint frameCount);

    [LibraryImport(Library, EntryPoint = "ta_get_info")]
    public static partial void GetInfo(IntPtr engine, out Info info);

    [LibraryImport(Library, EntryPoint = "ta_backend_name")]
    private static partial IntPtr BackendName(uint backend);

    [LibraryImport(Library, EntryPoint = "ta_get_stats")]
    public static partial void GetStats(IntPtr engine, out Stats stats);

    [LibraryImport(Library, EntryPoint = "ta_reset_stats")]
    public static partial void ResetStats(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_set_output_gain")]
    public static partial void SetOutputGain(IntPtr engine, float gain);

    [LibraryImport(Library, EntryPoint = "ta_sound_load", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SoundLoad(IntPtr engine, string path, LoadFlags flags);

    [LibraryImport(Library, EntryPoint = "ta_sound_create_sine")]
    public static partial int SoundCreateSine(IntPtr engine, double frequency, double amplitude);

    [LibraryImport(Library, EntryPoint = "ta_sound_create_push")]
    public static partial int SoundCreatePush(IntPtr engine, uint channels, uint sampleRate, uint capacityFrames);

    [LibraryImport(Library, EntryPoint = "ta_push_write")]
    public static partial int PushWrite(IntPtr engine, int sound, float* frames, uint frameCount);

    [LibraryImport(Library, EntryPoint = "ta_push_queued_frames")]
    public static partial int PushQueuedFrames(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_push_underrun_frames")]
    public static partial ulong PushUnderrunFrames(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_unload")]
    public static partial void SoundUnload(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_play")]
    public static partial int SoundPlay(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_pause")]
    public static partial int SoundPause(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_reset_state")]
    public static partial void SoundResetState(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_seek_seconds")]
    public static partial int SoundSeekSeconds(IntPtr engine, int sound, double seconds);

    [LibraryImport(Library, EntryPoint = "ta_sound_get_length_seconds")]
    public static partial double SoundGetLengthSeconds(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_sound_set_volume")]
    public static partial void SoundSetVolume(IntPtr engine, int sound, float volume);

    [LibraryImport(Library, EntryPoint = "ta_sound_set_pitch")]
    public static partial void SoundSetPitch(IntPtr engine, int sound, float pitch);

    [LibraryImport(Library, EntryPoint = "ta_sound_set_position")]
    public static partial void SoundSetPosition(IntPtr engine, int sound, float x, float y, float z);

    [LibraryImport(Library, EntryPoint = "ta_set_listener_position")]
    public static partial void SetListenerPosition(IntPtr engine, float x, float y, float z);

    [LibraryImport(Library, EntryPoint = "ta_set_tracked_sound")]
    public static partial void SetTrackedSound(IntPtr engine, int sound);

    [LibraryImport(Library, EntryPoint = "ta_get_playback_seconds")]
    public static partial double GetPlaybackSeconds(IntPtr engine, ulong nowNs);

    [LibraryImport(Library, EntryPoint = "ta_bus_create")]
    public static partial int BusCreate(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_bus_set_volume")]
    public static partial void BusSetVolume(IntPtr engine, int bus, float volume);

    [LibraryImport(Library, EntryPoint = "ta_echo_create")]
    public static partial int EchoCreate(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_echo_set")]
    public static partial void EchoSet(IntPtr engine, int echo, float delaySeconds, float feedback, float wetMix);

    [LibraryImport(Library, EntryPoint = "ta_tap_create")]
    public static partial int TapCreate(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_tap_read_latest")]
    public static partial ulong TapReadLatest(IntPtr engine, int tap, float* destination, int count);

    [LibraryImport(Library, EntryPoint = "ta_tap_read_since")]
    public static partial int TapReadSince(IntPtr engine, int tap, ulong sinceFrame, float* destination, int maxCount, out ulong totalFrames);

    [LibraryImport(Library, EntryPoint = "ta_tap_take_peak")]
    public static partial float TapTakePeak(IntPtr engine, int tap);

    [LibraryImport(Library, EntryPoint = "ta_connect")]
    public static partial int Connect(IntPtr engine, NodeKind sourceKind, int sourceIndex, NodeKind targetKind, int targetIndex);

    [LibraryImport(Library, EntryPoint = "ta_set_apply_routes_immediately")]
    public static partial void SetApplyRoutesImmediately(IntPtr engine, int immediately);

    [LibraryImport(Library, EntryPoint = "ta_decode_peaks", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int DecodePeaks(string path, int bucketCount, float* minMaxPairs, out double lengthSeconds);

    [LibraryImport(Library, EntryPoint = "ta_decode_window", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int DecodeWindow(string path, ulong startFrame, uint frameCount, uint channels, int useSeekTable, float* output, out uint sampleRate);

    [LibraryImport(Library, EntryPoint = "ta_get_device_count")]
    public static partial int GetDeviceCount(IntPtr engine, int capture);

    [LibraryImport(Library, EntryPoint = "ta_get_device_name")]
    public static partial int GetDeviceName(IntPtr engine, int capture, int index, byte* buffer, int bufferLength);

    [LibraryImport(Library, EntryPoint = "ta_capture_start")]
    public static partial int CaptureStart(IntPtr engine, int deviceIndex, int loopback);

    [LibraryImport(Library, EntryPoint = "ta_capture_stop")]
    public static partial void CaptureStop(IntPtr engine);

    [LibraryImport(Library, EntryPoint = "ta_capture_take_peak")]
    public static partial float CaptureTakePeak(IntPtr engine);

    private const string Library = "tixlaudio";
}

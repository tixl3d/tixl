#nullable enable
using System;
using System.Threading;
using T3.Core.Audio.Native;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Settings;

namespace T3.Core.Audio;

/// <summary>
/// Owns the native engine and the two buses everything plays through:
///   Operator sources and [AudioBus]es > Operator Bus > Output > Soundcard
///   Soundtrack clips > Soundtrack Bus > Output > Soundcard
/// An export detaches the device and pulls the same graph.
/// </summary>
public static class AudioMixerManager
{
    /// <summary>Null when the native library is unavailable; everything audio then stays silent but running.</summary>
    internal static NativeAudioEngine? NativeEngine => _engine;

    /// <summary>False when audio can only be rendered: no device could be opened, but an export still has its audio.</summary>
    internal static bool HasOutputDevice => _hasOutputDevice;

    /// <summary>Where sources not routed through the audio graph play.</summary>
    internal static AudioHandle OperatorBus { get; private set; }

    /// <summary>Where soundtrack clips not routed through the audio graph play.</summary>
    internal static AudioHandle SoundtrackBus { get; private set; }

    public static void Initialize()
    {
        lock (_initLock)
        {
            if (_initialized)
                return;

            _initialized = true;

            _engine = NativeAudioEngine.TryCreate(NativeAudio.Mode.Device, sampleRate: AudioConfig.MixerFrequency);
            _hasOutputDevice = _engine != null;
            _engine ??= NativeAudioEngine.TryCreate(NativeAudio.Mode.Offline, NativeAudio.Backend.Null, AudioConfig.MixerFrequency);
            if (_engine == null)
                return;

            OperatorBus = _engine.CreateBus();
            SoundtrackBus = _engine.CreateBus();
        }
    }

    public static void SetOperatorMixerVolume(float volume) => _engine?.SetBusVolume(OperatorBus, volume);

    internal static void SetOperatorMute(bool mute)
        => _engine?.SetBusVolume(OperatorBus, mute ? 0f : CompositionSettings.Current.Audio.OperatorVolume);

    /// <summary>Delay in seconds, feedback and mix 0..1.</summary>
    public static void SetEcho(AudioHandle bus, float delaySecs, float feedback, float mix, bool pingPong)
        => _engine?.SetEcho(bus, delaySecs, feedback, mix, pingPong);

    public static void SetCompressor(AudioHandle bus, float thresholdDb, float ratio, float attackSecs, float releaseSecs, float makeupGainDb)
        => _engine?.SetCompressor(bus, thresholdDb, ratio, attackSecs, releaseSecs, makeupGainDb);

    /// <summary>Mix 0..1, decay in seconds to -60 dB, and how long high frequencies last relative to that, 0..1.</summary>
    public static void SetReverb(AudioHandle bus, float mix, float decaySecs, float highFrequencyRatio)
        => _engine?.SetReverb(bus, mix, decaySecs, highFrequencyRatio);

    /// <summary>
    /// Reads the duration of an audio file in seconds. Returns 0 on failure. Used by the timeline-drop handler
    /// to size a new clip's TimeRange at the moment of import, without going through the full playback path.
    /// </summary>
    public static double TryProbeAudioDurationSecs(string filePath)
    {
        if (!OfflineAudioDecoder.TryOpen(filePath, out var decoder))
        {
            Log.Warning($"[AudioMixer] Could not read audio file '{filePath}'.");
            return 0;
        }

        using (decoder)
        {
            return decoder.DurationSecs;
        }
    }

    /// <summary>
    /// Called once per frame. An export renders at the app volume whatever is muted for monitoring,
    /// and the analysis taps sit before this gain, so neither mute nor volume changes what operators react to.
    /// </summary>
    internal static void UpdateNativeEngine()
    {
        if (_engine == null)
            return;

        _engine.ProcessDeferredReleases();
        var config = CoreSettings.Config;
        _engine.SetOutputGain(config.AppMute && !AudioRendering.IsRecording ? 0f : config.AppVolume);
    }

    /// <summary>The highest recent sample of a source or bus in the audio graph (0.0 to 1.0).</summary>
    public static float GetLevel(AudioHandle handle) => _engine != null ? Math.Min(_engine.GetLevel(handle), 1f) : 0f;

    public static float GetGlobalMixerLevel() => GetLevel(AudioHandle.Output);
    public static float GetOperatorMixerLevel() => GetLevel(OperatorBus);
    public static float GetSoundtrackMixerLevel() => GetLevel(SoundtrackBus);

    private static readonly Lock _initLock = new();
    private static NativeAudioEngine? _engine;
    private static bool _hasOutputDevice;
    private static bool _initialized;
}

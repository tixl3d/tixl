#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Logging;
using T3.Core.Settings;

namespace T3.Core.Audio;

/// <summary>
/// Manages the audio mixer architecture with separate paths for operator clips and soundtrack clips.
/// 
/// Architecture: 
///   Live Playback:
///     Operator Clip(s) > Operator Mixer (decode) > Global Mixer > Soundcard
///     Soundtrack Clip(s) > Soundtrack Mixer (decode) > Global Mixer > Soundcard
///   
///   Export:
///     GlobalMixer is PAUSED during export, so we can read directly from:
///     OperatorMixer and SoundtrackMixer using Bass.ChannelGetData()
///   
///   Offline Analysis (waveform images, FFT):
///     Standalone decode streams via CreateOfflineAnalysisStream() - no mixer needed.
///     Each analysis stream is independent and does not interfere with playback.
/// </summary>
public static class AudioMixerManager
{
    private static int _globalMixerHandle;
    private static int _operatorMixerHandle;
    private static int _soundtrackMixerHandle;
    private static bool _initialized;
    private static bool _initializationFailed;
    private static int _flacPluginHandle;
    private static readonly Lock _offlineStreamLock = new();
    private static readonly Lock _initLock = new();

    private static float _globalMixerVolume = 1.0f;

    internal static int GlobalMixerHandle => _globalMixerHandle;
    public static int OperatorMixerHandle => _operatorMixerHandle;
    internal static int SoundtrackMixerHandle => _soundtrackMixerHandle;
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Bumped on every <see cref="Shutdown"/> (e.g. an audio-device change frees ALL BASS handles).
    /// Anything caching mixer/stream handles across frames must compare against this and rebuild
    /// its handles when the generation changed — the cached ints are dead after a teardown.
    /// </summary>
    public static int ResetGeneration => _resetGeneration;
    private static int _resetGeneration;
    
    public static void Initialize()
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                Log.Gated.Audio("[AudioMixer] Already initialized, skipping.");
                return;
            }

            if (_initializationFailed)
            {
                // Don't spam logs with repeated init attempts after a failure
                return;
            }

            // Asking first, because everything below P/Invokes BASS and a missing library throws from the
            // call. Several callers sit in the render loop, where that would end the process.
            if (!BassLibrary.IsAvailable)
            {
                _initializationFailed = true;
                return;
            }

            Log.Gated.Audio("[AudioMixer] Starting initialization...");

        // Check if BASS is already initialized by checking the default output device
        // Note: Bass.CurrentDevice returns -1 when not initialized, so we check device 1 (default output)
        bool bassIsInitialized = false;
        try
        {
            // Device 1 is typically the default output device
            if (Bass.GetDeviceInfo(1, out var deviceInfo))
            {
                bassIsInitialized = deviceInfo.IsInitialized;
            }
        }
        catch
        {
            // GetDeviceInfo can fail if BASS DLL is not loaded yet
            bassIsInitialized = false;
        }
        
        if (bassIsInitialized)
        {
            Log.Warning("[AudioMixer] BASS was already initialized by something else - our low-latency config may not apply!");
            Log.Warning("[AudioMixer] To fix this, ensure AudioMixerManager.Initialize() is called BEFORE any Bass.Init() calls.");
            Bass.GetInfo(out var info);
            
            // Set the mixer frequency to the device's actual sample rate
            AudioConfig.MixerFrequency = info.SampleRate;
            Log.Gated.Audio($"[AudioMixer] Existing BASS - SampleRate: {info.SampleRate}Hz, MinBuffer: {info.MinBufferLength}ms, Latency: {info.Latency}ms");
        }
        else
        {
            Log.Gated.Audio("[AudioMixer] BASS not initialized, configuring for low latency...");
            
            // Query the default output device's sample rate from WASAPI before BASS init
            // WASAPI loopback devices represent the output and have the correct MixFrequency
            int deviceSampleRate = GetDefaultOutputSampleRate();
            
            // Configure BASS for low latency BEFORE initialization
            Bass.Configure(Configuration.UpdatePeriod, AudioConfig.UpdatePeriodMs);
            Bass.Configure(Configuration.UpdateThreads, AudioConfig.UpdateThreads);
            Bass.Configure(Configuration.PlaybackBufferLength, AudioConfig.PlaybackBufferLengthMs);
            Bass.Configure(Configuration.DeviceBufferLength, AudioConfig.DeviceBufferLengthMs);
            
            Log.Gated.Audio($"[AudioMixer] Config - UpdatePeriod: {AudioConfig.UpdatePeriodMs}ms, UpdateThreads: {AudioConfig.UpdateThreads}, PlaybackBuffer: {AudioConfig.PlaybackBufferLengthMs}ms, DeviceBuffer: {AudioConfig.DeviceBufferLengthMs}ms");
            
            // Try to initialize BASS with the device's actual sample rate first,
            // then fall back to common sample rates if that fails
            // Enable 3D audio support along with latency optimization
            // Init is tried across three dimensions, outermost first: the device, then the flag set, then the
            // sample rate. Device -1 leads so Windows and macOS behave exactly as before - the extra device
            // candidates only come into play where BASS's default does not work (see GetDeviceCandidates).
            (DeviceInitFlags Flags, string Name)[] methods =
            [
                (DeviceInitFlags.Latency | DeviceInitFlags.Stereo | DeviceInitFlags.Device3D, "LATENCY"),
                (DeviceInitFlags.Stereo | DeviceInitFlags.Device3D, "STEREO+3D"),
                (DeviceInitFlags.Default | DeviceInitFlags.Device3D, "DEFAULT+3D"),
            ];

            // Build frequency list: device rate first (if known), then common fallbacks
            var frequenciesToTry = new List<int>();
            if (deviceSampleRate > 0)
            {
                frequenciesToTry.Add(deviceSampleRate);
            }
            // Add common fallbacks that aren't already in the list
            if (deviceSampleRate != 48000) frequenciesToTry.Add(48000);
            if (deviceSampleRate != 44100) frequenciesToTry.Add(44100);

            var initialized = false;
            var usedFrequency = 0;
            var usedDeviceDefault = false;
            var initMethod = "LATENCY";
            var usedDevice = -1;

            foreach (var device in GetDeviceCandidates())
            {
                foreach (var (flags, methodName) in methods)
                {
                    foreach (var freq in frequenciesToTry)
                    {
                        var isDeviceRate = freq == deviceSampleRate && deviceSampleRate > 0;
                        var freqDesc = isDeviceRate ? $"{freq}Hz (device)" : $"{freq}Hz (fallback)";

                        if (Bass.Init(device, freq, flags, IntPtr.Zero))
                        {
                            if (methodName == "LATENCY")
                                Log.Gated.Audio($"[AudioMixer] BASS initialized with LATENCY flag at {freqDesc}");
                            else
                                Log.Warning($"[AudioMixer] BASS initialized with {methodName} at {freqDesc} (no latency optimization)");

                            initialized = true;
                            usedFrequency = freq;
                            usedDeviceDefault = isDeviceRate;
                            initMethod = methodName;
                            usedDevice = device;
                            break;
                        }

                        var error = Bass.LastError;

                        // Something else initialized BASS first; that init is usable as it stands.
                        if (error == Errors.Already)
                        {
                            Log.Gated.Audio("[AudioMixer] BASS already initialized");
                            initialized = true;
                            usedDeviceDefault = true;
                            initMethod = "EXISTING";
                            usedDevice = device;
                            break;
                        }

                        Log.Gated.Audio($"{error} [AudioMixer] Init of device {device} at {freqDesc} failed, trying next...");
                    }

                    if (initialized)
                        break;
                }

                if (!initialized)
                    continue;

                if (usedDevice != -1)
                    Log.Warning($"[AudioMixer] BASS's default device was unusable; using device {usedDevice} '{DescribeDevice(usedDevice)}' instead.");
                break;
            }

            if (!initialized)
            {
                var lastError = Bass.LastError;
                Log.Error($"[AudioMixer] Failed to initialize BASS with all methods: {lastError}");
                LogEnvironmentInfo();
                _initializationFailed = true;
                return;
            }
            
            // Get actual device info after init
            Bass.GetInfo(out var info);
            
            // Set the mixer frequency to the device's actual sample rate
            AudioConfig.MixerFrequency = info.SampleRate;
            var freqSource = usedDeviceDefault ? "device default" : $"fallback ({usedFrequency}Hz requested)";
            Log.Debug($"[AudioMixer] BASS initialized - SampleRate: {info.SampleRate}Hz ({freqSource}), Device: {Bass.CurrentDevice}, Method: {initMethod}, Latency: {info.Latency}ms");
        }

        // Load BASS FLAC plugin for native FLAC support (better than Media Foundation)
        _flacPluginHandle = Bass.PluginLoad(OperatingSystem.IsWindows() ? "bassflac.dll" : "libbassflac.so");
        if (_flacPluginHandle == 0)
        {
            Log.Warning($"[AudioMixer] Failed to load BASS FLAC plugin: {Bass.LastError}. FLAC files will use Media Foundation fallback.");
        }
        else
        {
            Log.Gated.Audio($"[AudioMixer] BASS FLAC plugin loaded successfully: Handle={_flacPluginHandle}");
        }

        // Create global mixer (stereo output to soundcard)
        Log.Gated.Audio("[AudioMixer] Creating global mixer stream...");
        _globalMixerHandle = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.Float | BassFlags.MixerNonStop);
        if (_globalMixerHandle == 0)
        {
            Log.Error($"[AudioMixer] Failed to create global mixer: {Bass.LastError}");
            _initializationFailed = true;
            return;
        }
        Log.Gated.Audio($"[AudioMixer] Global mixer created: Handle={_globalMixerHandle}");

        // Create operator mixer (decode stream that feeds into global mixer)
        Log.Gated.Audio("[AudioMixer] Creating operator mixer stream...");
        _operatorMixerHandle = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
        if (_operatorMixerHandle == 0)
        {
            Log.Error($"[AudioMixer] Failed to create operator mixer: {Bass.LastError}");
            _initializationFailed = true;
            return;
        }
        Log.Gated.Audio($"[AudioMixer] Operator mixer created: Handle={_operatorMixerHandle}");

        // Create soundtrack mixer (decode stream that feeds into global mixer)
        Log.Gated.Audio("[AudioMixer] Creating soundtrack mixer stream...");
        _soundtrackMixerHandle = BassMix.CreateMixerStream(AudioConfig.MixerFrequency, 2, BassFlags.MixerNonStop | BassFlags.Decode | BassFlags.Float);
        if (_soundtrackMixerHandle == 0)
        {
            Log.Error($"[AudioMixer] Failed to create soundtrack mixer: {Bass.LastError}");
            _initializationFailed = true;
            return;
        }
        Log.Gated.Audio($"[AudioMixer] Soundtrack mixer created: Handle={_soundtrackMixerHandle}");


        // Add operator mixer to global mixer with buffer flag for smooth mixing
        Log.Gated.Audio("[AudioMixer] Adding operator mixer to global mixer...");
        if (!BassMix.MixerAddChannel(_globalMixerHandle, _operatorMixerHandle, BassFlags.MixerChanBuffer))
        {
            Log.Error($"[AudioMixer] Failed to add operator mixer to global mixer: {Bass.LastError}");
        }
        else
        {
            Log.Gated.Audio("[AudioMixer] Operator mixer added to global mixer successfully");
        }

        // Add soundtrack mixer to global mixer
        // Use MixerChanBuffer to enable level metering via BassMix.ChannelGetLevel
        Log.Gated.Audio("[AudioMixer] Adding soundtrack mixer to global mixer...");
        if (!BassMix.MixerAddChannel(_globalMixerHandle, _soundtrackMixerHandle, BassFlags.MixerChanBuffer))
        {
            Log.Error($"[AudioMixer] Failed to add soundtrack mixer to global mixer: {Bass.LastError}");
        }
        else
        {
            Log.Gated.Audio("[AudioMixer] Soundtrack mixer added to global mixer successfully");
        }

        // Note: Offline mixer is NOT added to global mixer - it's completely isolated

        // Start the global mixer playing (outputs to soundcard)
        Log.Gated.Audio("[AudioMixer] Starting global mixer playback...");
        if (!Bass.ChannelPlay(_globalMixerHandle))
        {
            Log.Error($"[AudioMixer] Failed to start global mixer: {Bass.LastError}");
        }
        else
        {
            var playbackState = Bass.ChannelIsActive(_globalMixerHandle);
            Log.Gated.Audio($"[AudioMixer] Global mixer started, State: {playbackState}");
        }

        _initialized = true;
        Log.Gated.Audio("[AudioMixer] ✓ Audio mixer system initialized successfully with low-latency settings.");
        } // end lock
    }

    internal static void Shutdown()
    {
        lock (_initLock)
        {
            if (!_initialized && !_initializationFailed)
                return;

            Log.Gated.Audio("[AudioMixer] Shutting down...");
            
            Bass.StreamFree(_operatorMixerHandle);
            Bass.StreamFree(_soundtrackMixerHandle);
            Bass.StreamFree(_globalMixerHandle);
            
            _operatorMixerHandle = 0;
            _soundtrackMixerHandle = 0;
            _globalMixerHandle = 0;
            
            // Unload FLAC plugin
            if (_flacPluginHandle != 0)
            {
                Bass.PluginFree(_flacPluginHandle);
                _flacPluginHandle = 0;
            }
            
            Bass.Free();

            _resetGeneration++;
            _initialized = false;
            _initializationFailed = false; // Reset so initialization can be retried after device change
            Log.Gated.Audio("[AudioMixer] Audio mixer system shut down.");
        }
    }

    public static void SetOperatorMixerVolume(float volume)
    {
        if (!_initialized) return;
        Bass.ChannelSetAttribute(_operatorMixerHandle, ChannelAttribute.Volume, volume);
    }

    public static void SetSoundtrackMixerVolume(float volume)
    {
        if (!_initialized) return;
        Bass.ChannelSetAttribute(_soundtrackMixerHandle, ChannelAttribute.Volume, volume);
    }

    internal static void SetGlobalVolume(float volume)
    {
        if (!_initialized) return;
        Bass.ChannelSetAttribute(_globalMixerHandle, ChannelAttribute.Volume, volume);
    }

    internal static void SetGlobalMute(bool mute)
    {
        if (!_initialized) return;
        if (mute)
        {
            // Store the current volume before muting, but only if not already muted
            Bass.ChannelGetAttribute(_globalMixerHandle, ChannelAttribute.Volume, out var currentVolume);
            if (currentVolume > 0.001f)
            {
                _globalMixerVolume = currentVolume;
            }
            Bass.ChannelSetAttribute(_globalMixerHandle, ChannelAttribute.Volume, 0f);
        }
        else
        {
            // Always restore the current CoreSettings volume (user may have changed it while muted)
            float definedVolume = 1.0f;
            try
            {
                definedVolume = IO.CoreSettings.Config.AppVolume;
            }
            catch
            {
                // ignored
            }

            Bass.ChannelSetAttribute(_globalMixerHandle, ChannelAttribute.Volume, definedVolume);
        }
    }

    private static float _operatorMixerVolume = 1.0f;

    internal static void SetOperatorMute(bool mute)
    {
        if (!_initialized) return;
        if (mute)
        {
            // Store the current volume before muting, but only if not already muted
            Bass.ChannelGetAttribute(_operatorMixerHandle, ChannelAttribute.Volume, out var currentVolume);
            if (currentVolume > 0.001f)
            {
                _operatorMixerVolume = currentVolume;
            }
            Bass.ChannelSetAttribute(_operatorMixerHandle, ChannelAttribute.Volume, 0f);
        }
        else
        {
            // Always restore the current project settings volume (user may have changed it while muted)
            float definedVolume = 1.0f;
            try
            {
                definedVolume = CompositionSettings.Current.Audio.OperatorVolume;
            }
            catch
            {
                // ignored
            }

            Bass.ChannelSetAttribute(_operatorMixerHandle, ChannelAttribute.Volume, definedVolume);
        }
    }

    /// <summary>
    /// Creates a decode-only stream for offline analysis (waveform image generation, FFT, etc.)
    /// This stream is NOT connected to any output and will not interfere with playback.
    /// The caller is responsible for freeing the stream with Bass.StreamFree() when done.
    /// </summary>
    /// <param name="filePath">Absolute path to the audio file</param>
    /// <returns>Stream handle, or 0 if creation failed</returns>
    public static int CreateOfflineAnalysisStream(string filePath)
    {
        if (!BassLibrary.IsAvailable)
            return 0;

        // Ensure BASS is initialized
        if (!_initialized)
        {
            Initialize();
        }

        lock (_offlineStreamLock)
        {
            // Create a decode-only stream (no output to soundcard)
            var stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Prescan | BassFlags.Float);
            if (stream == 0)
            {
                var error = Bass.LastError;
                Log.Warning($"[AudioMixer] Failed to create offline analysis stream for '{filePath}': {error}");
                return 0;
            }

            Log.Gated.Audio($"[AudioMixer] Created offline analysis stream: Handle={stream} for '{filePath}'");
            return stream;
        }
    }

    /// <summary>
    /// Reads the duration of an audio file in seconds by opening a temporary decode stream.
    /// Returns 0 on failure. Used by the timeline-drop handler to size a new clip's TimeRange
    /// at the moment of import, without going through the full playback path.
    /// </summary>
    public static double TryProbeAudioDurationSecs(string filePath)
    {
        var stream = CreateOfflineAnalysisStream(filePath);
        if (stream == 0)
            return 0;
        try
        {
            var bytes = Bass.ChannelGetLength(stream);
            if (bytes < 0)
                return 0;
            return Bass.ChannelBytes2Seconds(stream, bytes);
        }
        finally
        {
            FreeOfflineAnalysisStream(stream);
        }
    }

    /// <summary>
    /// Frees an offline analysis stream created by CreateOfflineAnalysisStream.
    /// </summary>
    public static void FreeOfflineAnalysisStream(int streamHandle)
    {
        if (streamHandle == 0)
            return;

        lock (_offlineStreamLock)
        {
            Bass.StreamFree(streamHandle);
            Log.Gated.Audio($"[AudioMixer] Freed offline analysis stream: Handle={streamHandle}");
        }
    }

    /// <summary>
    /// Gets the current audio level from the global mixer (0.0 to 1.0 normalized).
    /// Returns the maximum of left and right channels.
    /// </summary>
    /// <remarks>
    /// Uses the level-ex variant of Bass.ChannelGetLevel with a configurable time window
    /// (see <see cref="AudioConfig.LevelMeteringWindowSeconds"/>). This provides windowed RMS-style
    /// metering rather than instantaneous peak levels, which is better for visual meter displays.
    /// This method does not consume audio data from the stream.
    /// </remarks>
    public static float GetGlobalMixerLevel()
    {
        if (!_initialized || _globalMixerHandle == 0)
            return 0f;

        // Use ChannelGetLevel (level-ex variant) with configurable time window for responsive metering.
        // The float[] overload returns RMS levels over the specified window, normalized to 0.0-1.0.
        float[] levels = new float[2];
        if (!Bass.ChannelGetLevel(_globalMixerHandle, levels, AudioConfig.LevelMeteringWindowSeconds, LevelRetrievalFlags.Stereo))
            return 0f;

        return Math.Max(levels[0], levels[1]);
    }

    /// <summary>
    /// Gets the current audio level from the operator mixer (0.0 to 1.0 normalized).
    /// Returns the maximum of left and right channels.
    /// </summary>
    /// <remarks>
    /// Uses BassMix.ChannelGetLevel for decode streams added to a mixer with MixerChanBuffer flag.
    /// This reads from the mixer's internal buffer without consuming audio data.
    /// Note: Decode streams require BassMix.ChannelGetLevel, not Bass.ChannelGetLevel.
    /// </remarks>
    public static float GetOperatorMixerLevel()
    {
        if (!_initialized || _operatorMixerHandle == 0)
            return 0f;

        // For decode streams with MixerChanBuffer, use BassMix.ChannelGetLevel (integer variant)
        // The float[] level-ex variant doesn't work correctly for decode streams
        var level = BassMix.ChannelGetLevel(_operatorMixerHandle);
        if (level == -1)
            return 0f;

        // Low 16 bits = left channel, high 16 bits = right channel (0-32768 range)
        var left = (level & 0xFFFF) / 32768f;
        var right = ((level >> 16) & 0xFFFF) / 32768f;
        return Math.Max(left, right);
    }

    /// <summary>
    /// Gets the current audio level from the soundtrack mixer (0.0 to 1.0 normalized).
    /// Returns the maximum of left and right channels.
    /// </summary>
    /// <remarks>
    /// Uses BassMix.ChannelGetLevel for decode streams added to a mixer with MixerChanBuffer flag.
    /// This reads from the mixer's internal buffer without consuming audio data.
    /// Note: Decode streams require BassMix.ChannelGetLevel, not Bass.ChannelGetLevel.
    /// </remarks>
    public static float GetSoundtrackMixerLevel()
    {
        if (!_initialized || _soundtrackMixerHandle == 0)
            return 0f;

        // For decode streams with MixerChanBuffer, use BassMix.ChannelGetLevel (integer variant)
        // The float[] level-ex variant doesn't work correctly for decode streams
        var level = BassMix.ChannelGetLevel(_soundtrackMixerHandle);
        if (level == -1)
            return 0f;

        // Low 16 bits = left channel, high 16 bits = right channel (0-32768 range)
        var left = (level & 0xFFFF) / 32768f;
        var right = ((level >> 16) & 0xFFFF) / 32768f;
        return Math.Max(left, right);
    }

    /// <summary>
    /// Queries the default output device's sample rate with a throwaway BASS init: BASS reports the device's
    /// output rate in its info, while enumerating WASAPI endpoints (the previous approach) blocks for seconds on
    /// machines with many or disconnected devices.
    /// </summary>
    /// <returns>The device sample rate in Hz, or 0 if it couldn't be determined.</returns>
    /// <summary>
    /// Devices to try for initialization, best first. -1 lets BASS pick, which is the right answer on
    /// Windows and macOS, so those get nothing else.
    /// </summary>
    /// <remarks>
    /// On Linux BASS's "Default" entry follows ALSA's <c>default</c> PCM, and under PipeWire or PulseAudio
    /// that can resolve to a dmix slave which refuses to open - init then fails with
    /// <see cref="Errors.Driver"/> on a machine that plainly has working output. The sound server's own
    /// device does work, so it is preferred over a raw card: opening a <c>hw:</c> device directly takes
    /// exclusive hold of it and bypasses whatever the user is mixing with.
    /// </remarks>
    private static List<int> GetDeviceCandidates()
    {
        var candidates = new List<int> { -1 };
        if (OperatingSystem.IsWindows())
            return candidates;

        var soundServers = new List<int>();
        var cards = new List<int>();

        // Device 0 is BASS's "No sound" device, and 1 is the default that -1 already stands for.
        for (var i = 2; Bass.GetDeviceInfo(i, out var info); i++)
        {
            if (!info.IsEnabled)
                continue;

            var driver = info.Driver ?? string.Empty;
            if (driver.Contains("pipewire", StringComparison.OrdinalIgnoreCase)
                || driver.Contains("pulse", StringComparison.OrdinalIgnoreCase))
            {
                soundServers.Add(i);
            }
            else
            {
                cards.Add(i);
            }
        }

        candidates.AddRange(soundServers);
        candidates.AddRange(cards);
        return candidates;
    }

    /// <summary>The device's reported name, for a log line that says which one was settled on.</summary>
    private static string DescribeDevice(int device)
        => Bass.GetDeviceInfo(device, out var info) && !string.IsNullOrEmpty(info.Name) ? info.Name : $"#{device}";

    private static int GetDefaultOutputSampleRate()
    {
        try
        {
            // Same candidate order as the real init, so the rate measured here belongs to the device that
            // will actually be opened rather than to one that cannot be.
            var opened = -1;
            foreach (var device in GetDeviceCandidates())
            {
                if (!Bass.Init(device, 48000, DeviceInitFlags.Default, IntPtr.Zero))
                    continue;

                opened = device;
                break;
            }

            if (opened == -1)
            {
                Log.Debug($"[AudioMixer] Probe init failed: {Bass.LastError}");
                return 0;
            }

            Bass.GetInfo(out var info);
            Bass.Free();
            var sampleRate = info.SampleRate;
            Log.Debug($"[AudioMixer] Default output device runs at {sampleRate}Hz");
            return sampleRate;
        }
        catch (Exception ex)
        {
            Log.Debug($"[AudioMixer] Failed to query device sample rate: {ex.Message}");
        }

        return 0; // Couldn't determine
    }

    /// <summary>
    /// Logs environment info to help diagnose BASS initialization failures.
    /// </summary>
    private static void LogEnvironmentInfo()
    {
        Log.Error("[AudioMixer] Environment info for diagnosis:");
        Log.Error($"  OS: {Environment.OSVersion}");
        Log.Error($"  64-bit OS: {Environment.Is64BitOperatingSystem}, 64-bit Process: {Environment.Is64BitProcess}");
        Log.Error($"  Current Directory: {Environment.CurrentDirectory}");
        
        try
        {
            // Log available audio devices
            int deviceCount = Bass.DeviceCount;
            Log.Error($"  BASS Device Count: {deviceCount}");
            for (int i = 0; i < deviceCount; i++)
            {
                if (Bass.GetDeviceInfo(i, out var deviceInfo))
                {
                    Log.Error($"    Device[{i}]: '{deviceInfo.Name}' Type={deviceInfo.Type} Enabled={deviceInfo.IsEnabled} Default={deviceInfo.IsDefault}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"  Failed to enumerate BASS devices: {ex.Message}");
        }
        
        try
        {
            // Natives load from the app folder, not the working directory (which a launcher may set anywhere).
            var bassFileName = OperatingSystem.IsWindows() ? "bass.dll" : "libbass.so";
            var bassDllExists = System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, bassFileName));
            Log.Error($"  {bassFileName} exists in app dir: {bassDllExists}");
            
            var bassMixFileName = OperatingSystem.IsWindows() ? "bassmix.dll" : "libbassmix.so";
            var bassMixDllExists = System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, bassMixFileName));
            Log.Error($"  {bassMixFileName} exists in app dir: {bassMixDllExists}");
        }
        catch (Exception ex)
        {
            Log.Error($"  Failed to check DLL existence: {ex.Message}");
        }
    }
}

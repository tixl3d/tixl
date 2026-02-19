#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using ManagedBass;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using T3.Core.Logging;

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
            var initFlags = DeviceInitFlags.Latency | DeviceInitFlags.Stereo | DeviceInitFlags.Device3D;
            
            // Build frequency list: device rate first (if known), then common fallbacks
            var frequenciesToTry = new List<int>();
            if (deviceSampleRate > 0)
            {
                frequenciesToTry.Add(deviceSampleRate);
            }
            // Add common fallbacks that aren't already in the list
            if (deviceSampleRate != 48000) frequenciesToTry.Add(48000);
            if (deviceSampleRate != 44100) frequenciesToTry.Add(44100);
            
            bool initialized = false;
            int usedFrequency = 0;
            bool usedDeviceDefault = false;
            string initMethod = "LATENCY";
            
            foreach (var freq in frequenciesToTry)
            {
                bool isDeviceRate = (freq == deviceSampleRate && deviceSampleRate > 0);
                var freqDesc = isDeviceRate ? $"{freq}Hz (device)" : $"{freq}Hz (fallback)";
                Log.Gated.Audio($"[AudioMixer] Attempting BASS.Init with Latency+Stereo at {freqDesc}...");
                
                if (Bass.Init(-1, freq, initFlags, IntPtr.Zero))
                {
                    Log.Gated.Audio($"[AudioMixer] BASS initialized with LATENCY flag at {freqDesc}");
                    initialized = true;
                    usedFrequency = freq;
                    usedDeviceDefault = isDeviceRate;
                    initMethod = "LATENCY";
                    break;
                }
                
                var error1 = Bass.LastError;
                // If already initialized, that's fine - continue with existing init
                if (error1 == Errors.Already)
                {
                    Log.Gated.Audio("[AudioMixer] BASS already initialized");
                    initialized = true;
                    usedDeviceDefault = true; // Assume existing init used device default
                    initMethod = "EXISTING";
                    break;
                }
                
                Log.Gated.Audio($"{error1} [AudioMixer] Init at {freqDesc} failed, trying next...");
            }
            
            // If all frequencies failed with Latency flag, try without it
            if (!initialized)
            {
                foreach (var freq in frequenciesToTry)
                {
                    bool isDeviceRate = (freq == deviceSampleRate && deviceSampleRate > 0);
                    var freqDesc = isDeviceRate ? $"{freq}Hz (device)" : $"{freq}Hz (fallback)";
                    
                    if (Bass.Init(-1, freq, DeviceInitFlags.Stereo | DeviceInitFlags.Device3D, IntPtr.Zero))
                    {
                        Log.Warning($"[AudioMixer] BASS initialized with STEREO+3D flag at {freqDesc} (no latency optimization)");
                        initialized = true;
                        usedFrequency = freq;
                        usedDeviceDefault = isDeviceRate;
                        initMethod = "STEREO+3D";
                        break;
                    }
                    
                    if (Bass.LastError == Errors.Already)
                    {
                        initialized = true;
                        usedDeviceDefault = true;
                        initMethod = "EXISTING";
                        break;
                    }
                }
            }
            
            // Last resort - basic init with 3D
            if (!initialized)
            {
                foreach (var freq in frequenciesToTry)
                {
                    bool isDeviceRate = (freq == deviceSampleRate && deviceSampleRate > 0);
                    var freqDesc = isDeviceRate ? $"{freq}Hz (device)" : $"{freq}Hz (fallback)";
                    
                    if (Bass.Init(-1, freq, DeviceInitFlags.Default | DeviceInitFlags.Device3D, IntPtr.Zero))
                    {
                        Log.Warning($"[AudioMixer] BASS initialized with DEFAULT+3D flags at {freqDesc}");
                        initialized = true;
                        usedFrequency = freq;
                        usedDeviceDefault = isDeviceRate;
                        initMethod = "DEFAULT+3D";
                        break;
                    }
                    
                    if (Bass.LastError == Errors.Already)
                    {
                        initialized = true;
                        usedDeviceDefault = true;
                        initMethod = "EXISTING";
                        break;
                    }
                }
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
        _flacPluginHandle = Bass.PluginLoad("bassflac.dll");
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
            // Always restore the current ProjectSettings volume (user may have changed it while muted)
            float definedVolume = 1.0f;
            try
            {
                definedVolume = IO.ProjectSettings.Config.GlobalPlaybackVolume;
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
            // Always restore the current ProjectSettings volume (user may have changed it while muted)
            float definedVolume = 1.0f;
            try
            {
                definedVolume = IO.ProjectSettings.Config.OperatorPlaybackVolume;
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
    /// Queries WASAPI to get the default output device's configured sample rate.
    /// This works before BASS is initialized.
    /// </summary>
    /// <returns>The device sample rate in Hz, or 0 if it couldn't be determined.</returns>
    private static int GetDefaultOutputSampleRate()
    {
        try
        {
            // Enumerate WASAPI devices to find the default output's loopback
            // Loopback devices represent the output and have the correct MixFrequency
            var deviceCount = BassWasapi.DeviceCount;
            
            for (var i = 0; i < deviceCount; i++)
            {
                var info = BassWasapi.GetDeviceInfo(i);
                
                // Look for enabled loopback device (represents system output)
                if (info.IsEnabled && info.IsLoopback && !info.IsInput)
                {
                    var sampleRate = info.MixFrequency;
                    Log.Debug($"[AudioMixer] Found default output device: '{info.Name}' at {sampleRate}Hz");
                    return sampleRate;
                }
            }
            
            // Fallback: try to find any enabled loopback device
            for (var i = 0; i < deviceCount; i++)
            {
                var info = BassWasapi.GetDeviceInfo(i);
                if (info.IsEnabled && info.IsLoopback)
                {
                    var sampleRate = info.MixFrequency;
                    Log.Debug($"[AudioMixer] Found loopback device: '{info.Name}' at {sampleRate}Hz");
                    return sampleRate;
                }
            }
            
            Log.Debug("[AudioMixer] Could not find default output device sample rate from WASAPI");
        }
        catch (Exception ex)
        {
            Log.Debug($"[AudioMixer] Failed to query WASAPI device sample rate: {ex.Message}");
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
            // Check for bass.dll
            var bassDllPath = System.IO.Path.Combine(Environment.CurrentDirectory, "bass.dll");
            var bassDllExists = System.IO.File.Exists(bassDllPath);
            Log.Error($"  bass.dll exists in current dir: {bassDllExists}");
            
            var bassMixDllPath = System.IO.Path.Combine(Environment.CurrentDirectory, "bassmix.dll");
            var bassMixDllExists = System.IO.File.Exists(bassMixDllPath);
            Log.Error($"  bassmix.dll exists in current dir: {bassMixDllExists}");
        }
        catch (Exception ex)
        {
            Log.Error($"  Failed to check DLL existence: {ex.Message}");
        }
    }
}

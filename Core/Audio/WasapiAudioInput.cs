using System;
using System.Collections.Generic;
using System.Linq;
using ManagedBass;
using ManagedBass.Wasapi;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Provides Windows Audio Session API (WASAPI) audio input handling for real-time audio capture.
/// Uses the WASAPI audio API to get audio reaction from devices like speakers and microphones.
/// Supports both loopback (system audio) and input device capture.
/// </summary>
/// <remarks>
/// This class manages the lifecycle of WASAPI audio capture, including device enumeration,
/// initialization, and frame-by-frame audio processing. It integrates with the playback system
/// to provide FFT analysis data and audio level metering.
/// </remarks>
public static class WasapiAudioInput
{
    /// <summary>
    /// Processes audio input at the start of each frame.
    /// Handles device switching, capture restart on failure, and stops capture when not using external audio.
    /// </summary>
    /// <param name="settings">The playback settings containing audio configuration. If null, no action is taken.</param>
    /// <remarks>
    /// This method should be called once per frame. It manages the following:
    /// <list type="bullet">
    /// <item>Stops capture if audio source is not set to external device</item>
    /// <item>Attempts to restart capture if previous FFT data fetch failed</item>
    /// <item>Initializes capture for the specified input device</item>
    /// </list>
    /// </remarks>
    public static void StartFrame(PlaybackSettings settings)
    {
        if (settings == null)
            return;
                    
        if (settings.AudioSource != PlaybackSettings.AudioSources.ExternalDevice)
        {
            if (!string.IsNullOrEmpty(ActiveInputDeviceName))
            {
                Stop();
            }
            return ;
        }

        var deviceName = settings.AudioInputDeviceName;
        if (ActiveInputDeviceName == deviceName)
        {
            // Try to restart capture
            if(!_failedToGetLastFffData)
                return;

            Log.Debug("Trying to restart WASAPI...");
            _failedToGetLastFffData = false;
        }
            
        if (string.IsNullOrEmpty(deviceName))
        {
            if (_complainedOnce)
                return ;
                
            Log.Warning("Can't switch to WASAPI device without a name");
            _complainedOnce = true;
            return ;
        }
        
        var device = InputDevices.FirstOrDefault(d => d.DeviceInfo.Name == deviceName);
        if (device == null)
        {
            Log.Warning($"Can't find input device {deviceName}");
            _complainedOnce = true;
            return ;
        }

        StartInputCapture(device);
        _complainedOnce = false;
    }
    
    /// <summary>
    /// Gets the list of available WASAPI input devices.
    /// </summary>
    /// <value>
    /// A list of <see cref="WasapiInputDevice"/> instances representing available audio input devices.
    /// The list is lazily initialized on first access.
    /// </value>
    public static List<WasapiInputDevice> InputDevices
    {
        get
        {
            if (_inputDevices == null)
                InitializeInputDeviceList();

            return _inputDevices;
        }
    }

    /// <summary>
    /// Initializes and starts WASAPI audio capture for the specified device.
    /// </summary>
    /// <param name="device">
    /// The WASAPI input device to capture from. If null, attempts to use the default input device.
    /// </param>
    /// <remarks>
    /// Configures WASAPI with the device's native mix frequency and minimum update period.
    /// It ensures BASS is initialized before WASAPI setup and registers <see cref="ProcessDataCallback"/>
    /// for asynchronous audio data processing.
    /// </remarks>
    private static void StartInputCapture(WasapiInputDevice device)
    {
        // Ensure BASS is initialized before WASAPI
        // WASAPI requires a valid BASS device to work properly
        AudioMixerManager.Initialize();
        
        var inputDeviceIndex = BassWasapi.DefaultInputDevice;

        if (device == null)
        {
            if (_inputDevices.Count == 0)
            {
                Log.Error("No wasapi input devices found");
                return;
            }

            Log.Error($"Attempting default input {BassWasapi.DefaultInputDevice}.");
            device = _inputDevices[0];
        }
        else
        {
            Log.Info($"Initializing WASAPI audio input for  {device.DeviceInfo.Name}... ");
            inputDeviceIndex = device.WasapiDeviceIndex;
        }

        SampleRate = device.DeviceInfo.MixFrequency;

        BassWasapi.Stop();
        BassWasapi.Free();
        if (!BassWasapi.Init(inputDeviceIndex,
                             Frequency: device.DeviceInfo.MixFrequency,
                             Channels: 0,
                             //Flags: WasapiInitFlags.Buffer | WasapiInitFlags.Exclusive,
                             Flags: WasapiInitFlags.Buffer,
                             Buffer: (float)device.DeviceInfo.MinimumUpdatePeriod*4,
                             Period: (float)device.DeviceInfo.MinimumUpdatePeriod,
                             Procedure: ProcessDataCallback,
                             User: IntPtr.Zero))
        {
            Log.Error("Can't initialize WASAPI:" + Bass.LastError);
            return;
        }

        ActiveInputDeviceName = device.DeviceInfo.Name;
        BassWasapi.Start();
    }
        
    /// <summary>
    /// Stops the WASAPI audio capture and releases associated resources.
    /// </summary>
    private static void Stop()
    {
        //Log.Debug("Wasapi.Stop()");
        BassWasapi.Stop();
        BassWasapi.Free();
        ActiveInputDeviceName = null;
    }

    /// <summary>
    /// Flag indicating whether a warning about missing device name has been logged.
    /// Used to prevent repeated log spam.
    /// </summary>
    private static bool _complainedOnce;
    
    /// <summary>
    /// Enumerates and initializes the list of available WASAPI input devices.
    /// </summary>
    /// <remarks>
    /// Populates <see cref="_inputDevices"/> with all enabled input and loopback devices.
    /// Requires BASS to be initialized before enumeration.
    /// </remarks>
    private static void InitializeInputDeviceList()
    {
        _inputDevices = [];

        // Ensure BASS is initialized before enumerating WASAPI devices
        AudioMixerManager.Initialize();
        
        // Keep in local variable to avoid double evaluation
        var deviceCount = BassWasapi.DeviceCount;

        for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
        {
            var deviceInfo = BassWasapi.GetDeviceInfo(deviceIndex);
            var isValidInputDevice = deviceInfo.IsEnabled && (deviceInfo.IsLoopback || deviceInfo.IsInput);

            if (!isValidInputDevice)
                continue;

            Log.Debug($"Found Wasapi input ID:{_inputDevices.Count} {deviceInfo.Name} LoopBack:{deviceInfo.IsLoopback} IsInput:{deviceInfo.IsInput} (at {deviceIndex})");
            _inputDevices.Add(new WasapiInputDevice()
                                  {
                                      WasapiDeviceIndex = deviceIndex,
                                      DeviceInfo = deviceInfo,
                                  });
        }
    }

    /// <summary>
    /// Callback invoked asynchronously by WASAPI when new audio data arrives.
    /// Processes FFT data, audio levels, and triggers audio analysis updates.
    /// </summary>
    /// <param name="buffer">Pointer to the audio sample buffer.</param>
    /// <param name="length">The length of the buffer in bytes.</param>
    /// <param name="user">User data pointer (unused).</param>
    /// <returns>The length parameter, indicating all data was processed.</returns>
    /// <remarks>
    /// This callback may be invoked multiple times per frame. It handles:
    /// <list type="bullet">
    /// <item>Waveform sample buffer updates (skipped during file export)</item>
    /// <item>FFT gain buffer population for audio analysis</item>
    /// <item>Stereo level calculation for metering</item>
    /// <item>Beat synchronization updates when enabled</item>
    /// </list>
    /// </remarks>
    private static int ProcessDataCallback(IntPtr buffer, int length, IntPtr user)
    {
        // Skip all WASAPI processing during export - AudioRendering handles FFT/waveform
        if (Playback.Current.IsRenderingToFile)
            return length;
        
        var time = Playback.RunTimeInSecs;  // Keep because timer is still running 
        TimeSinceLastUpdate = time - LastUpdateTime;
        LastUpdateTime = time;

        // Skip waveform buffer updates during export - AudioRendering handles this
        if (WaveFormProcessing.RequestedOnce && !Playback.Current.IsRenderingToFile)
        {
            var sizeInBytes = AudioConfig.WaveformSampleCount << 2 << 1;
            WaveFormProcessing.LastFetchResultCode = BassWasapi.GetData(WaveFormProcessing.InterleavenSampleBuffer,  
                                                                        sizeInBytes);
        }
        
        var resultCode = BassWasapi.GetData(AudioAnalysis.FftGainBuffer, (int)(AudioAnalysis.BassFlagForFftBufferSize | DataFlags.FFTRemoveDC));
        _failedToGetLastFffData = resultCode < 0;
        if (_failedToGetLastFffData)
        {
            Log.Debug($"Can't get Wasapi FFT-Data: {Bass.LastError}");
            return length;
        }
        
        // level is an int32 carrying per-channel level, such as: "0xRRRRLLLL"
        // convert to M/S, and scale it.
        // more info : https://documentation.help/BASSWASAPI/BASS_WASAPI_GetLevel.html
        var level = BassWasapi.GetLevel();
        if (level != -1) // exactly -1 is a capture error, do not measure it
        {
            var left = level & 0xffff;
            var right = (level >> 16) & 0xffff;
            _lastAudioLevel = (left + right + Math.Abs(left-right)) * short.MaxValue * 0.00001f;
        }

        var playbackSettings = Playback.Current?.Settings;
        
        if (playbackSettings == null) 
            return length;
        
        AudioAnalysis.ProcessUpdate(playbackSettings.AudioGainFactor,
                                    playbackSettings.AudioDecayFactor);

        if (playbackSettings.EnableAudioBeatLocking)
        {
            BeatSynchronizer.UpdateBeatTimer();
        }
        
        return length;
    }

    /// <summary>
    /// Flag indicating whether the last FFT data fetch from WASAPI failed.
    /// Used to trigger capture restart on the next frame.
    /// </summary>
    private static bool _failedToGetLastFffData;

    /// <summary>
    /// Represents a WASAPI audio input device with its associated device information.
    /// </summary>
    public sealed class WasapiInputDevice
    {
        /// <summary>
        /// The internal WASAPI device index used for initialization.
        /// </summary>
        internal int WasapiDeviceIndex;
        
        /// <summary>
        /// Contains detailed device information including name, mix frequency, and capabilities.
        /// </summary>
        public WasapiDeviceInfo DeviceInfo;
    }

    /// <summary>
    /// Internal cache of enumerated WASAPI input devices.
    /// </summary>
    private static List<WasapiInputDevice> _inputDevices;
    
    /// <summary>
    /// The time in seconds since the last audio data update callback.
    /// </summary>
    internal static double TimeSinceLastUpdate;
    
    /// <summary>
    /// The playback time in seconds when the last audio update occurred.
    /// </summary>
    internal static double LastUpdateTime;
    
    /// <summary>
    /// The sample rate in Hz of the currently active audio capture device.
    /// Defaults to 48000 Hz if device sample rate cannot be determined.
    /// </summary>
    internal static int SampleRate;

    /// <summary>
    /// Initializes the sample rate from the audio system.
    /// </summary>
    /// <remarks>
    /// Attempts to get the device sample rate from <see cref="AudioMixerManager"/>.
    /// Falls back to 48000 Hz if initialization fails.
    /// </remarks>
    static WasapiAudioInput()
    {
        try
        {
            // Ensure AudioMixerManager has a chance to query the device sample rate.
            AudioMixerManager.Initialize();

            var deviceRate = AudioConfig.MixerFrequency; // AudioMixerManager sets this during Initialize
            SampleRate = deviceRate > 0 ? deviceRate : 48000; //Set to device rate if valid, fallback to 48kHz if not
        }
        catch (Exception ex)
        {
            Log.Debug($"WasapiAudioInput: Failed to initialize sample rate from AudioMixerManager: {ex.Message}");
            SampleRate = 48000;
        }
    }

    /// <summary>
    /// Gets the name of the currently active WASAPI input device.
    /// </summary>
    /// <value>
    /// The device name string if capture is active; otherwise, <c>null</c>.
    /// </value>
    public static string ActiveInputDeviceName { get; private set; }
    
    /// <summary>
    /// The raw audio level value from the last WASAPI level measurement.
    /// </summary>
    private static float _lastAudioLevel;
    
    /// <summary>
    /// Gets a time-decayed audio level value suitable for visual metering. (gain meter in playback settings)
    /// </summary>
    /// <value>
    /// A float value representing the decaying audio level, calculated from the last
    /// measured level divided by the elapsed time since measurement.
    /// </value>
    /// <remarks>
    /// This property is primarily used for the gain meter display in the playback settings dialog.
    /// The decay creates a smooth falloff effect for visual representation.
    /// </remarks>
    public static float DecayingAudioLevel => (float)(_lastAudioLevel / Math.Max(1, (Playback.RunTimeInSecs - LastUpdateTime) * 100));
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManagedBass;
using T3.Core.Animation;
using T3.Core.Audio.Timing;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Settings;

namespace T3.Core.Audio.Input;

/// <summary>
/// Captures audio from an input device so the graph can react to something other than the project's own
/// soundtrack, which <see cref="AudioEngine"/> analyses directly.
/// </summary>
/// <remarks>
/// Manages the lifecycle of capture - device enumeration, starting, and frame-by-frame processing - and
/// feeds FFT and level data to the playback system. Which devices exist, and whether loopback is among
/// them, is up to <see cref="AudioInputBackend"/>.
/// </remarks>
public static class AudioInput
{
    /// <summary>
    /// Processes audio input at the start of each frame.
    /// Handles device switching, capture restart on failure, and stops capture when not
    /// using external audio. Capture is also kept running while an audio recording
    /// session is active (<see cref="BeginRecording"/>), regardless of the
    /// <see cref="CompositionSettings.PlaybackConfig.AudioSource"/> setting — recording
    /// and FFT analysis are independent capture consumers.
    /// </summary>
    /// <param name="settings">The playback settings containing audio configuration. If null, no action is taken.</param>
    public static void StartFrame(CompositionSettings settings)
    {
        if (settings == null)
            return;

        var wantsCaptureForFft = settings.Playback.AudioSource == CompositionSettings.AudioSources.ExternalDevice;
        var wantsCaptureForRecording = _isCaptureNeededForRecording;

        // Without a usable backend there is no capture to start and nothing to stop.
        if ((wantsCaptureForFft || wantsCaptureForRecording) && !AudioInputBackend.Current.IsAvailable)
            return;

        if (!wantsCaptureForFft && !wantsCaptureForRecording)
        {
            if (!string.IsNullOrEmpty(ActiveInputDeviceName))
            {
                Stop();
            }
            return;
        }

        var deviceName = ResolveInputDeviceName(settings.Playback.AudioInputDeviceName);
        if (ActiveInputDeviceName == deviceName)
        {
            // Try to restart capture
            if(!_failedToGetLastFffData)
                return;

            Log.Debug("Trying to restart WASAPI...");
            _failedToGetLastFffData = false;
        }
            
        // No configured device name. For FFT capture this is an error (user has
        // ExternalDevice mode but no device picked). For recording-only capture
        // (AudioSource is ProjectSoundtrack but a record session is active), silently
        // fall back to the system default input so the recording still gets audio.
        if (string.IsNullOrEmpty(deviceName))
        {
            if (wantsCaptureForFft)
            {
                if (_complainedOnce)
                    return;

                Log.Warning("Can't switch to WASAPI device without a name");
                _complainedOnce = true;
                return;
            }

            // Recording-only path: pick the first available input device.
            if (InputDevices.Count == 0)
                return;

            StartInputCapture(InputDevices[0]);
            _complainedOnce = false;
            return;
        }

        var device = InputDevices.FirstOrDefault(d => d.Name == deviceName);
        if (device == null)
        {
            Log.Warning($"Can't find input device {deviceName}");
            _complainedOnce = true;
            return;
        }

        StartInputCapture(device);
        _complainedOnce = false;
    }
    
    /// <summary>
    /// Resolves the device a composition asks for into the actual device name to capture from.
    /// An empty project-level name means "use this machine's configured default input"
    /// (<see cref="CoreSettings.ConfigData.LocalAudioInputDeviceName"/>), which keeps shared
    /// projects portable. A non-empty name is an explicit per-project override.
    /// </summary>
    public static string ResolveInputDeviceName(string projectDeviceName)
    {
        return string.IsNullOrEmpty(projectDeviceName)
                   ? CoreSettings.Config.LocalAudioInputDeviceName ?? string.Empty
                   : projectDeviceName;
    }

    /// <summary>
    /// Gets the list of available WASAPI input devices.
    /// </summary>
    /// <value>
    /// A list of <see cref="AudioInputDevice"/> instances representing available audio input devices.
    /// The list is lazily initialized on first access.
    /// </value>
    public static List<AudioInputDevice> InputDevices
    {
        get
        {
            if (!AudioInputBackend.Current.IsAvailable)
                return _inputDevices ??= [];

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
    private static void StartInputCapture(AudioInputDevice device)
    {
        if (device == null)
        {
            if (_inputDevices.Count == 0)
            {
                Log.Error("No audio input devices found");
                return;
            }

            device = _inputDevices[0];
            Log.Error($"Attempting first input '{device.Name}'.");
        }
        else
        {
            Log.Info($"Initializing {AudioInputBackend.Current.Name} audio input for {device.Name}... ");
        }

        SampleRate = device.SampleRate;

        if (!AudioInputBackend.Current.TryStartCapture(device, OnCapturedData))
        {
            return;
        }

        ActiveInputDeviceName = device.Name;
    }
        
    /// <summary>
    /// Stops the WASAPI audio capture and releases associated resources.
    /// Any active recording session is finalised first so the WAV file isn't left
    /// with a placeholder header.
    /// </summary>
    private static void Stop()
    {
        //Log.Debug("Wasapi.Stop()");

        if (_activeRecording != null)
        {
            EndRecording();
        }

        AudioInputBackend.Current.StopCapture();
        ActiveInputDeviceName = null;
        DanceAiPhaseTracker.Reset();
    }

    /// <summary>
    /// Starts writing the live WASAPI capture to an uncompressed 16-bit PCM WAV file in
    /// <see cref="RecordingPaths.TempRecordingsDirectory"/>.
    /// </summary>
    /// <param name="suffix">Optional source identifier appended to the filename
    /// (e.g. <c>mic1</c>). Sanitised before use.</param>
    /// <returns>The absolute path of the WAV file, or <c>null</c> if recording could not start.</returns>
    public static string BeginRecording(int sessionIndex = -1, string suffix = null)
    {
        if (_activeRecording != null)
        {
            Log.Warning($"WAV recording already active at '{_activeRecording.Path}'. Call EndRecording first.");
            return null;
        }

        // Mark capture as needed for recording so StartFrame won't tear it down even if
        // the composition's AudioSource is something other than ExternalDevice. If WASAPI
        // isn't running yet, bring it up on demand using whatever input device is
        // configured — or fall back to the system default.
        _isCaptureNeededForRecording = true;
        if (string.IsNullOrEmpty(ActiveInputDeviceName))
        {
            EnsureCaptureRunningForRecording();
        }

        if (string.IsNullOrEmpty(ActiveInputDeviceName))
        {
            _isCaptureNeededForRecording = false;
            Log.Warning("Cannot start WAV recording: no WASAPI input device available.");
            return null;
        }

        var directory = RecordingPaths.TempRecordingsDirectory;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            Log.Warning($"Cannot create recordings directory '{directory}': {e.Message}");
            return null;
        }

        // -1 = compute independently; the recording session passes a shared index so the audio + data
        // files of one session line up (AudioRec-007 / DataRec-007).
        if (sessionIndex < 0)
            sessionIndex = RecordingPaths.NextSessionIndex(directory);
        var fileName = RecordingPaths.BuildFileName(RecordingPaths.AudioRecordingPrefix, sessionIndex, ".wav", suffix);
        var path = Path.Combine(directory, fileName);

        var sampleRate = SampleRate > 0 ? SampleRate : 48000;
        var channels = AudioInputBackend.Current.ActiveChannelCount > 0 ? AudioInputBackend.Current.ActiveChannelCount : 2;

        try
        {
            // Assign last so the capture callback only sees a fully constructed writer.
            var writer = new WavFileWriter(path, sampleRate, channels);
            _activeRecording = writer;
            Log.Debug($"WAV recording started: {path} ({sampleRate} Hz, {channels} ch)");
            return path;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to start WAV recording at '{path}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Stops the active WAV recording started by <see cref="BeginRecording"/> and
    /// finalises the file header so the WAV is readable.
    /// </summary>
    /// <returns>The path of the finalised file, or <c>null</c> if no recording was active.</returns>
    public static string EndRecording()
    {
        var writer = _activeRecording;
        if (writer == null)
            return null;

        // Clear the field before disposing so the callback stops feeding the writer
        // the moment it next observes the change. The writer's own lock guards against
        // a callback that may already be inside AppendFloat32Samples.
        _activeRecording = null;
        writer.Dispose();

        // Recording is done; let StartFrame tear capture down again on the next tick
        // (it will, if AudioSource isn't ExternalDevice). Holding capture alive until the
        // next frame avoids a stutter if BeginRecording is called again immediately.
        _isCaptureNeededForRecording = false;

        if (writer.BytesWritten == 0)
        {
            // A finalised-but-silent WAV means capture ran yet WASAPI never delivered frames —
            // typically another app holds the device in exclusive mode (e.g. Audacity), the input
            // is muted, or the wrong device is selected in Playback settings.
            Log.Warning($"Audio recording captured no samples — '{Path.GetFileName(writer.Path)}' is silent. "
                        + "Check that the input device isn't muted, that the right one is selected in Playback settings, "
                        + "and that no other app (e.g. Audacity in exclusive mode) is holding it.");
        }
        else
        {
            Log.Debug($"WAV recording stopped: {writer.Path} ({writer.DurationSeconds:F1} s, {writer.BytesWritten} bytes)");
        }

        return writer.Path;
    }

    /// <summary>
    /// Brings WASAPI capture up on demand when a recording is requested but no capture
    /// is currently active (because the composition's AudioSource isn't ExternalDevice).
    /// Picks the configured input device if one is set, else the first available.
    /// </summary>
    private static void EnsureCaptureRunningForRecording()
    {
        var configuredDeviceName = ResolveInputDeviceName(Playback.Current?.Settings?.Playback.AudioInputDeviceName);

        AudioInputDevice device = null;
        if (!string.IsNullOrEmpty(configuredDeviceName))
            device = InputDevices.FirstOrDefault(d => d.Name == configuredDeviceName);

        if (device == null && InputDevices.Count > 0)
            device = InputDevices[0];

        if (device == null)
        {
            Log.Warning("Cannot start WASAPI capture for recording: no input devices available.");
            return;
        }

        StartInputCapture(device);
    }

    /// <summary>True while a WAV recording session is active.</summary>
    public static bool IsRecording => _activeRecording != null;

    /// <summary>Path of the WAV file currently being written, or <c>null</c> if not recording.</summary>
    public static string ActiveRecordingPath => _activeRecording?.Path;

    /// <summary>
    /// Flag indicating whether a warning about missing device name has been logged.
    /// Used to prevent repeated log spam.
    /// </summary>
    private static bool _complainedOnce;

    /// <summary>
    /// True while a recording session needs WASAPI capture running, regardless of the
    /// composition's <see cref="CompositionSettings.PlaybackConfig.AudioSource"/>. Set by
    /// <see cref="BeginRecording"/>, cleared by <see cref="EndRecording"/>. Lets the
    /// FFT-input and recording paths use the WASAPI input independently — the OS allows
    /// simultaneous render-and-capture, so there's no hardware reason to couple them.
    /// </summary>
    private static bool _isCaptureNeededForRecording;
    
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
        AudioInputBackend.Current.EnumerateDevices(_inputDevices);
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
    private static void OnCapturedData(IntPtr buffer, int length)
    {
        // Skip all capture processing during export - AudioRendering handles FFT/waveform
        if (Playback.Current.IsRenderingToFile)
            return;

        // Stream the raw captured samples to the active WAV recording, if any.
        // Read directly from the callback's buffer instead of issuing another GetData()
        // call — GetData consumes BASS's internal queue and would compete with the
        // analysis paths below for the same samples.
        var writer = _activeRecording;
        if (writer != null && buffer != IntPtr.Zero && length > 0)
        {
            writer.AppendFloat32Samples(buffer, length);
        }

        var time = Playback.RunTimeInSecs;  // Keep because timer is still running
        TimeSinceLastUpdate = time - LastUpdateTime;
        LastUpdateTime = time;

        // Skip waveform buffer updates during export - AudioRendering handles this
        if (WaveFormProcessing.RequestedOnce && !Playback.Current.IsRenderingToFile)
        {
            var sizeInBytes = AudioConfig.WaveformSampleCount << 2 << 1;
            WaveFormProcessing.LastFetchResultCode = AudioInputBackend.Current.GetData(WaveFormProcessing.InterleavenSampleBuffer,
                                                                                       sizeInBytes);
        }
        
        var resultCode = AudioInputBackend.Current.GetData(AudioAnalysis.FftGainBuffer,
                                                           (int)(AudioAnalysis.BassFlagForFftBufferSize | DataFlags.FFTRemoveDC));
        _failedToGetLastFffData = resultCode < 0;
        if (_failedToGetLastFffData)
        {
            Log.Debug($"Can't get FFT data from the audio input: {Bass.LastError}");
            return;
        }
        
        // level is an int32 carrying per-channel level, such as: "0xRRRRLLLL"
        // convert to M/S, and scale it.
        // more info : https://documentation.help/BASSWASAPI/BASS_WASAPI_GetLevel.html
        var level = AudioInputBackend.Current.GetLevel();
        if (level != -1) // exactly -1 is a capture error, do not measure it
        {
            var left = level & 0xffff;
            var right = (level >> 16) & 0xffff;
            _lastAudioLevel = (left + right + Math.Abs(left-right)) * short.MaxValue * 0.00001f;
        }

        var playbackSettings = Playback.Current?.Settings;
        
        if (playbackSettings == null) 
            return;
        
        AudioAnalysis.ProcessUpdate(playbackSettings.Playback.AudioGainFactor,
                                    playbackSettings.Playback.AudioDecayFactor);

        if (playbackSettings.Playback.EnableAudioBeatLocking)
        {
            if (playbackSettings.Playback.BeatLockSource != CompositionSettings.BeatLockSources.OnsetDetection)
            {
                DanceAiPhaseTracker.FeedCapture(buffer, length, AudioInputBackend.Current.ActiveChannelCount, SampleRate);
            }
            else
            {
                BeatSynchronizer.UpdateBeatTimer();
            }
        }
    }

    /// <summary>
    /// Flag indicating whether the last FFT data fetch from WASAPI failed.
    /// Used to trigger capture restart on the next frame.
    /// </summary>
    private static bool _failedToGetLastFffData;

    /// <summary>
    /// Internal cache of enumerated WASAPI input devices.
    /// </summary>
    private static List<AudioInputDevice> _inputDevices;
    
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
    static AudioInput()
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
            Log.Debug($"AudioInput: Failed to initialize sample rate from AudioMixerManager: {ex.Message}");
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
    /// Active WAV writer. Set by <see cref="BeginRecording"/>, cleared by
    /// <see cref="EndRecording"/> and <see cref="Stop"/>. Read on the BASS capture thread
    /// inside the callback.
    /// </summary>
    private static WavFileWriter _activeRecording;

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

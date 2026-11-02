using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using T3.Core.Audio.Native;
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
/// The native engine captures into a ring buffer, and everything else - analysis, beat tracking, recording -
/// runs here on the main thread, so no managed code runs on the capture thread and none of it races the frame.
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

        UpdateCaptureState(settings);
        ProcessCapturedAudio();
    }

    private static void UpdateCaptureState(CompositionSettings settings)
    {
        var wantsCaptureForFft = settings.Playback.AudioSource == CompositionSettings.AudioSources.ExternalDevice;
        var wantsCaptureForRecording = _isCaptureNeededForRecording;

        // Without a usable backend there is no capture to start and nothing to stop.
        if ((wantsCaptureForFft || wantsCaptureForRecording) && AudioMixerManager.NativeEngine == null)
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

            Log.Debug("Trying to restart the audio input...");
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

                Log.Warning("Can't switch to an audio input device without a name");
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
    /// Gets the list of available audio input devices.
    /// </summary>
    /// <value>
    /// A list of <see cref="AudioInputDevice"/> instances representing available audio input devices.
    /// The list is lazily initialized on first access.
    /// </value>
    public static List<AudioInputDevice> InputDevices
    {
        get
        {
            if (AudioMixerManager.NativeEngine == null)
                return _inputDevices ??= [];

            if (_inputDevices == null)
                InitializeInputDeviceList();

            return _inputDevices;
        }
    }

    /// <summary>Starts capturing from the specified device, replacing what ran before.</summary>
    private static void StartInputCapture(AudioInputDevice device)
    {
        var engine = AudioMixerManager.NativeEngine;
        if (engine == null)
            return;

        Log.Info($"Initializing audio input for {device.Name}... ");

        var result = engine.StartCapture(device.NativeIndex, device.IsLoopback && OperatingSystem.IsWindows());
        if (result != 0)
        {
            Log.Error($"Can't open audio input '{device.Name}' (error {result}).");
            return;
        }

        _nextCaptureFrame = 0;
        ActiveInputDeviceName = device.Name;
    }

    /// <summary>
    /// Stops the audio capture and releases associated resources.
    /// Any active recording session is finalised first so the WAV file isn't left
    /// with a placeholder header.
    /// </summary>
    private static void Stop()
    {
        if (_activeRecording != null)
        {
            EndRecording();
        }

        AudioMixerManager.NativeEngine?.StopCapture();
        ActiveInputDeviceName = null;
        DanceAiPhaseTracker.Reset();
    }

    /// <summary>
    /// Starts writing the live capture to an uncompressed 16-bit PCM WAV file in
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
        // the composition's AudioSource is something other than ExternalDevice. If capture
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
            Log.Warning("Cannot start WAV recording: no audio input device available.");
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

        var sampleRate = SampleRate;
        const int channels = CaptureChannelCount;

        try
        {
            // Assign last so nothing reads a partly constructed writer.
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

        _activeRecording = null;
        writer.Dispose();

        // Recording is done; let StartFrame tear capture down again on the next tick
        // (it will, if AudioSource isn't ExternalDevice). Holding capture alive until the
        // next frame avoids a stutter if BeginRecording is called again immediately.
        _isCaptureNeededForRecording = false;

        if (writer.BytesWritten == 0)
        {
            // A finalised-but-silent WAV means capture ran yet the device never delivered frames —
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
    /// Brings capture up on demand when a recording is requested but no capture
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
            Log.Warning("Cannot start capture for recording: no input devices available.");
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
    /// True while a recording session needs capture running, regardless of the
    /// composition's <see cref="CompositionSettings.PlaybackConfig.AudioSource"/>. Set by
    /// <see cref="BeginRecording"/>, cleared by <see cref="EndRecording"/>. Lets the
    /// FFT-input and recording paths use the input independently — the OS allows
    /// simultaneous render-and-capture, so there's no hardware reason to couple them.
    /// </summary>
    private static bool _isCaptureNeededForRecording;
    
    /// <summary>Enumerates capture devices, plus the outputs that can be recorded where WASAPI offers loopback.</summary>
    private static void InitializeInputDeviceList()
    {
        _inputDevices = [];
        var engine = AudioMixerManager.NativeEngine;
        if (engine == null)
            return;

        for (var index = 0; index < engine.GetDeviceCount(capture: true); index++)
        {
            var name = engine.GetDeviceName(capture: true, index);
            if (name == null)
                continue;

            // PipeWire and PulseAudio list what an output plays as an ordinary capture device.
            var isMonitor = name.StartsWith("Monitor of", StringComparison.OrdinalIgnoreCase);
            _inputDevices.Add(new AudioInputDevice { Name = name, IsLoopback = isMonitor, NativeIndex = index });
            Log.Debug($"Found audio input ID:{_inputDevices.Count - 1} {name}");
        }

        if (!OperatingSystem.IsWindows())
            return;

        for (var index = 0; index < engine.GetDeviceCount(capture: false); index++)
        {
            var name = engine.GetDeviceName(capture: false, index);
            if (name != null)
                _inputDevices.Add(new AudioInputDevice { Name = name + " (Loopback)", IsLoopback = true, NativeIndex = index });
        }
    }

    /// <summary>
    /// Works through what was captured since the last frame in steps of about 10 ms, because beat tracking
    /// and the analysis decay are tuned to that rate. After a long stall only the newest steps are analysed;
    /// a recording still receives every frame.
    /// </summary>
    private static void ProcessCapturedAudio()
    {
        var engine = AudioMixerManager.NativeEngine;
        if (engine == null || string.IsNullOrEmpty(ActiveInputDeviceName))
            return;

        if (!engine.IsCaptureRunning)
        {
            _failedToGetLastFffData = true;
            return;
        }

        var written = engine.CaptureFramesWritten;

        // An export stops this loop and handles analysis itself, so what arrived meanwhile is out of date.
        if (Playback.Current.IsRenderingToFile)
        {
            _nextCaptureFrame = written;
            return;
        }

        var capacity = (ulong)NativeAudioEngine.CaptureRingCapacity;
        if (written - _nextCaptureFrame > capacity)
        {
            if (_activeRecording != null)
                Log.Warning("The frame loop stalled for too long; the audio recording lost samples.");

            _nextCaptureFrame = written - capacity;
        }

        var stepsLeft = (long)((written - _nextCaptureFrame) / CaptureStepFrames);
        var now = Playback.RunTimeInSecs;
        var stepSecs = CaptureStepFrames / (double)SampleRate;

        for (; stepsLeft > 0; stepsLeft--)
        {
            if (engine.ReadCapture(_nextCaptureFrame, _captureStep) < CaptureStepFrames)
                return;

            _nextCaptureFrame += CaptureStepFrames;
            ConsumeStep(stepsLeft <= MaxAnalysedStepsPerFrame, now - (stepsLeft - 1) * stepSecs, stepSecs);
        }
    }

    private static unsafe void ConsumeStep(bool analyse, double stepTime, double stepSecs)
    {
        fixed (float* samples = _captureStep)
        {
            var buffer = (IntPtr)samples;
            _activeRecording?.AppendFloat32Samples(buffer, _captureStep.Length * sizeof(float));

            var settings = Playback.Current?.Settings;
            if (settings is { Playback.EnableAudioBeatLocking: true }
                && settings.Playback.BeatLockSource != CompositionSettings.BeatLockSources.OnsetDetection)
            {
                DanceAiPhaseTracker.FeedCapture(buffer, _captureStep.Length * sizeof(float), CaptureChannelCount, SampleRate);
            }
        }

        // Recording alone doesn't take over the analysis, which then follows the project's own audio.
        if (Playback.Current?.Settings is not { Playback.AudioSource: CompositionSettings.AudioSources.ExternalDevice } playbackSettings)
            return;

        Array.Copy(_monoHistory, CaptureStepFrames, _monoHistory, 0, _monoHistory.Length - CaptureStepFrames);
        var interleaved = WaveFormProcessing.InterleavenSampleBuffer;
        Array.Copy(interleaved, _captureStep.Length, interleaved, 0, interleaved.Length - _captureStep.Length);
        Array.Copy(_captureStep, 0, interleaved, interleaved.Length - _captureStep.Length, _captureStep.Length);
        WaveFormProcessing.LastFetchResultCode = interleaved.Length;

        var peak = 0f;
        var writeIndex = _monoHistory.Length - CaptureStepFrames;
        for (var frame = 0; frame < CaptureStepFrames; frame++)
        {
            var left = _captureStep[frame * 2];
            var right = _captureStep[frame * 2 + 1];
            _monoHistory[writeIndex + frame] = (left + right) * 0.5f;
            peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
        }

        if (!analyse)
            return;

        TimeSinceLastUpdate = stepSecs;
        LastUpdateTime = stepTime;
        _lastAudioLevel = peak * LevelMeterScale;

        var mean = 0f;
        for (var index = 0; index < _monoHistory.Length; index++)
            mean += _monoHistory[index];

        mean /= _monoHistory.Length;
        for (var index = 0; index < _monoHistory.Length; index++)
            _fftSamples[index] = _monoHistory[index] - mean;

        _fft.ComputeMagnitudes(_fftSamples, AudioAnalysis.FftGainBuffer);
        AudioAnalysis.ProcessUpdate(playbackSettings.Playback.AudioGainFactor, playbackSettings.Playback.AudioDecayFactor);

        if (playbackSettings.Playback.EnableAudioBeatLocking
            && playbackSettings.Playback.BeatLockSource == CompositionSettings.BeatLockSources.OnsetDetection)
        {
            BeatSynchronizer.UpdateBeatTimer();
        }
    }

    /// <summary>
    /// Flag indicating that the capture device stopped by itself.
    /// Used to trigger capture restart on the next frame.
    /// </summary>
    private static bool _failedToGetLastFffData;

    /// <summary>
    /// Internal cache of enumerated input devices.
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
    /// The sample rate in Hz captured audio arrives at, whatever the device runs at.
    /// </summary>
    internal const int SampleRate = AudioConfig.MixerFrequency;

    /// <summary>
    /// Gets the name of the currently active input device.
    /// </summary>
    /// <value>
    /// The device name string if capture is active; otherwise, <c>null</c>.
    /// </value>
    public static string ActiveInputDeviceName { get; private set; }
    
    /// <summary>
    /// The raw audio level value from the last measurement.
    /// </summary>
    private static float _lastAudioLevel;

    /// <summary>
    /// Active WAV writer. Set by <see cref="BeginRecording"/>, cleared by
    /// <see cref="EndRecording"/> and <see cref="Stop"/>.
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

    /** Everything is captured as interleaved stereo, which the analysis and the WAV writer expect. */
    private const int CaptureChannelCount = 2;

    /** 10 ms at the mix rate. */
    private const int CaptureStepFrames = 480;

    private const int MaxAnalysedStepsPerFrame = 16;

    /** Scales a 0..1 peak into the range the gain meter in the playback settings was calibrated to. */
    private const float LevelMeterScale = 21474f;

    private static readonly float[] _captureStep = new float[CaptureStepFrames * CaptureChannelCount];
    private static readonly float[] _monoHistory = new float[AudioConfig.FftBufferSize * 2];
    private static readonly float[] _fftSamples = new float[AudioConfig.FftBufferSize * 2];
    private static readonly Fft _fft = new(AudioConfig.FftBufferSize * 2);
    private static ulong _nextCaptureFrame;
}

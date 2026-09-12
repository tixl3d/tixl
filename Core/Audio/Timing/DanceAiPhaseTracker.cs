#nullable enable
using System;
using System.IO;
using System.Threading;
using DanceAi;
using Microsoft.ML.OnnxRuntime;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.IO;
using T3.Core.Logging;

namespace T3.Core.Audio.Timing;

/// <summary>
/// The DanceAi integration: runs the DanceAi bar-phase model on the live WASAPI capture and exposes both
/// the raw model phase and a <see cref="BarPhaseLock"/> clock locked onto it, for beat locking.
/// </summary>
/// <remarks>
/// DanceAi is a small recurrent network (about 16 MB) self-trained by Felix Niemeyer
/// (https://github.com/felixniemeyer/dance) for exactly one task: estimating the bar phase and tempo of an
/// audio stream. It runs locally through ONNX Runtime on a single thread, costing roughly 4 ms of CPU per
/// 16.7 ms of audio, i.e. about 40 % of one core while active, and nothing while the source is not selected.
///
/// The capture callback only downmixes and resamples into a queue; inference (~4 ms per 16 ms of audio)
/// runs on a dedicated background thread so it never stalls the audio driver. The worker publishes a clock
/// anchor (bar time at a timestamp plus tempo) that the main thread extrapolates to the current runtime.
/// The model files live next to the executable in <c>dance-phase/</c> and are loaded on first use.
/// </remarks>
public static class DanceAiPhaseTracker
{
    /// <summary>True once the model is loaded and the clock has locked onto a first bar start.</summary>
    public static bool IsLocked => _estimator != null && _isLocked;

    /// <summary>True once the model is loaded and estimates are flowing, regardless of the lock.</summary>
    public static bool HasEstimates => _estimator != null && _hasEstimate;

    /// <summary>
    /// 0 follows the model tightly (fast, may wobble), 1 trusts the running tempo (smooth, slow to correct).
    /// Written by the UI thread, read by the worker.
    /// </summary>
    public static float Smoothing
    {
        get => _smoothing;
        set => _smoothing = Math.Clamp(value, 0, 1);
    }

    /// <summary>Short human-readable state for the settings UI.</summary>
    public static string StatusMessage
    {
        get
        {
            if (_loadError != null)
                return _loadError;

            if (_estimator == null)
                return "Loading bar-phase model...";

            return _isLocked ? "Tracking" : "Waiting for a bar start...";
        }
    }

    /// <summary>Locked clock: continuous bar count since the first bar start, extrapolated to the current runtime.</summary>
    public static double BarProgress
    {
        get
        {
            lock (_stateLock)
            {
                return _lockedAnchorBars + SecondsSinceAnchor() * BpmMath.BarsPerSecond(_lockedBpm);
            }
        }
    }

    /// <summary>Locked clock tempo, assuming four beats per bar.</summary>
    public static double CurrentBpm
    {
        get
        {
            lock (_stateLock)
            {
                return _lockedBpm;
            }
        }
    }

    /// <summary>Unprocessed model phase, unwrapped to a continuous bar count and extrapolated with the model tempo.</summary>
    public static double RawBarProgress
    {
        get
        {
            lock (_stateLock)
            {
                return _rawAnchorBars + SecondsSinceAnchor() * BpmMath.BarsPerSecond(_rawBpm);
            }
        }
    }

    /// <summary>Tempo as predicted by the model itself, assuming four beats per bar.</summary>
    public static double RawBpm
    {
        get
        {
            lock (_stateLock)
            {
                return _rawBpm;
            }
        }
    }

    /// <summary>Expected |phase error| in bars as reported by the model, in [0, 0.5]. Lower is more confident.</summary>
    public static float ExpectedPhaseError => _expectedPhaseError;

    /// <summary>
    /// Forget all carried state, e.g. after a device switch. The estimator itself is reset by the worker
    /// before its next inference, because it is not safe to touch while <c>Feed</c> is running.
    /// </summary>
    public static void Reset()
    {
        lock (_queueLock)
        {
            _queuedSampleCount = 0;
            _resampler.Reset();
            _streamSampleCount = 0;
            _streamStartTimeSec = double.NaN;
        }

        lock (_stateLock)
        {
            _phaseLock.Reset();
            _isLocked = false;
            _hasEstimate = false;
            _rawBars = 0;
            _previousRawPhase = double.NaN;
        }

        _resetRequested = true;
    }

    /// <summary>
    /// Called from the WASAPI capture callback with interleaved float samples.
    /// Cheap: downmix + resample into the queue and wake the worker.
    /// </summary>
    internal static unsafe void FeedCapture(IntPtr buffer, int lengthInBytes, int channelCount, int sampleRate)
    {
        if (_loadError != null || buffer == IntPtr.Zero || lengthInBytes <= 0 || channelCount <= 0 || sampleRate <= 0)
            return;

        EnsureWorker();

        var interleaved = new ReadOnlySpan<float>((void*)buffer, lengthInBytes / sizeof(float));
        lock (_queueLock)
        {
            var appended = _resampler.Append(interleaved, channelCount, sampleRate, ref _queue, _queuedSampleCount);
            if (appended == 0)
                return;

            _queuedSampleCount += appended;
            _streamSampleCount += appended;
            UpdateStreamStartTime();
        }

        _samplesAvailable.Set();
    }

    private static double SecondsSinceAnchor() => Playback.RunTimeInSecs - _anchorTimeSec;

    /// <summary>
    /// Callbacks arrive late but never early, so the earliest observed start wins and later
    /// observations only nudge it slowly. That gives every frame a jitter-free timestamp.
    /// </summary>
    private static void UpdateStreamStartTime()
    {
        var observedStart = Playback.RunTimeInSecs - _streamSampleCount / (double)ModelSampleRate;
        if (double.IsNaN(_streamStartTimeSec) || observedStart < _streamStartTimeSec)
        {
            _streamStartTimeSec = observedStart;
        }
        else
        {
            _streamStartTimeSec += (observedStart - _streamStartTimeSec) * StreamStartDriftRate;
        }
    }

    private static void EnsureWorker()
    {
        if (_worker != null)
            return;

        lock (_stateLock)
        {
            if (_worker != null)
                return;

            _worker = new Thread(WorkerLoop)
                          {
                              Name = "DanceAiPhaseTracker",
                              IsBackground = true,
                              Priority = ThreadPriority.AboveNormal,
                          };
            _worker.Start();
        }
    }

    private static void WorkerLoop()
    {
        if (!TryLoadModel())
            return;

        var estimator = _estimator!;
        var frameDurationSec = estimator.Meta.FrameSize / (double)ModelSampleRate;
        var processedFrameCount = 0L;

        while (true)
        {
            _samplesAvailable.WaitOne();

            if (_resetRequested)
            {
                _resetRequested = false;
                estimator.Reset();
                processedFrameCount = 0;
            }

            double streamStartTimeSec;
            int count;
            lock (_queueLock)
            {
                count = _queuedSampleCount;
                if (_workBuffer.Length < count)
                    _workBuffer = new float[count * 2];

                Array.Copy(_queue, _workBuffer, count);
                _queuedSampleCount = 0;
                streamStartTimeSec = _streamStartTimeSec;
            }

            if (count == 0)
                continue;

            try
            {
                foreach (var estimate in estimator.Feed(new ReadOnlySpan<float>(_workBuffer, 0, count)))
                {
                    processedFrameCount++;
                    var frameEndTimeSec = streamStartTimeSec + processedFrameCount * frameDurationSec;
                    PublishEstimate(estimate, frameEndTimeSec, frameDurationSec);
                }
            }
            catch (Exception e)
            {
                // A transient inference failure must not kill the feature for the session: log the first
                // one, start over with fresh model state and keep going.
                if (!_inferenceFailedOnce)
                {
                    _inferenceFailedOnce = true;
                    Log.Warning("Bar-phase inference failed, restarting: " + e.Message);
                }

                Reset();
            }
        }
    }

    private static void PublishEstimate(FrameEstimate estimate, double frameEndTimeSec, double frameDurationSec)
    {
        _expectedPhaseError = (float)estimate.ExpectedPhaseError;

        lock (_stateLock)
        {
            UnwrapRawPhase(estimate.Phase);
            _rawAnchorBars = _rawBars;
            _rawBpm = Math.Clamp(BpmMath.BpmFromBarDuration(Math.Max(estimate.BarDurationS, MinBarDurationSec)),
                                 BarPhaseLock.MinBpm, BarPhaseLock.MaxBpm);
            _hasEstimate = true;

            _phaseLock.Update(estimate.Phase, estimate.BarDurationS, frameEndTimeSec, frameDurationSec, _smoothing);
            _lockedBpm = _phaseLock.Bpm;
            _lockedAnchorBars = _phaseLock.BarTime;
            _anchorTimeSec = frameEndTimeSec;
            _isLocked = _phaseLock.IsLocked;
        }

        if (CoreSettings.Config.EnableBeatSyncProfiling)
            RecordTraces();
    }

    /// <summary>Per-frame curves for comparing the raw model output with the locked clock in the data-set viewer.</summary>
    private static void RecordTraces()
    {
        DebugDataRecording.KeepTraceData("BarPhase/rawPhase", _rawBars % 1, ref _rawPhaseChannel);
        DebugDataRecording.KeepTraceData("BarPhase/rawBpm", _rawBpm, ref _rawBpmChannel);
        DebugDataRecording.KeepTraceData("BarPhase/lockedPhase", _lockedAnchorBars % 1, ref _lockedPhaseChannel);
        DebugDataRecording.KeepTraceData("BarPhase/lockedBpm", _lockedBpm, ref _lockedBpmChannel);
        DebugDataRecording.KeepTraceData("BarPhase/pendingError", _phaseLock.PendingPhaseError, ref _pendingErrorChannel);
    }

    /// <summary>Accumulate the wrapped model phase into a continuous bar count for raw mode.</summary>
    private static void UnwrapRawPhase(double phase)
    {
        if (!double.IsNaN(_previousRawPhase))
        {
            var delta = phase - _previousRawPhase;
            if (delta < -0.5)
                delta += 1;
            if (delta > 0.5)
                delta -= 1;
            _rawBars += delta;
        }

        _previousRawPhase = phase;
    }

    private static bool TryLoadModel()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, ModelFolder);
        var modelPath = Path.Combine(folder, ModelFileName);
        var metaPath = Path.Combine(folder, MetaFileName);

        if (!File.Exists(modelPath) || !File.Exists(metaPath))
        {
            _loadError = $"Bar-phase model not found in {folder}";
            Log.Warning(_loadError);
            return false;
        }

        try
        {
            // Default ORT threading spins helper threads that burn >2 cores for no wall-clock gain on this small GRU.
            var options = new EstimatorOptions
                              {
                                  SessionOptions = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 },
                              };
            var estimator = DancePhaseEstimator.FromFiles(modelPath, metaPath, options);
            if (estimator.Meta.Samplerate != ModelSampleRate)
            {
                _loadError = $"Bar-phase model expects {estimator.Meta.Samplerate} Hz, only {ModelSampleRate} Hz is supported";
                Log.Warning(_loadError);
                estimator.Dispose();
                return false;
            }

            lock (_stateLock)
            {
                _estimator = estimator;
            }

            Log.Debug($"Loaded bar-phase model {estimator.Meta.ModelType} from {modelPath}");
            return true;
        }
        catch (Exception e)
        {
            _loadError = "Failed to load bar-phase model: " + e.Message;
            Log.Warning(_loadError);
            return false;
        }
    }

    private const string ModelFolder = "dance-phase";
    private const string ModelFileName = "bar-phase.onnx";
    private const string MetaFileName = "bar-phase.meta.json";
    private const int ModelSampleRate = 24000;
    private const double MinBarDurationSec = 0.1;
    private const double StreamStartDriftRate = 0.002;

    private static readonly Lock _stateLock = new();
    private static readonly Lock _queueLock = new();
    private static readonly AutoResetEvent _samplesAvailable = new(false);
    private static readonly BarPhaseLock _phaseLock = new();
    private static readonly MonoResampler _resampler = new(ModelSampleRate);

    private static Thread? _worker;
    private static DataChannel? _rawPhaseChannel;
    private static DataChannel? _rawBpmChannel;
    private static DataChannel? _lockedPhaseChannel;
    private static DataChannel? _lockedBpmChannel;
    private static DataChannel? _pendingErrorChannel;
    private static DancePhaseEstimator? _estimator;
    private static volatile string? _loadError;
    private static volatile bool _inferenceFailedOnce;
    private static volatile bool _resetRequested;
    private static volatile bool _isLocked;
    private static volatile bool _hasEstimate;
    private static volatile float _expectedPhaseError;
    private static volatile float _smoothing = 0.5f;

    // Published clocks: bar count at the anchor time, extrapolated with the tempo by the reader.
    private static double _anchorTimeSec;
    private static double _lockedAnchorBars;
    private static double _lockedBpm = 120;
    private static double _rawAnchorBars;
    private static double _rawBpm = 120;
    private static double _rawBars;
    private static double _previousRawPhase = double.NaN;

    // Capture queue, filled on the audio thread and drained by the worker.
    private static float[] _queue = new float[8192];
    private static float[] _workBuffer = new float[8192];
    private static int _queuedSampleCount;
    private static long _streamSampleCount;
    private static double _streamStartTimeSec = double.NaN;
}

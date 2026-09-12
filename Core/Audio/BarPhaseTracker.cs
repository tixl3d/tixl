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

namespace T3.Core.Audio;

/// <summary>
/// Estimates the musical bar phase of the live WASAPI capture with the DanceAi neural model
/// and locks a smooth bar clock onto it, mirroring <see cref="BeatSynchronizer"/>.
/// </summary>
/// <remarks>
/// The model's phase is only sharp at the bar start and progresses unevenly in between, so the clock is
/// not driven by the raw phase. Instead every bar rollover of the model phase is an event: the intervals
/// between events give the tempo, the clock's distance to its bar start at the event gives the phase error.
/// Corrections are applied mostly through BPM (like the onset tracker) so playback never jumps.
///
/// The capture callback only downmixes and resamples into a queue; inference (~4 ms per 16 ms of
/// audio) runs on a dedicated background thread so it never stalls the audio driver.
/// The model files live next to the executable in <c>dance-phase/</c> and are loaded on first use.
/// </remarks>
public static class BarPhaseTracker
{
    /// <summary>True once the model is loaded and the clock has locked onto a first beat.</summary>
    public static bool IsAvailable => _estimator != null && _hasLock;

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

            return _hasLock ? "Tracking" : "Waiting for a bar start...";
        }
    }

    /// <summary>Continuous bar count since tracking started, extrapolated to the current runtime.</summary>
    public static double BarProgress
    {
        get
        {
            lock (_stateLock)
            {
                var elapsedS = Playback.RunTimeInSecs - _anchorTimeS;
                return _anchorBarTime + elapsedS * _bpm / 240.0;
            }
        }
    }

    /// <summary>Locked tempo, assuming four beats per bar.</summary>
    public static double CurrentBpm
    {
        get
        {
            lock (_stateLock)
            {
                return _bpm;
            }
        }
    }

    /// <summary>True once the model is loaded and estimates are flowing, regardless of the lock.</summary>
    public static bool HasEstimates => _estimator != null && _hasEstimate;

    /// <summary>Unprocessed model phase, unwrapped to a continuous bar count and extrapolated with the model tempo.</summary>
    public static double RawBarProgress
    {
        get
        {
            lock (_stateLock)
            {
                var elapsedS = Playback.RunTimeInSecs - _anchorTimeS;
                return _rawAnchorBars + elapsedS * _rawBpm / 240.0;
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

    /// <summary>Forget all carried state, e.g. after a device switch.</summary>
    public static void Reset()
    {
        lock (_queueLock)
        {
            _queuedSampleCount = 0;
            _resamplePos = 0;
            _lastMonoSample = 0;
            _streamSampleCount = 0;
            _streamStartTimeS = double.NaN;
        }

        _processedFrameCount = 0;

        lock (_stateLock)
        {
            _estimator?.Reset();
            _lock.Reset();
            _hasLock = false;
            _hasEstimate = false;
            _rawBars = 0;
            _previousRawPhase = double.NaN;
        }
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
        var frameCount = interleaved.Length / channelCount;
        if (frameCount == 0)
            return;

        lock (_queueLock)
        {
            // Mono buffer keeps the previous chunk's last sample at index 0 so interpolation is seamless.
            var monoCount = frameCount + 1;
            if (_monoBuffer.Length < monoCount)
                _monoBuffer = new float[monoCount * 2];

            _monoBuffer[0] = _lastMonoSample;
            var channelScale = 1f / channelCount;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var sum = 0f;
                var baseIndex = frameIndex * channelCount;
                for (var channel = 0; channel < channelCount; channel++)
                {
                    sum += interleaved[baseIndex + channel];
                }

                _monoBuffer[frameIndex + 1] = sum * channelScale;
            }

            _lastMonoSample = _monoBuffer[monoCount - 1];

            var ratio = sampleRate / (double)ModelSampleRate;
            var maxOutput = (int)(frameCount / ratio) + 2;
            if (_queue.Length < _queuedSampleCount + maxOutput)
                Array.Resize(ref _queue, Math.Max(_queue.Length * 2, _queuedSampleCount + maxOutput));

            var queuedBefore = _queuedSampleCount;
            var pos = _resamplePos;
            while (pos + 1 < monoCount)
            {
                var index = (int)pos;
                var frac = (float)(pos - index);
                var a = _monoBuffer[index];
                var b = _monoBuffer[index + 1];
                _queue[_queuedSampleCount++] = a + (b - a) * frac;
                pos += ratio;
            }

            _resamplePos = pos - (monoCount - 1);
            _streamSampleCount += _queuedSampleCount - queuedBefore;
            UpdateStreamStartTime();
        }

        _samplesAvailable.Set();
    }

    /// <summary>
    /// Callbacks arrive late but never early, so the earliest observed start wins and later
    /// observations only nudge it slowly. That gives every frame a jitter-free timestamp.
    /// </summary>
    private static void UpdateStreamStartTime()
    {
        var observedStart = Playback.RunTimeInSecs - _streamSampleCount / (double)ModelSampleRate;
        if (double.IsNaN(_streamStartTimeS) || observedStart < _streamStartTimeS)
        {
            _streamStartTimeS = observedStart;
        }
        else
        {
            _streamStartTimeS += (observedStart - _streamStartTimeS) * 0.002;
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
                              Name = "BarPhaseTracker",
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

        while (true)
        {
            _samplesAvailable.WaitOne();

            double streamStartTimeS;
            int count;
            lock (_queueLock)
            {
                count = _queuedSampleCount;
                if (_workBuffer.Length < count)
                    _workBuffer = new float[count * 2];

                Array.Copy(_queue, _workBuffer, count);
                _queuedSampleCount = 0;
                streamStartTimeS = _streamStartTimeS;
            }

            if (count == 0)
                continue;

            try
            {
                var frameSize = _estimator!.Meta.FrameSize;
                var frameDurationS = frameSize / (double)ModelSampleRate;
                foreach (var estimate in _estimator.Feed(new ReadOnlySpan<float>(_workBuffer, 0, count)))
                {
                    _processedFrameCount++;
                    var frameEndTimeS = streamStartTimeS + _processedFrameCount * frameDurationS;
                    _expectedPhaseError = (float)estimate.ExpectedPhaseError;

                    lock (_stateLock)
                    {
                        UnwrapRawPhase(estimate.Phase);
                        _rawAnchorBars = _rawBars;
                        _rawBpm = 240.0 / Math.Max(estimate.BarDurationS, 0.1);
                        _hasEstimate = true;

                        _lock.Update(estimate.Phase, estimate.BarDurationS, frameEndTimeS, frameDurationS, _smoothing);
                        _bpm = _lock.Bpm;
                        _anchorBarTime = _lock.BarTime;
                        _anchorTimeS = frameEndTimeS;
                        _hasLock = _lock.HasLock;
                    }

                    if (CoreSettings.Config.EnableBeatSyncProfiling)
                        RecordTraces();
                }
            }
            catch (Exception e)
            {
                _loadError = "Bar-phase inference failed: " + e.Message;
                Log.Warning(_loadError);
                return;
            }
        }
    }

    /// <summary>Per-frame curves for comparing the raw model output with the locked clock in the data-set viewer.</summary>
    private static void RecordTraces()
    {
        DebugDataRecording.KeepTraceData("BarPhase/rawPhase", _rawBars % 1, ref _rawPhaseChannel);
        DebugDataRecording.KeepTraceData("BarPhase/rawBpm", _rawBpm, ref _rawBpmChannel);
        DebugDataRecording.KeepTraceData("BarPhase/lockedPhase", _anchorBarTime % 1, ref _lockedPhaseChannel);
        DebugDataRecording.KeepTraceData("BarPhase/lockedBpm", _bpm, ref _lockedBpmChannel);
        DebugDataRecording.KeepTraceData("BarPhase/pendingError", _lock.PendingPhaseError, ref _pendingErrorChannel);
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

    /// <summary>
    /// A bar clock locked onto the bar rollovers of the model phase. Runs on the worker thread only.
    /// Only the wrap from 1 back to 0 is used as an event: the model's phase progresses unevenly within a
    /// bar, so quarter crossings are not reliable beat times, but the bar start is.
    /// </summary>
    private sealed class PhaseLock
    {
        public double BarTime => _barTime;
        public double Bpm => _bpm;
        public bool HasLock => _hasLock;
        public double PendingPhaseError => _pendingPhaseError;

        public void Reset()
        {
            _hasLock = false;
            _barTime = 0;
            _bpm = 120;
            _modelBpm = 120;
            _tempoTrim = 0;
            _pendingPhaseError = 0;
            _previousPhase = double.NaN;
            _lastRolloverTimeS = double.NaN;
            _intervalHead = 0;
            _intervalCount = 0;
        }

        /// <summary>Advance the clock by one model frame and correct it if the frame contains a bar rollover.</summary>
        public void Update(double modelBarPhase, double modelBarDurationS, double frameEndTimeS, double frameDurationS, float smoothing)
        {
            var isRollover = !double.IsNaN(_previousPhase) && modelBarPhase < _previousPhase - 0.5;
            _previousPhase = modelBarPhase;

            if (!_hasLock)
            {
                if (!isRollover)
                    return;

                // Snap onto the first bar start.
                _modelBpm = 240.0 / Math.Max(modelBarDurationS, 0.1);
                _bpm = Math.Clamp(_modelBpm, MinBpm, MaxBpm);
                _barTime = 0;
                _lastRolloverTimeS = frameEndTimeS;
                _hasLock = true;
                return;
            }

            _barTime += frameDurationS * _bpm / 240.0;
            BleedPhaseError(frameDurationS, smoothing);
            _bpm = Math.Clamp(_modelBpm * (1 + _tempoTrim), MinBpm, MaxBpm);

            if (!isRollover)
                return;

            var barDurationS = 240.0 / _bpm;
            var intervalS = frameEndTimeS - _lastRolloverTimeS;

            // The raw phase jitters across the wrap point, so a rollover shortly after the last one is noise.
            if (intervalS < barDurationS * RefractoryBarFraction)
                return;

            _lastRolloverTimeS = frameEndTimeS;
            UpdateTempo(intervalS, barDurationS, smoothing);

            // How far the clock is from its nearest bar start, in bars; positive means the clock is ahead.
            var barError = _barTime - Math.Round(_barTime);

            // Near half a bar off, the wrapped error flips sign every bar and the corrections cancel.
            // Always resolve that ambiguity by slowing down so the clock settles instead of dithering.
            if (barError < -HalfBarDeadZone)
                barError += 1;

            _pendingPhaseError = barError;
            _tempoTrim = Math.Clamp(_tempoTrim - barError * TempoTrimGain, -MaxTempoTrim, MaxTempoTrim);

            if (CoreSettings.Config.EnableBeatSyncProfiling)
            {
                DebugDataRecording.KeepTraceData("BarPhase/barError", barError);
                DebugDataRecording.KeepTraceData("BarPhase/bpm", _bpm);
                DebugDataRecording.KeepTraceData("BarPhase/tempoTrim", _tempoTrim);
            }
        }

        /// <summary>
        /// Pay the phase error off over time by slewing the clock, never by stepping it. The time constant
        /// spans ~0.1 s (tight) to ~1.5 s (smooth) so behaviour doesn't depend on tempo.
        /// </summary>
        private void BleedPhaseError(double frameDurationS, float smoothing)
        {
            if (_pendingPhaseError == 0)
                return;

            var timeConstantS = 0.1 + smoothing * 1.4;
            var gain = Math.Min(1, frameDurationS / timeConstantS);
            var correction = _pendingPhaseError * gain;

            // Never let a correction change the apparent tempo by more than a quarter.
            var maxCorrection = frameDurationS * _bpm / 240.0 * MaxSlewFraction;
            correction = Math.Clamp(correction, -maxCorrection, maxCorrection);
            _barTime -= correction;
            _pendingPhaseError -= correction;
        }

        /// <summary>
        /// Tempo from the intervals between accepted bar rollovers, averaged over the last 1 (tight) to 4
        /// (smooth) bars. Intervals far off the current bar, e.g. across a stall of the model, are ignored so
        /// the clock holds its tempo there. The trim integrates the bar error to cancel any remaining bias.
        /// </summary>
        private void UpdateTempo(double intervalS, double barDurationS, float smoothing)
        {
            var ratio = intervalS / barDurationS;
            if (ratio < 0.5 || ratio > 2.0)
                return;

            _intervals[_intervalHead] = intervalS;
            _intervalHead = (_intervalHead + 1) % IntervalCapacity;
            if (_intervalCount < IntervalCapacity)
                _intervalCount++;

            var window = Math.Min(_intervalCount, 1 + (int)(smoothing * 3));
            var sum = 0.0;
            for (var step = 1; step <= window; step++)
            {
                sum += _intervals[(_intervalHead + IntervalCapacity - step) % IntervalCapacity];
            }

            _modelBpm = 240.0 * window / sum;
        }

        private const int IntervalCapacity = 4;
        private const double RefractoryBarFraction = 0.6;
        private const double HalfBarDeadZone = 0.35;
        private const double MaxSlewFraction = 0.25;
        private const double TempoTrimGain = 0.3;
        private const double MaxTempoTrim = 0.03;
        private const double MinBpm = 40.0;
        private const double MaxBpm = 400.0;

        private readonly double[] _intervals = new double[IntervalCapacity];
        private int _intervalHead;
        private int _intervalCount;

        private bool _hasLock;
        private double _barTime;
        private double _bpm = 120;
        private double _modelBpm = 120;
        private double _tempoTrim;
        private double _pendingPhaseError;
        private double _previousPhase = double.NaN;
        private double _lastRolloverTimeS = double.NaN;
    }

    private const string ModelFolder = "dance-phase";
    private const string ModelFileName = "bar-phase.onnx";
    private const string MetaFileName = "bar-phase.meta.json";
    private const int ModelSampleRate = 24000;

    private static readonly object _stateLock = new();
    private static readonly object _queueLock = new();
    private static readonly AutoResetEvent _samplesAvailable = new(false);
    private static readonly PhaseLock _lock = new();

    private static Thread? _worker;
    private static DataChannel? _rawPhaseChannel;
    private static DataChannel? _rawBpmChannel;
    private static DataChannel? _lockedPhaseChannel;
    private static DataChannel? _lockedBpmChannel;
    private static DataChannel? _pendingErrorChannel;
    private static DancePhaseEstimator? _estimator;
    private static volatile string? _loadError;
    private static volatile bool _hasLock;
    private static volatile bool _hasEstimate;
    private static volatile float _expectedPhaseError;
    private static volatile float _smoothing = 0.5f;

    // Published clock: bar time at the anchor, extrapolated with the BPM by the reader.
    private static double _anchorBarTime;
    private static double _anchorTimeS;
    private static double _bpm = 120;
    private static double _rawAnchorBars;
    private static double _rawBars;
    private static double _previousRawPhase = double.NaN;
    private static double _rawBpm = 120;

    private static float[] _monoBuffer = new float[4096];
    private static float[] _queue = new float[8192];
    private static float[] _workBuffer = new float[8192];
    private static int _queuedSampleCount;
    private static long _streamSampleCount;
    private static long _processedFrameCount;
    private static double _streamStartTimeS = double.NaN;
    private static double _resamplePos;
    private static float _lastMonoSample;
}

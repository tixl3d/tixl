#nullable enable
using System;
using System.IO;
using System.Threading;
using DanceAi;
using Microsoft.ML.OnnxRuntime;
using T3.Core.Animation;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Estimates the musical bar phase of the live WASAPI capture with the DanceAi neural model
/// and exposes it as a smooth, monotonically advancing bar clock for beat locking.
/// </summary>
/// <remarks>
/// The capture callback only downmixes and resamples into a queue; inference (~4 ms per 16 ms of
/// audio) runs on a dedicated background thread so it never stalls the audio driver.
/// The model files live next to the executable in <c>dance-phase/</c> and are loaded on first use.
/// </remarks>
public static class BarPhaseTracker
{
    /// <summary>True once the model is loaded and estimates are flowing.</summary>
    public static bool IsAvailable => _estimator != null && _hasEstimate;

    /// <summary>Short human-readable state for the settings UI.</summary>
    public static string StatusMessage
    {
        get
        {
            if (_loadError != null)
                return _loadError;

            if (_estimator == null)
                return "Loading bar-phase model...";

            return _hasEstimate ? "Tracking" : "Waiting for audio...";
        }
    }

    /// <summary>Continuous bar count since tracking started, extrapolated to the current runtime.</summary>
    public static double BarProgress
    {
        get
        {
            lock (_stateLock)
            {
                return _extrapolator.UnwrappedPhaseAt(Playback.RunTimeInSecs * 1000);
            }
        }
    }

    /// <summary>Estimated tempo, assuming four beats per bar.</summary>
    public static double CurrentBpm
    {
        get
        {
            lock (_stateLock)
            {
                return _extrapolator.GetRate() * 4 * 60;
            }
        }
    }

    /// <summary>Expected |phase error| in bars, in [0, 0.5]. Lower is more confident.</summary>
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
            _extrapolator.Reset();
            _hasEstimate = false;
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
                foreach (var estimate in _estimator.Feed(new ReadOnlySpan<float>(_workBuffer, 0, count)))
                {
                    _processedFrameCount++;
                    var frameEndTimeMs = (streamStartTimeS + _processedFrameCount * frameSize / (double)ModelSampleRate) * 1000;
                    lock (_stateLock)
                    {
                        _extrapolator.Update(estimate.Phase, estimate.BarDurationS, frameEndTimeMs);
                    }

                    _expectedPhaseError = (float)estimate.ExpectedPhaseError;
                    _hasEstimate = true;
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

    private static readonly object _stateLock = new();
    private static readonly object _queueLock = new();
    private static readonly AutoResetEvent _samplesAvailable = new(false);
    private static readonly PhaseExtrapolator _extrapolator = new();

    private static Thread? _worker;
    private static DancePhaseEstimator? _estimator;
    private static volatile string? _loadError;
    private static volatile bool _hasEstimate;
    private static volatile float _expectedPhaseError;

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

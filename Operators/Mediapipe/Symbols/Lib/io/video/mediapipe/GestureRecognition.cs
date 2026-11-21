using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using T3.Graphics.Compat;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using Google.Protobuf;
using Mediapipe;
#nullable enable

using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Framework.Formats;
using T3.Core.Resource.Assets;
using Landmark = Mediapipe.Landmark;
using Image = Mediapipe.Framework.Formats.Image;

namespace Lib.io.video.mediapipe;

internal class GestureRecognitionRequest
{
    public byte[]? PixelData;
    public int Width;
    public int Height;
    public long Timestamp;
    public float MinHandDetectionConfidence;
    public float MinHandPresenceConfidence;
    public int MaxHands;
    public string[]? CategoryAllowlist;
    public string[]? CategoryDenylist;
}

internal class GestureRecognitionResultPacket
{
    public Point[]? Landmarks;
    public int HandCount;
    public string[]? Handedness;
    public Point[]? WorldLandmarks;
}

[Guid("A1B2C3D4-E5F6-4798-89AB-CDEF12345683")]
[ExportDependencies("mediapipe_c.dll", "libmediapipe_c.so", "libmediapipe_c.dylib", "libmediapipe_opencl_stub.dylib", "OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib", "Emgu.CV.dll", "cvextern.dll", "OpenGL.Net.dll", "CppSharp.Runtime.dll", "Google.Protobuf.dll")]
public class GestureRecognition : Instance<GestureRecognition>
{
    [Output(Guid = "B2C3D4E5-F6A7-489A-9B0C-DEF123456784", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> OutputTexture = new();

    [Output(Guid = "B2C3D4E5-F6A7-489A-9B0C-DEF123456794", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> DebugTexture = new();

    [Output(Guid = "C3D4E5F6-A7B8-49AB-AC1D-EF1234567895", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<string>> RecognizedGestures = new();

    [Output(Guid = "4B1C2D3E-5F6A-7B8C-9D0E-1F2A3B4C5D6E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> PrimaryGesture = new();

    [Output(Guid = "D4E5F6A7-B8C9-4AB0-BD2E-F12345678906", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Confidence = new();

    [Output(Guid = "E5F6A7B8-C9D0-4B01-CE3F-1234A5678907", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> HandCount = new();

    [Output(Guid = "F6A7B8C9-D0E1-4C12-DF4A-234567A89008", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Output(Guid = "07B8C9D0-E1F2-4D23-E05B-345678901239", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<string>> Handedness = new();

    [Output(Guid = "29D0E1F2-A3B4-4F45-A27D-567890123451", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews> WorldLandmarksBuffer = new();

    [Output(Guid = "3A0E1F2A-3B4C-5D6E-7F89-012345678902", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews> PointBuffer = new();

    public GestureRecognition()
    {
        OutputTexture.UpdateAction = Update;
        DebugTexture.UpdateAction = Update;
        RecognizedGestures.UpdateAction = Update;
        PrimaryGesture.UpdateAction = Update;
        Confidence.UpdateAction = Update;
        HandCount.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        Handedness.UpdateAction = Update;
        WorldLandmarksBuffer.UpdateAction = Update;
        PointBuffer.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        // The host can evaluate an instance after it has been disposed (hot reload, package
        // unload races). Starting a worker here would leak its native task and its thread for the
        // process lifetime, because Dispose has already run.
        if (_disposed)
            return;

        var inputTexture = InputTexture.GetValue(context);
        var enabled = Enabled.GetValue(context);
        var debug = Debug.GetValue(context);
        var minHandDetectionConfidence = MinHandDetectionConfidence.GetValue(context);
        var minHandPresenceConfidence = MinHandPresenceConfidence.GetValue(context);
        var maxHands = MaxHands.GetValue(context);
        var useCombinedAIOutput = UseCombinedAIOutput.GetValue(context);
        var categoryAllowlist = CategoryAllowlist.GetValue(context);
        var categoryDenylist = CategoryDenylist.GetValue(context);
        var correctAspectRatio = CorrectAspectRatio.GetValue(context);
        var zScale = ZScale.GetValue(context);

        if (!enabled || inputTexture == null || inputTexture.IsDisposed)
        {
            OutputTexture.Value = inputTexture!;
            StopWorker();
            ClearOutputs();
            return;
        }

        OutputTexture.Value = inputTexture;

        var acceleration = Acceleration.GetValue(context);
        var inferenceThreads = InferenceThreads.GetValue(context);
        var parametersChanged = maxHands != _activeMaxHands
                                || acceleration != _activeAcceleration
                                || inferenceThreads != _activeInferenceThreads
                                || Math.Abs(minHandDetectionConfidence - _activeMinHandDetectionConfidence) > 0.001f
                                || Math.Abs(minHandPresenceConfidence - _activeMinHandPresenceConfidence) > 0.001f;
        if (parametersChanged)
            _initAttempts = 0;

        var initializing = _handLandmarker is null && _processingTask is { IsCompleted: false };
        var initNeeded = !initializing && (_processingTask == null || _processingTask.IsCompleted || _handLandmarker is null || parametersChanged);
        var retryDelayMs = MediaPipeInitRetry.BackoffMs(_initAttempts);
        var retryBlocked = _handLandmarker == null && _initAttempts > 0
                           && Environment.TickCount64 - _lastInitAttemptTicks < retryDelayMs;

        if (initNeeded && !retryBlocked)
        {
            _activeAcceleration = acceleration;
            _activeInferenceThreads = inferenceThreads;
            _lastInitAttemptTicks = Environment.TickCount64;
            _initAttempts++;
            InitializeWorker(maxHands, minHandDetectionConfidence, minHandPresenceConfidence, debug);
        }

        if (_handLandmarker == null)
        {
            ClearOutputs();
            return;
        }

        if (_inputQueue.IsEmpty && !_workerBusy)
        {
            var request = CreateRequestFromTexture(inputTexture, minHandDetectionConfidence, minHandPresenceConfidence, maxHands, categoryAllowlist!, categoryDenylist!);
            if (request != null)
            {
                _inputQueue.Enqueue(request);
            }
        }

        while (_outputQueue.TryDequeue(out var result))
        {
            if (_currentResult != null)
            {
                ReturnPointArrayToPool(_currentResult.Landmarks);
                ReturnPointArrayToPool(_currentResult.WorldLandmarks);
            }
            _currentResult = result;
        }

        if (_currentResult != null)
        {
            UpdateOutputsWithResult(_currentResult, useCombinedAIOutput, correctAspectRatio, zScale, inputTexture);
        }
        else
        {
            RecognizedGestures.Value = new List<string> { "None" };
            PrimaryGesture.Value = "None";
            Confidence.Value = 0.0f;
            HandCount.Value = 0;
            Handedness.Value = new List<string>();
            WorldLandmarksBuffer.Value = null!;
            PointBuffer.Value = null!;
        }

        if (debug)
        {
            using var mat = Texture2DToMat(inputTexture);
            if (!mat.Empty())
            {
                if (_currentResult != null)
                {
                    DrawDebugVisualsFromLandmarks(mat, _currentResult.Landmarks);
                }
                UpdateDebugTextureFromMat(mat);
            }
        }
        else
        {
            DebugTexture.Value = null!;
        }
    }

    private void ClearOutputs()
    {
        RecognizedGestures.Value = new List<string> { "None" };
        PrimaryGesture.Value = "None";
        Confidence.Value = 0.0f;
        HandCount.Value = 0;

        if (_currentResult != null)
        {
            ReturnPointArrayToPool(_currentResult.Landmarks);
            ReturnPointArrayToPool(_currentResult.WorldLandmarks);
            _currentResult = null;
        }

        DebugTexture.Value = null!;
        Handedness.Value = new List<string>();
        WorldLandmarksBuffer.Value = null!;
        PointBuffer.Value = null!;
    }

    #region MediaPipe Integration
    private const int MaxInferenceWidth = 640;
    private const int MaxCachedStagingTextures = 4;
    private const int MaxPooledBuffers = 4;

    private static readonly int MaxDownscaleThreads = Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>Xnnpack worker threads used when the InferenceThreads input keeps its default.
    /// See <see cref="MediaPipeInferenceTuning"/> for why the binding's 0 is a bad default.</summary>
    private const int DefaultInferenceThreads = MediaPipeInferenceTuning.DefaultThreads;

    private HandLandmarker? _handLandmarker;
    private long _frameTimestamp;
    private readonly object _handLandmarkerLock = new object();
    private readonly object _timestampLock = new object();

    private Task? _processingTask;
    private volatile bool _workerBusy;
    private long _lastInitAttemptTicks;
    private int _initAttempts;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<GestureRecognitionRequest> _inputQueue = new();
    private readonly ConcurrentQueue<GestureRecognitionResultPacket> _outputQueue = new();
    private GestureRecognitionResultPacket? _currentResult;
    private int _activeMaxHands = -1;
    private MediaPipeAcceleration? _activeAcceleration;
    private int _activeInferenceThreads = -1;
    private float _activeMinHandDetectionConfidence = -1f;
    private float _activeMinHandPresenceConfidence = -1f;
    private volatile bool _disposed;
    private readonly object _workerLock = new object();
    private readonly MediaPipeWorkerReaper _reaper = new();

    private readonly ConcurrentDictionary<(int width, int height, T3.Graphics.Format format), T3.Graphics.Compat.Texture2D> _cachedStagingTextures = new();

    private readonly object _textureCacheLock = new object();

    private readonly ConcurrentBag<byte[]> _bufferPool = new();
    private readonly ConcurrentDictionary<int, ConcurrentBag<Point[]>> _pointArrayPool = new();
    private readonly object _poolLock = new object();

    #endregion

    #region Worker Thread
    private void InitializeWorker(int maxHands, float minHandDetectionConfidence, float minHandPresenceConfidence, bool debug)
    {
        StopWorker();
        _activeMaxHands = maxHands;
        _activeMinHandDetectionConfidence = minHandDetectionConfidence;
        _activeMinHandPresenceConfidence = minHandPresenceConfidence;

        // Registry lookup and a cheap file check stay here; the native landmarker creation runs on the worker.
        var modelFound = MediaPipeModelResolver.TryResolve("hand_landmarker.task", this, out var fullPath);
        if (!modelFound)
        {
            // Not Debug-gated: a missing asset is the one failure a user has to act on, and the retry
                // backoff keeps this to one message per backoff window.
                Log.Warning($"[GestureRecognition] Model not found: {fullPath}", this);
            return;
        }

        fullPath = fullPath.Replace("\\", "/");

        // An empty file means a broken download or an unresolved Git LFS pointer; reading the length can
        // itself fail if the asset vanished between the registry lookup and here. Neither may throw out
        // of the evaluation loop.
        try
        {
            if (new System.IO.FileInfo(fullPath).Length == 0)
            {
                Log.Warning($"[GestureRecognition] Model file is empty (check Git LFS?): {fullPath}", this);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[GestureRecognition] Cannot read model file: {ex.Message}", this);
            return;
        }

        var acceleration = _activeAcceleration ?? MediaPipeAcceleration.Cpu;
        var inferenceThreads = _activeInferenceThreads;

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        _processingTask = Task.Run(() => InitializeAndRunWorker(fullPath, acceleration, inferenceThreads, maxHands,
                                                                minHandDetectionConfidence, minHandPresenceConfidence,
                                                                token, debug), token);
    }

    private void InitializeAndRunWorker(string fullPath, MediaPipeAcceleration acceleration, int inferenceThreads, int maxHands,
                                        float minHandDetectionConfidence, float minHandPresenceConfidence,
                                        CancellationToken token, bool debug)
    {
        HandLandmarker? landmarker = null;
        try
        {
            var delegateCase = acceleration == MediaPipeAcceleration.Xnnpack ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.Xnnpack
                : acceleration == MediaPipeAcceleration.Gpu ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.GPU
                : Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU;

            HandLandmarkerOptions BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate selected) => new(
                new Mediapipe.Tasks.Core.CoreBaseOptions(modelAssetPath: fullPath, delegateCase: selected, numThreads: inferenceThreads),
                VisionRunningMode.VIDEO,
                maxHands
            );

            try
            {
                landmarker = HandLandmarker.CreateFromOptions(BuildOptions(delegateCase));
            }
            catch (Exception gpuEx) when (acceleration != MediaPipeAcceleration.Cpu)
            {
                Log.Warning($"[GestureRecognition] GPU acceleration not available ({gpuEx.Message}) - using CPU.", this);
                landmarker = HandLandmarker.CreateFromOptions(BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU));
            }

            if (landmarker == null)
                return;

            var published = false;
            lock (_handLandmarkerLock)
            {
                if (!token.IsCancellationRequested)
                {
                    _handLandmarker = landmarker;
                    published = true;
                }
            }

            if (!published)
            {
                try { MediaPipeWorkerReaper.CloseNative(landmarker); } catch { }
                return;
            }

            // A later failure starts its backoff from the first step again.
            _initAttempts = 0;

            // The worker loop owns this instance; StopWorker closes it only after this task exits.
            WorkerLoop(token, landmarker, debug);
        }
        catch (Exception ex)
        {
            // Warning, not Debug-gated: this is the path a missing native library, a rejected
            // delegate or an unreadable model takes, and "nothing happens" with no log is
            // the worst failure mode. The retry backoff limits it to one message per window.
            Log.Warning($"[GestureRecognition] Initialization failed: {ex.Message}", this);
            if (landmarker != null)
            {
                try { MediaPipeWorkerReaper.CloseNative(landmarker); } catch { }
                lock (_handLandmarkerLock)
                {
                    if (ReferenceEquals(_handLandmarker, landmarker))
                        _handLandmarker = null;
                }
            }
        }
    }
    private void WorkerLoop(CancellationToken token, HandLandmarker landmarker, bool debug)
    {
        while (!token.IsCancellationRequested)
        {
            if (_inputQueue.TryDequeue(out var request))
            {
                // Whatever piled up while the previous inference ran is stale by now - only the
                // newest frame is worth its latency, the rest goes straight back to the pool.
                while (_inputQueue.TryDequeue(out var newer))
                {
                    ReturnBufferToPool(request.PixelData);
                    request = newer;
                }

                try
                {
                    if (request.PixelData != null)
                    {
                        _workerBusy = true;
                        ProcessFrame(request, landmarker, debug);
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[GestureRecognition] Worker error: {ex.Message}", this);
                }
                finally
                {
                    _workerBusy = false;
                    ReturnBufferToPool(request.PixelData);
                }
            }
            else
            {
                Thread.Sleep(1);
            }
        }
    }

    private void ProcessFrame(GestureRecognitionRequest request, HandLandmarker landmarker, bool debug)
    {
        using var image = new Image(ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        var result = landmarker.DetectForVideo(image, request.Timestamp);

        if (result.HandLandmarks != null && result.HandLandmarks.Count > 0)
        {
            var landmarks = ConvertHandLandmarkerResultToLandmarks(result, request.MinHandDetectionConfidence, request.MinHandPresenceConfidence, request.MaxHands, request.Width, request.Height, debug);
            var worldLandmarks = ConvertHandLandmarkerResultToWorldLandmarks(result, request.MaxHands, debug);
            var handedness = ConvertHandedness(result);

            var packet = new GestureRecognitionResultPacket
            {
                Landmarks = landmarks,
                HandCount = landmarks != null ? landmarks.Length / 21 : 0,
                Handedness = handedness,
                WorldLandmarks = worldLandmarks
            };
            _outputQueue.Enqueue(packet);
        }
        else
        {
            _outputQueue.Enqueue(new GestureRecognitionResultPacket
            {
                Landmarks = null,
                HandCount = 0,
                Handedness = null,
                WorldLandmarks = null
            });
        }
    }

    private void StopWorker()
    {
        lock (_workerLock)
        {
            var cancellation = _cancellationTokenSource;
            _cancellationTokenSource = null;
            cancellation?.Cancel();

            var task = _processingTask;
            _processingTask = null;

            HandLandmarker? nativeTask;
            lock (_handLandmarkerLock)
            {
                nativeTask = _handLandmarker;
                _handLandmarker = null;
            }

            // The task API blocks inside native inference and offers no cancellation, so the native
            // instance may only be closed after this worker has fully exited - closing it earlier is a
            // use-after-free in native code. The reaper waits on the thread pool, so the evaluation
            // thread never blocks on an in-flight inference.
            _reaper.Queue(task, nativeTask, cancellation);

            while (_inputQueue.TryDequeue(out var pendingRequest))
            {
                ReturnBufferToPool(pendingRequest.PixelData);
            }

            // Results still in flight when the worker stopped own pooled arrays.
            while (_outputQueue.TryDequeue(out var staleResult))
            {
                ReturnPointArrayToPool(staleResult.Landmarks);
                ReturnPointArrayToPool(staleResult.WorldLandmarks);
            }
        }
    }

    #endregion

    #region Memory Management
    private T3.Graphics.Compat.Texture2D GetOrCreateStagingTexture(int width, int height, T3.Graphics.Format format)
    {
        var key = (width, height, format);

        if (_cachedStagingTextures.TryGetValue(key, out var cachedTexture))
        {
            return cachedTexture;
        }

        lock (_textureCacheLock)
        {
            if (_cachedStagingTextures.TryGetValue(key, out cachedTexture))
            {
                return cachedTexture;
            }

            var device = ResourceManager.Device;
            var newTexture = new T3.Graphics.Compat.Texture2D(device, new Texture2DDescription
            {
                Width = width, Height = height, MipLevels = 1, ArraySize = 1,
                Format = format, SampleDescription = new T3.Graphics.SampleDescription(1, 0),
                Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
            });

            // A resized window or a switched camera produces a new size per step, so an uncapped
            // cache would keep one full-resolution staging texture per distinct size for the life
            // of the operator. Only the current size is reused frame to frame.
            if (_cachedStagingTextures.Count >= MaxCachedStagingTextures)
            {
                foreach (var stale in _cachedStagingTextures)
                {
                    stale.Value?.Dispose();
                }
                _cachedStagingTextures.Clear();
            }

            _cachedStagingTextures[key] = newTexture;
            return newTexture;
        }
    }

    private byte[] GetBuffer(int size)
    {
        if (_bufferPool.TryTake(out var b) && b.Length == size) return b;

        // Pinned on the POH: the native Image keeps this pointer and reads it during
        // DetectForVideo, after the wrapper's own pin over the constructor has been released.
        return GC.AllocateUninitializedArray<byte>(size, pinned: true);
    }

    private void ReturnBufferToPool(byte[]? b)
    {
        // Bounded: each buffer is pinned for its whole life, so a resolution change must not
            // leave one permanently pinned array per historic size behind.
            if (b != null && _bufferPool.Count < MaxPooledBuffers) _bufferPool.Add(b);
    }

    private Point[] GetPointArray(int size)
    {
        if (!_pointArrayPool.TryGetValue(size, out var pool))
        {
            lock (_poolLock)
            {
                if (!_pointArrayPool.TryGetValue(size, out pool))
                {
                    pool = new ConcurrentBag<Point[]>();
                    _pointArrayPool[size] = pool;
                }
            }
        }

        if (pool.TryTake(out var arr))
        {
            return arr;
        }

        return new Point[size];
    }

    private void ReturnPointArrayToPool(Point[]? arr)
    {
        if (arr != null)
        {
            var size = arr.Length;
            if (!_pointArrayPool.TryGetValue(size, out var pool))
            {
                lock (_poolLock)
                {
                    if (!_pointArrayPool.TryGetValue(size, out pool))
                    {
                        pool = new ConcurrentBag<Point[]>();
                        _pointArrayPool[size] = pool;
                    }
                }
            }
            pool.Add(arr);
        }
    }

    private GestureRecognitionRequest? CreateRequestFromTexture(Texture2D texture, float minHandDetectionConfidence, float minHandPresenceConfidence, int maxHands, string categoryAllowlist, string categoryDenylist)
    {
        var desc = texture.Description;

        // One readback per evaluation frame serves every MediaPipe operator reading this texture: the
        // mapped copy costs ~2 ms at 1080p and barely less at 640 px, so seven operators on one camera
        // would otherwise pay it seven times for byte-identical pixels.
        if (!MediaPipeFrameCache.TryGet(texture, MaxInferenceWidth, 3, out var frame,
                                        out var inferenceWidth, out var inferenceHeight, out var readbackError))
        {
            Log.Debug($"[GestureRecognition] Texture readback failed: {readbackError}", this);
            return null;
        }

        // The cached array is shared and read-only for this frame; the worker still gets its own pooled
        // copy because the native Image keeps that pointer for the duration of the inference.
        byte[] buffer = GetBuffer(frame.Length);
        System.Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);

        long ts;
        lock(_timestampLock) { ts = _frameTimestamp; _frameTimestamp += 33; }

        return new GestureRecognitionRequest
        {
        PixelData = buffer,
        Width = inferenceWidth,
        Height = inferenceHeight,
        Timestamp = ts,
        MinHandDetectionConfidence = minHandDetectionConfidence,
        MinHandPresenceConfidence = minHandPresenceConfidence,
        MaxHands = maxHands,
        CategoryAllowlist = string.IsNullOrEmpty(categoryAllowlist) ? null : categoryAllowlist.Split(','),
        CategoryDenylist = string.IsNullOrEmpty(categoryDenylist) ? null : categoryDenylist.Split(',')
        };
    }
    #endregion Memory Management

    private void UpdateOutputsWithResult(GestureRecognitionResultPacket result, bool useCombinedAIOutput, bool correctAspectRatio, float zScale, Texture2D inputTexture)
    {
        if (result.Landmarks == null || result.Landmarks.Length == 0)
        {
            RecognizedGestures.Value = new List<string> { "None" };
            PrimaryGesture.Value = "None";
            Confidence.Value = 0.0f;
            HandCount.Value = 0;
            Handedness.Value = new List<string>();
            WorldLandmarksBuffer.Value = null!;
            PointBuffer.Value = null!;
            return;
        }

        HandCount.Value = result.HandCount;
        UpdateCount.Value++;
        Handedness.Value = result.Handedness != null ? new List<string>(result.Handedness) : new List<string>();

        var gesture = RecognizeBasicGesture(result.Landmarks);
        RecognizedGestures.Value = new List<string> { gesture.Name };
        PrimaryGesture.Value = gesture.Name;
        Confidence.Value = gesture.Confidence;

        float aspectRatio = 1.0f;
        if (correctAspectRatio && inputTexture != null!)
        {
            aspectRatio = (float)inputTexture.Description.Width / inputTexture.Description.Height;
        }

        var processedLandmarks = new Point[result.Landmarks.Length];
        for (int i = 0; i < result.Landmarks.Length; i++)
        {
            var p = result.Landmarks[i];

            p.Position.X *= 2.0f;
            if (correctAspectRatio)
            {
                p.Position.X *= aspectRatio;
            }

            p.Position.Y *= 2.0f;
            p.Position.Z *= zScale;

            processedLandmarks[i] = p;
        }

        UpdateBuffer(result.WorldLandmarks, ref _worldLandmarksBufferWithViews, WorldLandmarksBuffer);
        UpdateBuffer(processedLandmarks, ref _pointBufferWithViews, PointBuffer);

        if (useCombinedAIOutput)
        {
            GenerateAITextures(processedLandmarks, gesture.Name, gesture.Confidence);
        }
    }

    private BufferWithViews? _worldLandmarksBufferWithViews;
    private BufferWithViews? _pointBufferWithViews;

    private void UpdateBuffer(Point[]? points, ref BufferWithViews? bufferWithViews, Slot<BufferWithViews> slot)
    {
        if (points == null || points.Length == 0)
        {
            slot.Value = null!;
            return;
        }

        int stride = System.Runtime.InteropServices.Marshal.SizeOf<T3.Core.DataTypes.Point>();

        if (bufferWithViews == null)
        {
            bufferWithViews = new BufferWithViews();
        }

        var structuredList = new StructuredList<T3.Core.DataTypes.Point>(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            structuredList.TypedElements[i] = points[i];
        }

        ResourceManager.SetupStructuredBuffer(structuredList.TypedElements, stride * points.Length, stride, ref bufferWithViews.Buffer);

        if (bufferWithViews.Buffer != null)
        {
             if (bufferWithViews.Srv == null || bufferWithViews.Srv.Description.Buffer.ElementCount != points.Length)
             {
                 bufferWithViews.Srv?.Dispose();
                 bufferWithViews.Srv = new ShaderResourceView(ResourceManager.Device, bufferWithViews.Buffer,
                     new ShaderResourceViewDescription
                     {
                         Format = T3.Graphics.Format.Unknown,
                         Dimension = ShaderResourceViewDimension.Buffer,
                         Buffer = new ShaderResourceViewDescription.BufferResource
                         {
                             ElementWidth = points.Length,
                             FirstElement = 0
                         }
                     });
             }

             if (bufferWithViews.Uav == null || bufferWithViews.Uav.Description.Buffer.ElementCount != points.Length)
             {
                 bufferWithViews.Uav?.Dispose();
                 bufferWithViews.Uav = new UnorderedAccessView(ResourceManager.Device, bufferWithViews.Buffer,
                     new UnorderedAccessViewDescription
                     {
                         Format = T3.Graphics.Format.Unknown,
                         Dimension = UnorderedAccessViewDimension.Buffer,
                         Buffer = new UnorderedAccessViewDescription.BufferResource
                         {
                             ElementCount = points.Length,
                             FirstElement = 0,
                             Flags = UnorderedAccessViewBufferFlags.None
                         }
                     });
             }

             slot.Value = bufferWithViews;
        }
    }

    private void DrawDebugVisualsFromLandmarks(Mat mat, Point[]? landmarks)
    {
        if (landmarks == null || landmarks.Length == 0) return;

        var landmarkColor = new Scalar(0, 255, 0, 255);
        var connectionColor = new Scalar(255, 0, 0, 255);
        var palmColor = new Scalar(255, 255, 255, 255);

        int matWidth = mat.Width;
        int matHeight = mat.Height;
        float aspectRatio = (float)matWidth / matHeight;

        for (int i = 0; i < landmarks.Length; i++)
        {
            var point = landmarks[i];

            int x = (int)((point.Position.X / aspectRatio / 2.0f + 0.5f) * matWidth);
            int y = (int)((0.5f - point.Position.Y / 2.0f) * matHeight);

            Cv2.Circle(mat, x, y, 2, landmarkColor, -1, LineTypes.AntiAlias);
        }
    }

    private Texture2D? _debugTexture;

    private void UpdateDebugTextureFromMat(Mat mat)
    {
        if (_debugTexture == null || _debugTexture.Description.Width != mat.Width || _debugTexture.Description.Height != mat.Height)
        {
            _debugTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = mat.Width,
                Height = mat.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = T3.Graphics.Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _debugTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, desc));
        }

        var context = ResourceManager.Device.ImmediateContext;
        var dataBox = new DataBox(mat.Data, (int)mat.Step(), 0);
        context.UpdateSubresource(dataBox, _debugTexture, 0);

        DebugTexture.Value = _debugTexture;
    }

    private Point[]? ConvertHandLandmarkerResultToLandmarks(HandLandmarkerResult result, float minHandDetectionConfidence, float minHandPresenceConfidence, int maxHands, int width, int height, bool debug)
    {
        if (result.HandLandmarks == null) return null;

        float aspectRatio = (float)width / height;

        // Count first so the array can come from the pool - Update's result-replacement
        // path returns it, keeping rentals and returns balanced.
        var total = 0;
        for (int handIndex = 0; handIndex < result.HandLandmarks.Count && handIndex < maxHands; handIndex++)
        {
            var handLandmarks = result.HandLandmarks[handIndex];
            if (handLandmarks.landmarks != null && handLandmarks.landmarks.Count > 0)
                total += Math.Min(handLandmarks.landmarks.Count, 21);
        }
        if (total == 0) return null;

        var landmarks = GetPointArray(total);

        try
        {
            int landmarkIndex = 0;
            for (int handIndex = 0; handIndex < result.HandLandmarks.Count && handIndex < maxHands; handIndex++)
            {
                var handLandmarks = result.HandLandmarks[handIndex];
                if (handLandmarks.landmarks == null || handLandmarks.landmarks.Count == 0)
                    continue;

                for (int i = 0; i < handLandmarks.landmarks.Count && i < 21; i++)
                {
                    var landmark = handLandmarks.landmarks[i];

                    var normalizedX = (landmark.X - 0.5f) * 2.0f * aspectRatio;

                    var normalizedY = (1.0f - landmark.Y - 0.5f) * 2.0f;

                    landmarks[landmarkIndex++] = new Point
                    {
                        Position = new Vector3(normalizedX, normalizedY, landmark.Z),
                        F1 = i,
                        F2 = landmark.Visibility ?? 0f,
                        Color = Vector4.One,
                        Scale = Vector3.One,
                        Orientation = Quaternion.Identity
                    };
                }
            }

            return landmarks;
        }
        catch (Exception ex)
        {
            ReturnPointArrayToPool(landmarks);
            if (debug) Log.Error($"[ConvertHandLandmarkerResultToLandmarks] Error converting MediaPipe landmarks: {ex.Message}", this);
            return null;
        }
    }

    private Point[]? ConvertHandLandmarkerResultToWorldLandmarks(HandLandmarkerResult result, int maxHands, bool debug)
    {
        if (result.HandWorldLandmarks == null) return null;

        var total = 0;
        for (int handIndex = 0; handIndex < result.HandWorldLandmarks.Count && handIndex < maxHands; handIndex++)
        {
            var handLandmarks = result.HandWorldLandmarks[handIndex];
            if (handLandmarks.landmarks != null && handLandmarks.landmarks.Count > 0)
                total += Math.Min(handLandmarks.landmarks.Count, 21);
        }
        if (total == 0) return null;

        var landmarks = GetPointArray(total);

        try
        {
            int landmarkIndex = 0;
            for (int handIndex = 0; handIndex < result.HandWorldLandmarks.Count && handIndex < maxHands; handIndex++)
            {
                var handLandmarks = result.HandWorldLandmarks[handIndex];
                if (handLandmarks.landmarks == null || handLandmarks.landmarks.Count == 0)
                    continue;

                for (int i = 0; i < handLandmarks.landmarks.Count && i < 21; i++)
                {
                    var landmark = handLandmarks.landmarks[i];

                    landmarks[landmarkIndex++] = new Point
                    {
                        Position = new Vector3(landmark.X, landmark.Y, landmark.Z),
                        F1 = i,
                        F2 = landmark.Visibility ?? 0f,
                        Color = Vector4.One,
                        Scale = Vector3.One,
                        Orientation = Quaternion.Identity
                    };
                }
            }

            return landmarks;
        }
        catch (Exception ex)
        {
            ReturnPointArrayToPool(landmarks);
            if (debug) Log.Error($"[ConvertHandLandmarkerResultToWorldLandmarks] Error converting MediaPipe world landmarks: {ex.Message}", this);
            return null;
        }
    }

    private string[]? ConvertHandedness(HandLandmarkerResult result)
    {
        if (result.Handedness == null!) return null;

        var handednessList = new List<string>();
        foreach (var handedness in result.Handedness)
        {
            if (handedness.Categories != null! && handedness.Categories.Count > 0)
            {
                handednessList.Add(handedness.Categories[0].CategoryName!);
            }
        }
        return handednessList.ToArray();
    }

    #region Gesture Recognition Logic
    private (string Name, float Confidence) RecognizeBasicGesture(Point[] landmarks)
    {
        if (landmarks.Length < 21) return ("None", 0.0f);

        var thumbTip = landmarks[4];
        var indexTip = landmarks[8];
        var middleTip = landmarks[12];
        var ringTip = landmarks[16];
        var pinkyTip = landmarks[20];

        var indexBase = landmarks[5];
        var middleBase = landmarks[9];
        var ringBase = landmarks[13];
        var pinkyBase = landmarks[17];

        var wrist = landmarks[0];

        bool thumbIsUp = thumbTip.Position.Y > indexBase.Position.Y;
        bool indexIsUp = indexTip.Position.Y > indexBase.Position.Y;
        bool middleIsUp = middleTip.Position.Y > middleBase.Position.Y;
        bool ringIsUp = ringTip.Position.Y > ringBase.Position.Y;
        bool pinkyIsUp = pinkyTip.Position.Y > pinkyBase.Position.Y;

        if (!indexIsUp && !middleIsUp && !ringIsUp && !pinkyIsUp && thumbIsUp)
        {
            return ("Thumbs Up", 0.9f);
        }
        else if (indexIsUp && !middleIsUp && !ringIsUp && !pinkyIsUp)
        {
            return ("Point", 0.8f);
        }
        else if (indexIsUp && middleIsUp && !ringIsUp && pinkyIsUp)
        {
            return ("Peace", 0.8f);
        }
        else if (indexIsUp && middleIsUp && ringIsUp && !pinkyIsUp)
        {
            return ("Open Palm", 0.7f);
        }
        else if (!indexIsUp && !middleIsUp && !ringIsUp && !pinkyIsUp && !thumbIsUp)
        {
            return ("Fist", 0.8f);
        }

        return ("None", 0.1f);
    }
    #endregion

    #region AI Texture Generation
    private Texture2D? _aiDataTexture;
    private Texture2D? _aiDataHighPrecisionTexture;
    private Texture2D? _aiDataSegmentationTexture;
    private readonly object _aiTextureLock = new object();

    private void GenerateAITextures(Point[] landmarks, string gestureName, float gestureConfidence)
    {
        if (landmarks == null || landmarks.Length == 0)
        {
            lock (_aiTextureLock)
            {
                _aiDataTexture?.Dispose();
                _aiDataTexture = null;
                _aiDataHighPrecisionTexture?.Dispose();
                _aiDataHighPrecisionTexture = null;
                _aiDataSegmentationTexture?.Dispose();
                _aiDataSegmentationTexture = null;
            }
            return;
        }

        lock (_aiTextureLock)
        {
            try
            {
                int maxHands = 2;
                int maxLandmarksPerHand = 21;
                int totalDataPoints = 4 + (maxHands * maxLandmarksPerHand) + 1;

                int textureWidth = 1;
                int textureHeight = 1;
                while (textureWidth * textureHeight < totalDataPoints)
                {
                    if (textureWidth <= textureHeight)
                        textureWidth *= 2;
                    else
                        textureHeight *= 2;
                }

                CreateOrUpdateTexture(ref _aiDataTexture, textureWidth, textureHeight, T3.Graphics.Format.R32G32B32A32_Float);

                CreateOrUpdateTexture(ref _aiDataHighPrecisionTexture, textureWidth, textureHeight, T3.Graphics.Format.R16G16_Float);

                CreateOrUpdateTexture(ref _aiDataSegmentationTexture, textureWidth, textureHeight, T3.Graphics.Format.R8_UNorm);

                FillAITextures(landmarks, gestureName, gestureConfidence, textureWidth, textureHeight);
            }
            catch (Exception ex)
            {
                Log.Error($"[GenerateAITextures] Error generating AI textures: {ex.Message}", this);
            }
        }
    }

    private void CreateOrUpdateTexture(ref Texture2D? texture, int width, int height, T3.Graphics.Format format)
    {
        if (texture == null || texture.Description.Width != width || texture.Description.Height != height || texture.Description.Format != format)
        {
            texture?.Dispose();

            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };

            texture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, desc));
        }
    }

    private void FillAITextures(Point[] landmarks, string gestureName, float gestureConfidence, int textureWidth, int textureHeight)
    {
        var rgba32FData = new float[textureWidth * textureHeight * 4];
        var rg16FData = new float[textureWidth * textureHeight * 2];
        var r8Data = new byte[textureWidth * textureHeight];

        rgba32FData[0] = 1.0f;
        rgba32FData[1] = 3.0f;
        rgba32FData[2] = 2.0f;
        rgba32FData[3] = 0.0f;
        rgba32FData[4] = 0.0f;

        int handCount = landmarks.Length / 21;
        rgba32FData[4] = (float)handCount;
        rgba32FData[5] = 0.0f;
        rgba32FData[6] = 0.0f;
        rgba32FData[7] = 0.0f;

        for (int i = 8; i < 16; i++)
        {
            rgba32FData[i] = 0.0f;
        }

        int dataOffset = 16;
        int highPrecisionOffset = 0;

        for (int handIndex = 0; handIndex < Math.Min(handCount, 2); handIndex++)
        {
            for (int landmarkIndex = 0; landmarkIndex < 21; landmarkIndex++)
            {
                int pointIndex = handIndex * 21 + landmarkIndex;
                if (pointIndex < landmarks.Length)
                {
                    var point = landmarks[pointIndex];

                    rgba32FData[dataOffset + 0] = point.Position.X;
                    rgba32FData[dataOffset + 1] = point.Position.Y;
                    rgba32FData[dataOffset + 2] = point.Position.Z;
                    rgba32FData[dataOffset + 3] = point.F2;
                    dataOffset += 4;

                    if (highPrecisionOffset + 1 < rg16FData.Length)
                    {
                        rg16FData[highPrecisionOffset + 0] = point.Position.X;
                        rg16FData[highPrecisionOffset + 1] = point.Position.Y;
                        highPrecisionOffset += 2;

                        if (landmarkIndex % 2 == 1 && highPrecisionOffset + 1 < rg16FData.Length)
                        {
                            rg16FData[highPrecisionOffset + 0] = point.Position.Z;
                            highPrecisionOffset += 2;
                        }
                    }
                }
            }
        }

        int gestureId = GetGestureId(gestureName);
        rgba32FData[dataOffset + 0] = (float)gestureId;
        rgba32FData[dataOffset + 1] = gestureConfidence;
        rgba32FData[dataOffset + 2] = 0.0f;
        rgba32FData[dataOffset + 3] = 0.0f;
        dataOffset += 4;

        for (int i = 0; i < r8Data.Length; i++)
        {
            r8Data[i] = 0;
        }

        for (int handIndex = 0; handIndex < Math.Min(handCount, 2); handIndex++)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int landmarkIndex = 0; landmarkIndex < 21; landmarkIndex++)
            {
                int pointIndex = handIndex * 21 + landmarkIndex;
                if (pointIndex < landmarks.Length)
                {
                    var point = landmarks[pointIndex];
                    if (point.Position.X < minX) minX = point.Position.X;
                    if (point.Position.Y < minY) minY = point.Position.Y;
                    if (point.Position.X > maxX) maxX = point.Position.X;
                    if (point.Position.Y > maxY) maxY = point.Position.Y;
                }
            }

            int pixelX = (int)(minX * textureWidth);
            int pixelY = (int)(minY * textureHeight);
            int pixelWidth = (int)((maxX - minX) * textureWidth);
            int pixelHeight = (int)((maxY - minY) * textureHeight);

            for (int y = Math.Max(0, pixelY); y < Math.Min(textureHeight, pixelY + pixelHeight); y++)
            {
                for (int x = Math.Max(0, pixelX); x < Math.Min(textureWidth, pixelX + pixelWidth); x++)
                {
                    int index = y * textureWidth + x;
                    if (index < r8Data.Length)
                    {
                        r8Data[index] = (byte)(handIndex + 1);
                    }
                }
            }
        }

        if (_aiDataTexture != null)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(rgba32FData, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var dataBox = new DataBox(handle.AddrOfPinnedObject(), textureWidth * 4 * sizeof(float), 0);
                ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _aiDataTexture);
            }
            finally
            {
                handle.Free();
            }
        }

        if (_aiDataHighPrecisionTexture != null)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(rg16FData, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var dataBox = new DataBox(handle.AddrOfPinnedObject(), textureWidth * 2 * sizeof(float), 0);
                ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _aiDataHighPrecisionTexture);
            }
            finally
            {
                handle.Free();
            }
        }

        if (_aiDataSegmentationTexture != null)
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(r8Data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var dataBox = new DataBox(handle.AddrOfPinnedObject(), textureWidth, 0);
                ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _aiDataSegmentationTexture);
            }
            finally
            {
                handle.Free();
            }
        }
    }

    private int GetGestureId(string gestureName)
    {
        switch (gestureName)
        {
            case "None": return 0;
            case "Thumbs Up": return 1;
            case "Point": return 2;
            case "Peace": return 3;
            case "Open Palm": return 4;
            case "Fist": return 5;
            default: return 0;
        }
    }
    #endregion

    #region Cleanup
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        // Stops the worker task and hands the landmarker to its deferred closer - closing it here
        // directly could hit a frame still running on the worker thread.
        _disposed = true;
        StopWorker();

        // The reaper owns the native instance now; give a running inference a moment to
        // return, but never hang shutdown on a wedged native library.
        _reaper.WaitForPending(2000);

        lock (_aiTextureLock)
        {
            _aiDataTexture?.Dispose();
            _aiDataTexture = null;
            _aiDataHighPrecisionTexture?.Dispose();
            _aiDataHighPrecisionTexture = null;
            _aiDataSegmentationTexture?.Dispose();
            _aiDataSegmentationTexture = null;
        }

        _debugTexture?.Dispose();
        _debugTexture = null;

        lock (_textureCacheLock)
        {
            foreach (var cachedTexture in _cachedStagingTextures.Values)
            {
                cachedTexture?.Dispose();
            }
            _cachedStagingTextures.Clear();
        }


        lock (_poolLock)
        {
            _bufferPool.Clear();
        }

        lock (_poolLock)
        {
            foreach (var pool in _pointArrayPool.Values)
            {
                pool.Clear();
            }
            _pointArrayPool.Clear();
        }

        if (_worldLandmarksBufferWithViews != null)
        {
            _worldLandmarksBufferWithViews.Srv?.Dispose();
            _worldLandmarksBufferWithViews.Uav?.Dispose();
            _worldLandmarksBufferWithViews.Buffer?.Dispose();
            _worldLandmarksBufferWithViews = null;
        }

        if (_pointBufferWithViews != null)
        {
            _pointBufferWithViews.Srv?.Dispose();
            _pointBufferWithViews.Uav?.Dispose();
            _pointBufferWithViews.Buffer?.Dispose();
            _pointBufferWithViews = null;
        }

        base.Dispose(isDisposing);
    }
#endregion Cleanup

#region Input Parameters
    [Input(Guid = "A7B8C9D0-E1F2-4D23-E05B-345678901240")]
    public readonly InputSlot<Texture2D> InputTexture = new();

    [Input(Guid = "B8C9D0E1-F2A3-4E34-F16C-456789012341")]
    public readonly InputSlot<bool> Enabled = new(true);

    [Input(Guid = "C9D0E1F2-A3B4-4F45-A27D-567890123452")]
    public readonly InputSlot<int> MaxHands = new(2);

    [Input(Guid = "D0E1F2A3-B4C5-4056-B38E-678901234562")]
    public readonly InputSlot<float> MinHandDetectionConfidence = new(0.5f);

    [Input(Guid = "E1F2A3B4-C5D6-4167-C49F-789012345673")]
    public readonly InputSlot<float> MinHandPresenceConfidence = new(0.5f);

    [Input(Guid = "E2F3A4B5-C6D7-58E9-F0A1-234567890BD2")]
    public readonly InputSlot<bool> UseCombinedAIOutput = new(false);

    [Input(Guid = "A1B2C3D4-E5F6-4798-89AB-CDEF12345693")]
    public readonly InputSlot<bool> Debug = new(false);

    [Input(Guid = "F3A4B5C6-D7E8-69F0-12A3-456789012345")]
    public readonly InputSlot<string> CategoryAllowlist = new();

    [Input(Guid = "04B5C6D7-E8F9-70A1-23B4-567890123456")]
    public readonly InputSlot<string> CategoryDenylist = new();

    [Input(Guid = "15C6D7E8-F9A0-81B2-34D5-678901234567")]
    public readonly InputSlot<bool> CorrectAspectRatio = new(false);

    /// <summary>Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere),
    /// Xnnpack = multithreaded CPU (the fast default; see InferenceThreads), Gpu = OpenGL/EGL
    /// delegate (Linux native has it; the CPU-only Windows/macOS natives fall back to Cpu).</summary>
    [Input(Guid = "a1b2c3d4-0004-4a9b-8c2d-101010101010")]
    public readonly InputSlot<MediaPipeAcceleration> Acceleration = new(MediaPipeAcceleration.Xnnpack);

    /// <summary>Worker threads for the Xnnpack acceleration (0 = library default, which is effectively
        /// single-threaded). 4 is ~2x faster than the default on a 20-core machine; use 2 when
        /// several MediaPipe operators run at once. Ignored by Cpu and Gpu.</summary>
    [Input(Guid = "a1b2c3d4-1004-4a9b-8c2d-101010101010")]
    public readonly InputSlot<int> InferenceThreads = new(DefaultInferenceThreads);

    [Input(Guid = "B6D7E8F9-0AB1-92C3-45E6-789012345678")]
    public readonly InputSlot<float> ZScale = new(1.0f);

    #endregion Input Parameters
private Mat Texture2DToMat(Texture2D texture)
{
    var device = ResourceManager.Device;
    var desc = texture.Description;

    var stagingTexture = GetOrCreateStagingTexture(desc.Width, desc.Height, desc.Format);
    try
    {
        device.ImmediateContext.CopyResource(texture, stagingTexture);
    }
    catch (Exception ex)
    {
        Log.Warning($"[GestureRecognition] Debug readback failed: {ex.Message}", this);
        return new Mat();
    }

    var dataBox = device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);
    if (dataBox.DataPointer == IntPtr.Zero)
    {
        device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        return new Mat();
    }

    // Driver row pitch is not guaranteed to be width * 4: copying width * height * 4 bytes
    // contiguously shears the image whenever rows are padded, and on a tightly allocated
    // staging texture it reads past the mapped region. Copy row by row instead.
    var rowBytes = desc.Width * 4;
    if (dataBox.RowPitch < rowBytes)
    {
        Log.Warning($"[GestureRecognition] Debug readback: row pitch {dataBox.RowPitch} < {rowBytes}.", this);
        device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        return new Mat();
    }

    var mat = new Mat(desc.Height, desc.Width, MatType.CV_8UC4);
    try
    {
        var stride = (int)mat.Step();
        for (var y = 0; y < desc.Height; y++)
        {
            T3.Graphics.Compat.GraphicsUtilities.CopyMemory(mat.Data + y * stride,
                                                            dataBox.DataPointer + y * dataBox.RowPitch,
                                                            rowBytes);
        }
    }
    finally
    {
        device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
    }

    Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2BGRA);
    return mat;
}
}
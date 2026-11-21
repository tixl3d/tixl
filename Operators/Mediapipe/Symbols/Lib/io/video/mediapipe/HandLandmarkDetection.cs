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
using T3.Core.Resource.Assets;
#nullable enable

using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Framework.Formats;
using Landmark = Mediapipe.Landmark;
using Image = Mediapipe.Framework.Formats.Image;
using ImageFormat = Mediapipe.RegionFlowComputationOptions.Types.ImageFormat;
// ReSharper disable EmptyGeneralCatchClause

namespace Lib.io.video.mediapipe;

internal class HandLandmarkDetectionRequest
{
    public byte[]? PixelData;
    public int Width;
    public int Height;
    public long Timestamp;
    public long SentTickCount;
}

internal class HandLandmarkDetectionResultPacket
{
    public Point[]? Landmarks;
    public int HandCount;
    public Point[]? WorldLandmarks;
}

[Guid("4567890a-bcde-f123-4567-890abcdef123")]
[ExportDependencies("mediapipe_c.dll", "libmediapipe_c.so", "libmediapipe_c.dylib", "libmediapipe_opencl_stub.dylib", "OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib", "Emgu.CV.dll", "cvextern.dll", "OpenGL.Net.dll", "CppSharp.Runtime.dll", "Google.Protobuf.dll")]
public class HandLandmarkDetection : Instance<HandLandmarkDetection>
{
    // MediaPipe resizes to its model input (~224px) internally; a camera frame wider than this only
    // burns downsample/memcpy time before hitting that resize.
    private const int MaxInferenceWidth = 640;
    private const int MaxCachedStagingTextures = 4;
    private const int MaxPooledBuffers = 4;

    private static readonly int MaxDownscaleThreads = Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>Xnnpack worker threads used when the InferenceThreads input keeps its default.
    /// See <see cref="MediaPipeInferenceTuning"/> for why the binding's 0 is a bad default.</summary>
    private const int DefaultInferenceThreads = MediaPipeInferenceTuning.DefaultThreads;

    [Output(Guid = "def01234-5678-90ab-cdef-1234567890ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> OutputTexture = new();

    [Output(Guid = "ef012345-6789-0abc-def1-234567890abc", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> DebugTexture = new();

    [Output(Guid = "f0123456-7890-abcd-ef12-34567890abcd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> HandCount = new();

    [Output(Guid = "01234567-890a-bcde-f012-34567890abcd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Output(Guid = "12345678-90ab-cdef-0123-4567890abcde", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews> WorldLandmarksBuffer = new();

    [Output(Guid = "23456789-0abc-def1-2345-67890abcdef1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews> PointBuffer = new();

    public HandLandmarkDetection()
    {
        OutputTexture.UpdateAction = Update;
        DebugTexture.UpdateAction = Update;
        HandCount.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
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
        var minTrackingConfidence = MinTrackingConfidence.GetValue(context);
        var maxHands = MaxHands.GetValue(context);

        var correctAspectRatio = CorrectAspectRatio.GetValue(context);
        var zScale = ZScale.GetValue(context);
        var constantSize = ConstantSize.GetValue(context);
        var handSize = HandSize.GetValue(context);

        if (!enabled || inputTexture == null || inputTexture.IsDisposed)
        {
            OutputTexture.Value = inputTexture!;
            StopWorker();
            ClearOutputs();
            return;
        }

        OutputTexture.Value = inputTexture;

        // Read back on the worker thread when converting results - changing these never needs a reinit,
        // so they must be assigned on every evaluation, not only when one happens to run.
        _activeConstantSize = constantSize;
        _activeHandSize = handSize;

        var acceleration = Acceleration.GetValue(context);
        var inferenceThreads = InferenceThreads.GetValue(context);
        var parametersChanged = maxHands != _activeMaxHands
                                || acceleration != _activeAcceleration
                                || inferenceThreads != _activeInferenceThreads
                                || Math.Abs(minHandDetectionConfidence - _activeMinHandDetectionConfidence) > 0.001f
                                || Math.Abs(minHandPresenceConfidence - _activeMinHandPresenceConfidence) > 0.001f
                                || Math.Abs(minTrackingConfidence - _activeMinTrackingConfidence) > 0.001f;
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
            InitializeWorker(maxHands, minHandDetectionConfidence, minHandPresenceConfidence, minTrackingConfidence, debug);
        }

        if (_handLandmarker == null)
        {
            ClearOutputs();
            return;
        }

        // Only hand the worker a frame when it is idle - a queued frame would be one inference old
        // by the time it is picked up, and the newer one it could have had is then dropped.
        if (_inputQueue.IsEmpty && !_workerBusy)
        {
            var request = CreateRequestFromTexture(inputTexture);
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

            // Assign every packet, including an empty one. Keeping the last non-empty result
            // latched the outputs onto the last face/hand/pose ever seen instead of reporting
            // that it left the frame - and made the count-zero paths unreachable.
            _currentResult = result;
        }


        if (_currentResult != null)
        {
            UpdateOutputsWithResult(_currentResult, correctAspectRatio, zScale, inputTexture);
            UpdateCount.Value++;

            if (debug)
            {
                using var mat = Texture2DToMat(inputTexture);
                if (!mat.Empty())
                {
                    DrawDebugVisualsFromLandmarks(mat, _currentResult.Landmarks);
                    UpdateDebugTextureFromMat(mat);
                }
            }
        }
        else
        {
            ClearOutputs();
        }
    }

    private void ClearOutputs()
    {
        HandCount.Value = 0;
        _landmarksArray = null;

        if (_currentResult != null)
        {
            ReturnPointArrayToPool(_currentResult.Landmarks);
            ReturnPointArrayToPool(_currentResult.WorldLandmarks);
            _currentResult = null;
        }

        DebugTexture.Value = null!;
        WorldLandmarksBuffer.Value = null!;
        PointBuffer.Value = null!;
    }

    #region MediaPipe Integration
    private HandLandmarker? _handLandmarker;
    private Point[]? _landmarksArray;
    private long _frameTimestamp;
    private readonly object _handLandmarkerLock = new object();
    private readonly object _timestampLock = new object();
    private readonly object _landmarksLock = new object();

    private Task? _processingTask;
    private volatile bool _workerBusy;
    private long _lastInitAttemptTicks;
    private int _initAttempts;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<HandLandmarkDetectionRequest> _inputQueue = new();
    private readonly ConcurrentQueue<HandLandmarkDetectionResultPacket> _outputQueue = new();
    private HandLandmarkDetectionResultPacket? _currentResult;
    private int _latencyLogCounter;

    private int _activeMaxHands = -1;
    private MediaPipeAcceleration? _activeAcceleration;
    private volatile bool _activeConstantSize;
    private volatile float _activeHandSize = 0.15f;
    private int _activeInferenceThreads = -1;
    private float _activeMinHandDetectionConfidence = -1f;
    private float _activeMinHandPresenceConfidence = -1f;
    private float _activeMinTrackingConfidence = -1f;

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
    private void InitializeWorker(int maxHands, float minHandDetectionConfidence, float minHandPresenceConfidence, float minTrackingConfidence, bool debug)
    {
        StopWorker();
        _activeMaxHands = maxHands;
        _activeMinHandDetectionConfidence = minHandDetectionConfidence;
        _activeMinHandPresenceConfidence = minHandPresenceConfidence;
        _activeMinTrackingConfidence = minTrackingConfidence;

        // Registry lookup only - the native landmarker creation runs on the worker below.
        var modelFound = MediaPipeModelResolver.TryResolve("hand_landmarker.task", this, out var fullPath);
        if (!modelFound)
        {
            // Not Debug-gated: a missing asset is the one failure a user has to act on, and the retry
                // backoff keeps this to one message per backoff window.
                Log.Warning($"[HandLandmarkDetection] Model not found: {fullPath}", this);
            return;
        }

        var acceleration = _activeAcceleration ?? MediaPipeAcceleration.Cpu;
        var inferenceThreads = _activeInferenceThreads;

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        _processingTask = Task.Run(() => InitializeAndRunWorker(fullPath, acceleration, inferenceThreads, maxHands,
                                                                minHandDetectionConfidence, minHandPresenceConfidence,
                                                                minTrackingConfidence, token, debug), token);
    }

    private void InitializeAndRunWorker(string fullPath, MediaPipeAcceleration acceleration, int inferenceThreads, int maxHands,
                                        float minHandDetectionConfidence, float minHandPresenceConfidence, float minTrackingConfidence,
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
                maxHands,
                minHandDetectionConfidence,
                minHandPresenceConfidence,
                minTrackingConfidence
            );

            try
            {
                landmarker = HandLandmarker.CreateFromOptions(BuildOptions(delegateCase));
            }
            catch (Exception gpuEx) when (acceleration != MediaPipeAcceleration.Cpu)
            {
                Log.Warning($"[HandLandmarkDetection] GPU acceleration not available ({gpuEx.Message}) - using CPU.", this);
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
            Log.Warning($"[HandLandmarkDetection] Initialization failed: {ex.Message}", this);
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
                // Whatever piled up while the previous inference ran is stale by now - only the newest
                // frame is worth its latency, the rest goes straight back to the pool.
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
                    if (debug) Log.Error($"[HandLandmarkDetection] Worker error: {ex.Message}", this);
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

    private void ProcessFrame(HandLandmarkDetectionRequest request, HandLandmarker landmarker, bool debug)
    {
        var queueLatencyMs = Environment.TickCount64 - request.SentTickCount;
        var inferenceStopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        var result = landmarker.DetectForVideo(image, request.Timestamp);
        inferenceStopwatch.Stop();

        if (debug && ++_latencyLogCounter % 90 == 1)
        {
            Log.Debug($"[HandLandmarkDetection] frame latency {queueLatencyMs + inferenceStopwatch.ElapsedMilliseconds}ms " +
                      $"(queue {queueLatencyMs}ms, inference {inferenceStopwatch.ElapsedMilliseconds}ms)", this);
        }

        if (result.HandLandmarks != null && result.HandLandmarks.Count > 0)
        {
            var landmarks = ConvertHandLandmarkerResultToLandmarks(result, _activeMaxHands);
            var worldLandmarks = ConvertHandLandmarkerResultToWorldLandmarks(result, _activeMaxHands);
            var packet = new HandLandmarkDetectionResultPacket
            {
                Landmarks = landmarks,
                HandCount = landmarks != null ? landmarks.Length / 21 : 0,
                WorldLandmarks = worldLandmarks
            };
            _outputQueue.Enqueue(packet);
        }
        else
        {
            _outputQueue.Enqueue(new HandLandmarkDetectionResultPacket
            {
                Landmarks = null,
                HandCount = 0,
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

    private HandLandmarkDetectionRequest? CreateRequestFromTexture(Texture2D texture)
    {
        var desc = texture.Description;

        // One readback per evaluation frame serves every MediaPipe operator reading this texture: the
        // mapped copy costs ~2 ms at 1080p and barely less at 640 px, so seven operators on one camera
        // would otherwise pay it seven times for byte-identical pixels.
        if (!MediaPipeFrameCache.TryGet(texture, MaxInferenceWidth, 3, out var frame,
                                        out var inferenceWidth, out var inferenceHeight, out var readbackError))
        {
            Log.Debug($"[HandLandmark] Texture readback failed: {readbackError}", this);
            return null;
        }

        // The cached array is shared and read-only for this frame; the worker still gets its own pooled
        // copy because the native Image keeps that pointer for the duration of the inference.
        byte[] buffer = GetBuffer(frame.Length);
        System.Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);

        long ts;
        lock(_timestampLock) { ts = _frameTimestamp; _frameTimestamp += 33; }

        return new HandLandmarkDetectionRequest
        {
            PixelData = buffer,
            Width = inferenceWidth,
            Height = inferenceHeight,
            Timestamp = ts,
            SentTickCount = Environment.TickCount64
        };
    }
    #endregion

    private void UpdateOutputsWithResult(HandLandmarkDetectionResultPacket result, bool correctAspectRatio, float zScale, Texture2D inputTexture)
    {
        if (result.Landmarks == null || result.Landmarks.Length == 0)
        {
            HandCount.Value = 0;
            WorldLandmarksBuffer.Value = null!;
            PointBuffer.Value = null!;
            return;
        }

        HandCount.Value = result.HandCount;
        _landmarksArray = result.Landmarks;

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
        int matWidth = mat.Width;
        int matHeight = mat.Height;

        for (int i = 0; i < landmarks.Length; i++)
        {
            var point = landmarks[i];

            // The stored landmarks are MediaPipe's normalized image coordinates re-centered to ±0.5
            // (see ConvertHandLandmarkerResultToLandmarks) - map them straight back to pixels. Going
            // through the processed-output inverse (X/2 + 0.5) here squeezed the points to half scale
            // around the image center.
            int px = (int)((point.Position.X + 0.5f) * matWidth);
            int py = (int)((0.5f - point.Position.Y) * matHeight);

            Cv2.Circle(mat, px, py, 2, landmarkColor, -1, LineTypes.AntiAlias);
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

    private Point[]? ConvertHandLandmarkerResultToLandmarks(HandLandmarkerResult result, int maxHands)
    {
        if (result.HandLandmarks == null) return null;

        var landmarks = new List<Point>();

        try
        {
            for (int handIndex = 0; handIndex < result.HandLandmarks.Count && handIndex < maxHands; handIndex++)
            {
                var handLandmarks = result.HandLandmarks[handIndex];

                if (handLandmarks.landmarks != null && handLandmarks.landmarks.Count > 0)
                {
                    // Normalized image coordinates shrink as the hand moves away from the camera
                    // (plain perspective). When ConstantSize is on, rescale each hand's landmark cloud
                    // around its wrist so the wrist->middle-knuckle distance is always HandSize - the
                    // hand keeps its position in the frame but no longer changes size with distance.
                    // Z is depth relative to the wrist in the same scale, so it is scaled by the same
                    // factor and stays consistent.
                    var wristX = handLandmarks.landmarks[0].X - 0.5f;
                    var wristY = 0.5f - handLandmarks.landmarks[0].Y;
                    var sizeScale = 1f;
                    if (_activeConstantSize && handLandmarks.landmarks.Count > 9)
                    {
                        var knuckleX = handLandmarks.landmarks[9].X - 0.5f;
                        var knuckleY = 0.5f - handLandmarks.landmarks[9].Y;
                        var apparentSize = MathF.Sqrt((knuckleX - wristX) * (knuckleX - wristX)
                                                      + (knuckleY - wristY) * (knuckleY - wristY));
                        if (apparentSize > 1e-5f)
                            sizeScale = _activeHandSize / apparentSize;
                    }

                    for (int i = 0; i < handLandmarks.landmarks.Count && i < 21; i++)
                    {
                        var landmark = handLandmarks.landmarks[i];

                        var centeredX = (landmark.X - 0.5f - wristX) * sizeScale + wristX;
                        var centeredY = (0.5f - landmark.Y - wristY) * sizeScale + wristY;
                        var z = -landmark.Z * sizeScale; // Flip Z to match FaceLandmarkDetection

                        landmarks.Add(new Point
                        {
                            Position = new Vector3(centeredX, centeredY, z),
                            F1 = i,
                            F2 = landmark.Visibility ?? 0f,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        });
                    }
                }
            }

            return landmarks.Count > 0 ? landmarks.ToArray() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Point[]? ConvertHandLandmarkerResultToWorldLandmarks(HandLandmarkerResult result, int maxHands)
    {
        if (result.HandWorldLandmarks == null!) return null;

        int totalLandmarks = 0;
        for (int handIndex = 0; handIndex < result.HandWorldLandmarks.Count && handIndex < maxHands; handIndex++)
        {
            var handLandmarks = result.HandWorldLandmarks[handIndex];
            if (handLandmarks.landmarks != null! && handLandmarks.landmarks.Count > 0)
            {
                totalLandmarks += Math.Min(handLandmarks.landmarks.Count, 21);
            }
        }

        if (totalLandmarks == 0) return null;

        var landmarks = GetPointArray(totalLandmarks);
        int landmarkIndex = 0;

        try
        {
            for (int handIndex = 0; handIndex < result.HandWorldLandmarks.Count && handIndex < maxHands; handIndex++)
            {
                var handLandmarks = result.HandWorldLandmarks[handIndex];

                if (handLandmarks.landmarks != null && handLandmarks.landmarks.Count > 0)
                {
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
            }

            // Return the rented array itself: cloning it dropped every rental (the clone went back to the
            // pool instead), so the pool never hit and a fresh array was allocated per frame.
            return landmarks.Length > 0 ? landmarks : null;
        }
        catch (Exception)
        {
            ReturnPointArrayToPool(landmarks);
            return null;
        }
    }

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

        _debugTexture?.Dispose();
        _debugTexture = null;

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

        if (_currentResult != null)
        {
            ReturnPointArrayToPool(_currentResult.Landmarks);
            ReturnPointArrayToPool(_currentResult.WorldLandmarks);
            _currentResult = null;
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
    #endregion

    #region Input Parameters
    [Input(Guid = "567890ab-cdef-1234-5678-90abcdef1234")]
    public readonly InputSlot<Texture2D> InputTexture = new();

    [Input(Guid = "67890abc-def1-2345-6789-0abcdef12345")]
    public readonly InputSlot<bool> Enabled = new(true);

    [Input(Guid = "7890abcd-ef12-3456-7890-abcdef123456")]
    public readonly InputSlot<int> MaxHands = new(2);

    [Input(Guid = "890abcde-f123-4567-890a-bcdef1234567")]
    public readonly InputSlot<float> MinHandDetectionConfidence = new(0.5f);

    [Input(Guid = "90abcdef-1234-5678-90ab-cdef12345678")]
    public readonly InputSlot<float> MinHandPresenceConfidence = new(0.5f);

    [Input(Guid = "0abcdef1-2345-6789-0abc-def123456789")]
    public readonly InputSlot<float> MinTrackingConfidence = new(0.5f);

    [Input(Guid = "abcdef12-3456-7890-abcd-ef1234567890")]
    public readonly InputSlot<bool> Debug = new(false);

    [Input(Guid = "bcdef123-4567-890a-bcde-f1234567890a")]
    public readonly InputSlot<bool> CorrectAspectRatio = new(false);

    /// <summary>Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere),
    /// Xnnpack = multithreaded CPU (the fast default; see InferenceThreads), Gpu = OpenGL/EGL
    /// delegate (Linux native has it; the CPU-only Windows/macOS natives fall back to Cpu).</summary>
    [Input(Guid = "a1b2c3d4-0001-4a9b-8c2d-101010101010")]
    public readonly InputSlot<MediaPipeAcceleration> Acceleration = new(MediaPipeAcceleration.Xnnpack);

    /// <summary>Worker threads for the Xnnpack acceleration (0 = library default, which is effectively
        /// single-threaded). 4 is ~2x faster than the default on a 20-core machine; use 2 when
        /// several MediaPipe operators run at once. Ignored by Cpu and Gpu.</summary>
    [Input(Guid = "a1b2c3d4-1001-4a9b-8c2d-101010101010")]
    public readonly InputSlot<int> InferenceThreads = new(DefaultInferenceThreads);

    /// <summary>Rescales each hand's landmark cloud around its wrist so its size no longer depends
    /// on the distance to the camera.</summary>
    [Input(Guid = "a1b2c3d4-2001-4a9b-8c2d-101010101010")]
    public readonly InputSlot<bool> ConstantSize = new(false);

    /// <summary>Target wrist-to-middle-knuckle distance (in centered, +/-0.5 image units) when
    /// ConstantSize is on.</summary>
    [Input(Guid = "a1b2c3d4-2002-4a9b-8c2d-101010101010")]
    public readonly InputSlot<float> HandSize = new(0.15f);

    [Input(Guid = "cdef1234-5678-90ab-cdef-1234567890ab")]
    public readonly InputSlot<float> ZScale = new(1.0f);
    #endregion

    #region Texture Conversion (Minimal OpenCV Usage)
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
        Log.Warning($"[HandLandmark] Debug readback failed: {ex.Message}", this);
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
        Log.Warning($"[HandLandmark] Debug readback: row pitch {dataBox.RowPitch} < {rowBytes}.", this);
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
    #endregion
}

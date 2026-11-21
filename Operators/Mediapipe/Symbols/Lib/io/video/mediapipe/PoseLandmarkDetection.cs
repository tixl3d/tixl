using System.Threading;
using OpenCvSharp;
using T3.Graphics.Compat;
using Mediapipe;

#nullable enable
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Framework.Formats;
using T3.Core.Resource.Assets;
using Image = Mediapipe.Framework.Formats.Image;
// ReSharper disable EmptyGeneralCatchClause

namespace Lib.io.video.mediapipe;

internal class PoseLandmarkDetectionRequest
{
    public byte[]? PixelData;
    public int Width;
    public int Height;
    public long Timestamp;
    public long SentTickCount;
}

internal class PoseLandmarkDetectionResultPacket
{
    public Point[]? Landmarks;
    public int PoseCount;
    public Point[]? WorldLandmarks;
    public byte[]? SegmentationMask;
    public int SegmentationMaskWidth;
    public int SegmentationMaskHeight;
}

[Guid("34567890-abcd-ef12-3456-7890abcdef12")]
[ExportDependencies("mediapipe_c.dll", "libmediapipe_c.so", "libmediapipe_c.dylib", "libmediapipe_opencl_stub.dylib", "OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib", "Emgu.CV.dll", "cvextern.dll", "OpenGL.Net.dll", "CppSharp.Runtime.dll", "Google.Protobuf.dll")]
public class PoseLandmarkDetection : Instance<PoseLandmarkDetection>
{
    // MediaPipe resizes to its model input (~256px) internally; a camera frame wider than this only
    // burns downsample/memcpy time before hitting that resize.
    private const int MaxInferenceWidth = 640;
    private const int MaxCachedStagingTextures = 4;
    private const int MaxPooledBuffers = 4;

    private static readonly int MaxDownscaleThreads = Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>Xnnpack worker threads used when the InferenceThreads input keeps its default.
    /// See <see cref="MediaPipeInferenceTuning"/> for why the binding's 0 is a bad default.</summary>
    private const int DefaultInferenceThreads = MediaPipeInferenceTuning.DefaultThreads;

    [Output(Guid = "cdef1234-5678-90ab-cdef-1234567890ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> OutputTexture = new();

    [Output(Guid = "def01234-5678-90ab-cdef-1234567890ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> DebugTexture = new();

    [Output(Guid = "ef012345-6789-0abc-def1-234567890abc", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> PoseCount = new();

    [Output(Guid = "f0123456-7890-abcd-ef12-34567890abcd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Output(Guid = "01234567-890a-bcde-f012-34567890abcd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> WorldLandmarksBuffer = new();

    [Output(Guid = "12345678-90ab-cdef-0123-4567890abcde", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> PointBuffer = new();

    [Output(Guid = "23456789-0abc-def1-2345-67890abcdef1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> SegmentationMask = new();

    public PoseLandmarkDetection()
    {
        OutputTexture.UpdateAction = Update;
        DebugTexture.UpdateAction = Update;
        PoseCount.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        WorldLandmarksBuffer.UpdateAction = Update;
        PointBuffer.UpdateAction = Update;
        SegmentationMask.UpdateAction = Update;
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

        var minPoseDetectionConfidence = MinPoseDetectionConfidence.GetValue(context);
        var minPosePresenceConfidence = MinPosePresenceConfidence.GetValue(context);
        var minTrackingConfidence = MinTrackingConfidence.GetValue(context);
        var maxPoses = MaxPoses.GetValue(context);

        var correctAspectRatio = CorrectAspectRatio.GetValue(context);
        var zScale = ZScale.GetValue(context);

        if (!enabled || inputTexture == null || inputTexture.IsDisposed)
        {
            OutputTexture.Value = inputTexture;
            StopWorker();
            ClearOutputs();
            return;
        }

        OutputTexture.Value = inputTexture;

        var acceleration = Acceleration.GetValue(context);
        var inferenceThreads = InferenceThreads.GetValue(context);
        var parametersChanged = maxPoses != _activeMaxPoses
                                || acceleration != _activeAcceleration
                                || inferenceThreads != _activeInferenceThreads
                                || Math.Abs(minPoseDetectionConfidence - _activeMinPoseDetectionConfidence) > 0.001f
                                || Math.Abs(minPosePresenceConfidence - _activeMinPosePresenceConfidence) > 0.001f
                                || Math.Abs(minTrackingConfidence - _activeMinTrackingConfidence) > 0.001f;
        if (parametersChanged)
            _initAttempts = 0;

        var initializing = _poseLandmarker is null && _processingTask is { IsCompleted: false };
        var initNeeded = !initializing && (_processingTask == null || _processingTask.IsCompleted || _poseLandmarker is null || parametersChanged);
        var retryDelayMs = MediaPipeInitRetry.BackoffMs(_initAttempts);
        var retryBlocked = _poseLandmarker == null && _initAttempts > 0
                           && Environment.TickCount64 - _lastInitAttemptTicks < retryDelayMs;

        if (initNeeded && !retryBlocked)
        {
            _activeAcceleration = acceleration;
            _activeInferenceThreads = inferenceThreads;
            _lastInitAttemptTicks = Environment.TickCount64;
            _initAttempts++;
            InitializeWorker(maxPoses, minPoseDetectionConfidence, minPosePresenceConfidence, minTrackingConfidence, debug);
        }

        if (_poseLandmarker == null)
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
        PoseCount.Value = 0;
        _landmarksArray = null;

        if (_currentResult != null)
        {
            ReturnPointArrayToPool(_currentResult.Landmarks);
            ReturnPointArrayToPool(_currentResult.WorldLandmarks);
            _currentResult = null;
        }

        DebugTexture.Value = null;
        WorldLandmarksBuffer.Value = null;
        PointBuffer.Value = null;
        SegmentationMask.Value = null;
    }

    #region MediaPipe Integration
    private PoseLandmarker? _poseLandmarker;
    private Point[]? _landmarksArray;
    private long _frameTimestamp;
    private readonly object _poseLandmarkerLock = new object();
    private readonly object _timestampLock = new object();

    private Task? _processingTask;
    private volatile bool _workerBusy;
    private long _lastInitAttemptTicks;
    private int _initAttempts;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<PoseLandmarkDetectionRequest> _inputQueue = new();
    private readonly ConcurrentQueue<PoseLandmarkDetectionResultPacket> _outputQueue = new();
    private PoseLandmarkDetectionResultPacket? _currentResult;
    private int _latencyLogCounter;

    private int _activeMaxPoses = -1;
    private MediaPipeAcceleration? _activeAcceleration;
    private int _activeInferenceThreads = -1;
    private float _activeMinPoseDetectionConfidence = -1f;
    private float _activeMinPosePresenceConfidence = -1f;
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
    private void InitializeWorker(int maxPoses, float minPoseDetectionConfidence, float minPosePresenceConfidence, float minTrackingConfidence, bool debug)
    {
        StopWorker();
        _activeMaxPoses = maxPoses;
        _activeMinPoseDetectionConfidence = minPoseDetectionConfidence;
        _activeMinPosePresenceConfidence = minPosePresenceConfidence;
        _activeMinTrackingConfidence = minTrackingConfidence;

        // Resolving the asset is a registry lookup. Creating the native landmarker (model load, GPU
        // delegate init) takes hundreds of milliseconds to seconds, so it runs on the worker: the eval
        // thread must never wait for a native model load.
        var modelFound = MediaPipeModelResolver.TryResolve("pose_landmarker.task", this, out var fullPath);
        if (!modelFound)
        {
            // Not Debug-gated: a missing asset is the one failure a user has to act on, and the retry
                // backoff keeps this to one message per backoff window.
                Log.Warning($"[PoseLandmarkDetection] Model not found: {fullPath}", this);
            return;
        }

        var acceleration = _activeAcceleration ?? MediaPipeAcceleration.Cpu;
        var inferenceThreads = _activeInferenceThreads;

        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        _processingTask = Task.Run(() => InitializeAndRunWorker(fullPath, acceleration, inferenceThreads, maxPoses,
                                                                minPoseDetectionConfidence, minPosePresenceConfidence,
                                                                minTrackingConfidence, token, debug), token);
    }

    private void InitializeAndRunWorker(string fullPath, MediaPipeAcceleration acceleration, int inferenceThreads,
                                        int maxPoses, float minPoseDetectionConfidence, float minPosePresenceConfidence,
                                        float minTrackingConfidence, CancellationToken token, bool debug)
    {
        PoseLandmarker? landmarker = null;
        try
        {
            var delegateCase = acceleration == MediaPipeAcceleration.Xnnpack
                                   ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.Xnnpack
                                   : acceleration == MediaPipeAcceleration.Gpu
                                       ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.GPU
                                       : Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU;

            PoseLandmarkerOptions BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate selected) => new(
                new Mediapipe.Tasks.Core.CoreBaseOptions(modelAssetPath: fullPath, delegateCase: selected, numThreads: inferenceThreads),
                VisionRunningMode.VIDEO,
                maxPoses,
                minPoseDetectionConfidence,
                minPosePresenceConfidence,
                minTrackingConfidence,
                outputSegmentationMasks: true);

            try
            {
                landmarker = PoseLandmarker.CreateFromOptions(BuildOptions(delegateCase));
            }
            catch (Exception gpuEx) when (acceleration != MediaPipeAcceleration.Cpu)
            {
                Log.Warning($"[PoseLandmarkDetection] GPU acceleration not available ({gpuEx.Message}) - using CPU.", this);
                landmarker = PoseLandmarker.CreateFromOptions(BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU));
            }

            if (landmarker == null)
                return;

            var published = false;
            lock (_poseLandmarkerLock)
            {
                // StopWorker may have run while the model was loading. Publishing now would leak a
                // landmarker it will never close, so it is closed below and the next generation starts.
                if (!token.IsCancellationRequested)
                {
                    _poseLandmarker = landmarker;
                    published = true;
                }
            }

            if (!published)
            {
                // Closed outside the lock: StopWorker takes the same lock from the evaluation thread,
                // and a slow native close must not stall a frame.
                MediaPipeWorkerReaper.CloseNative(landmarker);
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
            Log.Warning($"[PoseLandmarkDetection] Initialization failed: {ex.Message}", this);

            if (landmarker != null)
            {
                try
                {
                    MediaPipeWorkerReaper.CloseNative(landmarker);
                }
                catch (Exception closeEx)
                {
                    if (debug) Log.Error($"[PoseLandmarkDetection] Error closing PoseLandmarker: {closeEx.Message}", this);
                }

                lock (_poseLandmarkerLock)
                {
                    if (ReferenceEquals(_poseLandmarker, landmarker))
                        _poseLandmarker = null;
                }
            }
        }
    }

    private void WorkerLoop(CancellationToken token, PoseLandmarker landmarker, bool debug)
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
                    if (debug) Log.Error($"[PoseLandmarkDetection] Worker error: {ex.Message}", this);
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

    private void ProcessFrame(PoseLandmarkDetectionRequest request, PoseLandmarker landmarker, bool debug)
    {
        var queueLatencyMs = Environment.TickCount64 - request.SentTickCount;
        var inferenceStopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var image = new Image(ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        var result = landmarker.DetectForVideo(image, request.Timestamp);
        inferenceStopwatch.Stop();

        if (debug && ++_latencyLogCounter % 90 == 1)
        {
            Log.Debug($"[PoseLandmarkDetection] frame latency {queueLatencyMs + inferenceStopwatch.ElapsedMilliseconds}ms " +
                      $"(queue {queueLatencyMs}ms, inference {inferenceStopwatch.ElapsedMilliseconds}ms)", this);
        }

        if (result.PoseLandmarks != null && result.PoseLandmarks.Count > 0)
        {
            var landmarks = ConvertPoseLandmarkerResultToLandmarks(result, _activeMaxPoses);
            var worldLandmarks = ConvertPoseLandmarkerResultToWorldLandmarks(result, _activeMaxPoses);
            var segmentationMask = ConvertSegmentationMask(result, out var maskWidth, out var maskHeight);

            var packet = new PoseLandmarkDetectionResultPacket
            {
                Landmarks = landmarks,
                PoseCount = landmarks != null ? landmarks.Length / 33 : 0,
                WorldLandmarks = worldLandmarks,
                SegmentationMask = segmentationMask,
                SegmentationMaskWidth = maskWidth,
                SegmentationMaskHeight = maskHeight
            };
            _outputQueue.Enqueue(packet);
        }
        else
        {
            _outputQueue.Enqueue(new PoseLandmarkDetectionResultPacket
            {
                Landmarks = null,
                PoseCount = 0,
                WorldLandmarks = null,
                SegmentationMask = null
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

            PoseLandmarker? nativeTask;
            lock (_poseLandmarkerLock)
            {
                nativeTask = _poseLandmarker;
                _poseLandmarker = null;
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

    private PoseLandmarkDetectionRequest? CreateRequestFromTexture(Texture2D texture)
    {
        var desc = texture.Description;

        // One readback per evaluation frame serves every MediaPipe operator reading this texture: the
        // mapped copy costs ~2 ms at 1080p and barely less at 640 px, so seven operators on one camera
        // would otherwise pay it seven times for byte-identical pixels.
        if (!MediaPipeFrameCache.TryGet(texture, MaxInferenceWidth, 3, out var frame,
                                        out var inferenceWidth, out var inferenceHeight, out var readbackError))
        {
            Log.Debug($"[PoseLandmark] Texture readback failed: {readbackError}", this);
            return null;
        }

        // The cached array is shared and read-only for this frame; the worker still gets its own pooled
        // copy because the native Image keeps that pointer for the duration of the inference.
        byte[] buffer = GetBuffer(frame.Length);
        System.Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);

        long ts;
        lock(_timestampLock) { ts = _frameTimestamp; _frameTimestamp += 33; }

        return new PoseLandmarkDetectionRequest
        {
            PixelData = buffer,
            Width = inferenceWidth,
            Height = inferenceHeight,
            Timestamp = ts,
            SentTickCount = Environment.TickCount64
        };
    }
    #endregion

    private void UpdateOutputsWithResult(PoseLandmarkDetectionResultPacket result, bool correctAspectRatio, float zScale, Texture2D inputTexture)
    {
        if (result.Landmarks == null || result.Landmarks.Length == 0)
        {
            PoseCount.Value = 0;
            WorldLandmarksBuffer.Value = null;
            PointBuffer.Value = null;
            SegmentationMask.Value = null;
            return;
        }

        PoseCount.Value = result.PoseCount;
        _landmarksArray = result.Landmarks;

        float aspectRatio = 1.0f;
        if (correctAspectRatio && inputTexture != null)
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

        UpdateBuffer(result.WorldLandmarks, ref _worldLandmarksBufferWithViews, WorldLandmarksBuffer!);
        UpdateBuffer(processedLandmarks, ref _pointBufferWithViews, PointBuffer!);

        if (result.SegmentationMask != null)
        {
            UpdateSegmentationMaskTexture(result.SegmentationMask, result.SegmentationMaskWidth, result.SegmentationMaskHeight);
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
        int matWidth = mat.Width;
        int matHeight = mat.Height;

        for (int i = 0; i < landmarks.Length; i++)
        {
            var point = landmarks[i];

            // The stored landmarks are MediaPipe's normalized image coordinates re-centered to ±0.5
            // (see the landmark conversion) - map them straight back to pixels. Going through the
            // processed-output inverse (X/2 + 0.5) here squeezed the points to half scale around
            // the image center.
            int px = (int)((point.Position.X + 0.5f) * matWidth);
            int py = (int)((0.5f - point.Position.Y) * matHeight);

            Cv2.Circle(mat, px, py, 2, landmarkColor, -1, LineTypes.AntiAlias);
        }
    }

    private Texture2D? _debugTexture;
    private Texture2D? _segmentationMaskTexture;

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
        Log.Warning($"[PoseLandmark] Debug readback failed: {ex.Message}", this);
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
        Log.Warning($"[PoseLandmark] Debug readback: row pitch {dataBox.RowPitch} < {rowBytes}.", this);
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

    private Point[]? ConvertPoseLandmarkerResultToLandmarks(PoseLandmarkerResult result, int maxPoses)
    {
        if (result.PoseLandmarks == null) return null;

        int totalLandmarks = 0;
        for (int poseIndex = 0; poseIndex < result.PoseLandmarks.Count && poseIndex < maxPoses; poseIndex++)
        {
            var poseLandmarks = result.PoseLandmarks[poseIndex];
            if (poseLandmarks.landmarks != null && poseLandmarks.landmarks.Count > 0)
            {
                totalLandmarks += Math.Min(poseLandmarks.landmarks.Count, 33);
            }
        }

        if (totalLandmarks == 0) return null;

        var landmarks = GetPointArray(totalLandmarks);
        int landmarkIndex = 0;

        try
        {
            for (int poseIndex = 0; poseIndex < result.PoseLandmarks.Count && poseIndex < maxPoses; poseIndex++)
            {
                var poseLandmarks = result.PoseLandmarks[poseIndex];

                if (poseLandmarks.landmarks != null && poseLandmarks.landmarks.Count > 0)
                {
                    for (int i = 0; i < poseLandmarks.landmarks.Count && i < 33; i++)
                    {
                        var landmark = poseLandmarks.landmarks[i];

                        var centeredX = landmark.X - 0.5f;
                        var centeredY = 0.5f - landmark.Y;
                        var z = -landmark.Z; // Flip Z

                        landmarks[landmarkIndex++] = new Point
                        {
                            Position = new Vector3(centeredX, centeredY, z),
                            F1 = i,
                            F2 = landmark.Visibility ?? 0f,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                    }
                }
            }

            return landmarks;
        }
        catch (Exception)
        {
            ReturnPointArrayToPool(landmarks);
            return null;
        }
    }

    private Point[]? ConvertPoseLandmarkerResultToWorldLandmarks(PoseLandmarkerResult result, int maxPoses)
    {
        if (result.PoseWorldLandmarks == null!) return null;

        int totalLandmarks = 0;
        for (int poseIndex = 0; poseIndex < result.PoseWorldLandmarks.Count && poseIndex < maxPoses; poseIndex++)
        {
            var poseLandmarks = result.PoseWorldLandmarks[poseIndex];
            if (poseLandmarks.landmarks != null! && poseLandmarks.landmarks.Count > 0)
            {
                totalLandmarks += Math.Min(poseLandmarks.landmarks.Count, 33);
            }
        }

        if (totalLandmarks == 0) return null;

        var landmarks = GetPointArray(totalLandmarks);
        int landmarkIndex = 0;

        try
        {
            for (int poseIndex = 0; poseIndex < result.PoseWorldLandmarks.Count && poseIndex < maxPoses; poseIndex++)
            {
                var poseLandmarks = result.PoseWorldLandmarks[poseIndex];

                if (poseLandmarks.landmarks != null && poseLandmarks.landmarks.Count > 0)
                {
                    for (int i = 0; i < poseLandmarks.landmarks.Count && i < 33; i++)
                    {
                        var landmark = poseLandmarks.landmarks[i];

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

            return landmarks;
        }
        catch (Exception)
        {
            ReturnPointArrayToPool(landmarks);
            return null;
        }
    }

    private byte[]? ConvertSegmentationMask(PoseLandmarkerResult result, out int maskWidth, out int maskHeight)
    {
        maskWidth = 0;
        maskHeight = 0;
        if (result.SegmentationMasks == null || result.SegmentationMasks.Count == 0) return null;

        try
        {
            var mask = result.SegmentationMasks[0];
            int width = mask.Width();
            int height = mask.Height();
            if (width <= 0 || height <= 0) return null;

            mask.ConvertToCpu();
            using (var pixelLock = new PixelWriteLock(mask))
            {
                nint pixelsPtr = pixelLock.Pixels();
                if (pixelsPtr == 0) return null;

                // Confidence masks are one float per pixel; compress to a single 8-bit channel.
                int byteCount = width * height * 4;
                var floatBytes = new byte[byteCount];
                Marshal.Copy(pixelsPtr, floatBytes, 0, byteCount);

                var maskData = new byte[width * height];
                for (int i = 0; i < width * height; i++)
                {
                    float val = BitConverter.ToSingle(floatBytes, i * 4);
                    if (float.IsNaN(val)) val = 0f;
                    maskData[i] = (byte)(Math.Clamp(val, 0f, 1f) * 255f);
                }

                maskWidth = width;
                maskHeight = height;
                return maskData;
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            // The landmarker allocates these per inference and the result owns them - not
            // disposing them leaks ~1MB of native memory per frame.
            foreach (var mask in result.SegmentationMasks)
            {
                mask?.Dispose();
            }
        }
    }

    private void UpdateSegmentationMaskTexture(byte[] maskData, int width, int height)
    {
        if (maskData == null! || maskData.Length == 0 || width <= 0 || height <= 0)
        {
            SegmentationMask.Value = null;
            return;
        }

        if (_segmentationMaskTexture == null || _segmentationMaskTexture.Description.Width != width || _segmentationMaskTexture.Description.Height != height)
        {
            _segmentationMaskTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = T3.Graphics.Format.R8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _segmentationMaskTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, desc));
        }

        var handle = System.Runtime.InteropServices.GCHandle.Alloc(maskData, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var dataBox = new DataBox(handle.AddrOfPinnedObject(), width, 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _segmentationMaskTexture);
        }
        finally
        {
            handle.Free();
        }

        SegmentationMask.Value = _segmentationMaskTexture;
    }

    #region Cleanup
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        // Stops the worker task and hands the landmarker to its deferred closer - closing it here
        // directly could hit a frame still running on the worker thread.
        _disposed = true;
        StopWorker();

        _debugTexture?.Dispose();
        _debugTexture = null;
        _segmentationMaskTexture?.Dispose();
        _segmentationMaskTexture = null;

        // The reaper owns the native instance now; give a running inference a moment to
        // return, but never hang shutdown on a wedged native library.
        _reaper.WaitForPending(2000);

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

        if (_currentResult != null)
        {
            ReturnPointArrayToPool(_currentResult.Landmarks);
            ReturnPointArrayToPool(_currentResult.WorldLandmarks);
            _currentResult = null;
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
    [Input(Guid = "4567890a-bcde-f123-4567-890abcdef123")]
    public readonly InputSlot<Texture2D> InputTexture = new();

    [Input(Guid = "567890ab-cdef-1234-5678-90abcdef1234")]
    public readonly InputSlot<bool> Enabled = new(true);

    [Input(Guid = "67890abc-def1-2345-6789-0abcdef12345")]
    public readonly InputSlot<int> MaxPoses = new(1);

    [Input(Guid = "7890abcd-ef12-3456-7890-abcdef123456")]
    public readonly InputSlot<float> MinPoseDetectionConfidence = new(0.5f);

    [Input(Guid = "890abcde-f123-4567-890a-bcdef1234567")]
    public readonly InputSlot<float> MinPosePresenceConfidence = new(0.5f);

    [Input(Guid = "90abcdef-1234-5678-90ab-cdef12345678")]
    public readonly InputSlot<float> MinTrackingConfidence = new(0.5f);

    [Input(Guid = "0abcdef1-2345-6789-0abc-def123456789")]
    public readonly InputSlot<bool> Debug = new(false);

    [Input(Guid = "abcdef12-3456-7890-abcd-ef1234567890")]
    public readonly InputSlot<bool> CorrectAspectRatio = new(false);

    /// <summary>Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere),
    /// Xnnpack = multithreaded CPU (the fast default; see InferenceThreads), Gpu = OpenGL/EGL
    /// delegate (Linux native has it; the CPU-only Windows/macOS natives fall back to Cpu).</summary>
    [Input(Guid = "a1b2c3d4-0007-4a9b-8c2d-101010101010")]
    public readonly InputSlot<MediaPipeAcceleration> Acceleration = new(MediaPipeAcceleration.Xnnpack);

    /// <summary>Worker threads for the Xnnpack acceleration (0 = library default, which is effectively
        /// single-threaded). 4 is ~2x faster than the default on a 20-core machine; use 2 when
        /// several MediaPipe operators run at once. Ignored by Cpu and Gpu.</summary>
    [Input(Guid = "a1b2c3d4-1007-4a9b-8c2d-101010101010")]
    public readonly InputSlot<int> InferenceThreads = new(DefaultInferenceThreads);

    [Input(Guid = "bcdef123-4567-890a-bcde-f1234567890a")]
    public readonly InputSlot<float> ZScale = new(1.0f);
}
#endregion Input Parameters

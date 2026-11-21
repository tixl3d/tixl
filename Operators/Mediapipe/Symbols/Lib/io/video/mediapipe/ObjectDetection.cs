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
using Mediapipe.Tasks.Vision.ObjectDetector;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Framework.Formats;
using T3.Core.Resource.Assets;
using Image = Mediapipe.Framework.Formats.Image;

namespace Lib.io.video.mediapipe;

internal class ObjectDetectionRequest
{
    public byte[]? PixelData;
    public int Width;
    public int Height;
    public long Timestamp;
    public float MinDetectionConfidence;
    public int MaxObjects;
}

internal class ObjectDetectionResultPacket
{
    public Detection[]? Detections;
    public int ObjectCount;
    public int ImageWidth;
    public int ImageHeight;
}

[Guid("12345678-90ab-cdef-1234-567890abcdef")]
[ExportDependencies("mediapipe_c.dll", "libmediapipe_c.so", "libmediapipe_c.dylib", "libmediapipe_opencl_stub.dylib", "OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib", "Emgu.CV.dll", "cvextern.dll", "OpenGL.Net.dll", "CppSharp.Runtime.dll", "Google.Protobuf.dll")]
public class ObjectDetection : Instance<ObjectDetection>
{
    private const int MaxInferenceWidth = 640;
    private const int MaxCachedStagingTextures = 4;
    private const int MaxPooledBuffers = 4;

    private static readonly int MaxDownscaleThreads = Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>Xnnpack worker threads used when the InferenceThreads input keeps its default.
    /// See <see cref="MediaPipeInferenceTuning"/> for why the binding's 0 is a bad default.</summary>
    private const int DefaultInferenceThreads = MediaPipeInferenceTuning.DefaultThreads;

    [Output(Guid = "bcdef123-4567-890a-bcde-f1234567890a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> OutputTexture = new();

    [Output(Guid = "cdef1234-5678-90ab-cdef-1234567890ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> DebugTexture = new();

    [Output(Guid = "def01234-5678-90ab-cdef-1234567890ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> ObjectCount = new();

    [Output(Guid = "ef012345-6789-0abc-def1-234567890abc", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Output(Guid = "f0123456-7890-abcd-ef12-34567890abcd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> PointBuffer = new();

    [Output(Guid = "01234567-890a-bcde-f012-34567890abcd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Dict<float>> ObjectData = new();

    [Output(Guid = "12345678-90ab-cdef-0123-4567890abcde", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<List<string>> ActiveCategories = new();

    public ObjectDetection()
    {
        OutputTexture.UpdateAction = Update;
        DebugTexture.UpdateAction = Update;
        ObjectCount.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        PointBuffer.UpdateAction = Update;
        ObjectData.UpdateAction = Update;
        ActiveCategories.UpdateAction = Update;
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
        var minDetectionConfidence = MinDetectionConfidence.GetValue(context);
        var maxObjects = MaxObjects.GetValue(context);
        var model = (DetectionModel)Model.GetValue(context);
        var categoryAllowlist = CategoryAllowlist.GetValue(context);
        var categoryDenylist = CategoryDenylist.GetValue(context);
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

        // A failed init (missing model, missing native, rejected delegate, an allowlist the model
        // rejects) must not rebuild the worker every frame: wait between attempts, backing off up to
        // 30 s, and retry immediately when the configuration itself changed. `initializing` tells a
        // first attempt that is still running apart from one that never started.
        var parametersChanged = maxObjects != _activeMaxObjects
                                || Math.Abs(minDetectionConfidence - _activeMinDetectionConfidence) > 0.001f
                                || model != _activeModel
                                || categoryAllowlist != _activeCategoryAllowlist
                                || categoryDenylist != _activeCategoryDenylist
                                || Acceleration.GetValue(context) != _activeAcceleration
                                || InferenceThreads.GetValue(context) != _activeInferenceThreads;
        if (parametersChanged)
            _initAttempts = 0;

        var initializing = _objectDetector == null && _processingTask is { IsCompleted: false };
        var retryDelayMs = MediaPipeInitRetry.BackoffMs(_initAttempts);
        var retryBlocked = _objectDetector == null && _initAttempts > 0
                           && Environment.TickCount64 - _lastInitAttemptTicks < retryDelayMs;

        if (!initializing && !retryBlocked
            && (_processingTask == null || _processingTask.IsCompleted || _objectDetector == null || parametersChanged))
        {
            _activeAcceleration = Acceleration.GetValue(context);
            _activeInferenceThreads = InferenceThreads.GetValue(context);
            _lastInitAttemptTicks = Environment.TickCount64;
            _initAttempts++;
            InitializeWorker(maxObjects, minDetectionConfidence, debug, model, categoryAllowlist!, categoryDenylist!);
        }

        if (_objectDetector == null)
        {
            ClearOutputs();
            return;
        }

        if (_inputQueue.IsEmpty && !_workerBusy)
        {
            var request = CreateRequestFromTexture(inputTexture, minDetectionConfidence, maxObjects);
            if (request != null)
            {
                _inputQueue.Enqueue(request);
            }
        }

        while (_outputQueue.TryDequeue(out var result))
        {
            if (_currentResult != null)
            {
                ReturnDetectionArrayToPool(_currentResult.Detections);
            }

            // Assign every packet, including an empty one. Keeping the last non-empty result
            // latched the outputs onto the last objects ever seen instead of reporting that they
            // left the frame - and made the count-zero paths unreachable.
            _currentResult = result;
        }


        if (_currentResult != null)
        {
            UpdateOutputsWithResult(_currentResult, correctAspectRatio, zScale);

            if (debug)
            {
                using var mat = Texture2DToMat(inputTexture);
                if (!mat.Empty())
                {
                    DrawDebugVisualsFromDetections(mat, _currentResult.Detections);
                    UpdateDebugTextureFromMat(mat);
                }
            }
        }
        else
        {
            ObjectCount.Value = 0;
            DebugTexture.Value = null;
            PointBuffer.Value = null;
            ObjectData.Value = null!;
            ActiveCategories.Value = new List<string>();
        }
    }

    private void ClearOutputs()
    {
        ObjectCount.Value = 0;
        _currentResult = null;
        DebugTexture.Value = null;
        PointBuffer.Value = null;
        ObjectData.Value = null!;
        ActiveCategories.Value = new List<string>();
    }

    #region MediaPipe Integration
    private ObjectDetector? _objectDetector;
    private long _frameTimestamp;
    private readonly object _objectDetectorLock = new object();
    private readonly object _timestampLock = new object();

    private Task? _processingTask;
    private volatile bool _workerBusy;
    private long _lastInitAttemptTicks;
    private int _initAttempts;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<ObjectDetectionRequest> _inputQueue = new();
    private readonly ConcurrentQueue<ObjectDetectionResultPacket> _outputQueue = new();
    private ObjectDetectionResultPacket? _currentResult;
    private int _activeMaxObjects = -1;
    private MediaPipeAcceleration? _activeAcceleration;
    private int _activeInferenceThreads = -1;
    private float _activeMinDetectionConfidence = -1f;
    private DetectionModel _activeModel;
    private string _activeCategoryAllowlist = "";
    private string _activeCategoryDenylist = "";
    private volatile bool _disposed;
    private readonly object _workerLock = new object();
    private readonly MediaPipeWorkerReaper _reaper = new();

    private readonly ConcurrentDictionary<(int width, int height, T3.Graphics.Format format), T3.Graphics.Compat.Texture2D> _cachedStagingTextures = new();

    private readonly object _textureCacheLock = new object();

    private readonly ConcurrentBag<byte[]> _bufferPool = new();
    private readonly ConcurrentDictionary<int, ConcurrentBag<Detection[]>> _detectionArrayPool = new();
    private readonly object _poolLock = new object();
    #endregion

    #region Worker Thread
    private void InitializeWorker(int maxObjects, float minDetectionConfidence, bool debug, DetectionModel model,
                                  string categoryAllowlist, string categoryDenylist)
    {
        StopWorker();
        _activeMaxObjects = maxObjects;
        _activeMinDetectionConfidence = minDetectionConfidence;
        _activeModel = model;
        _activeCategoryAllowlist = categoryAllowlist;
        _activeCategoryDenylist = categoryDenylist;

        var modelName = model == DetectionModel.EfficientDetLite2
                            ? "efficientdet_lite2.tflite"
                            : "efficientdet_lite0.tflite";

        // Registry lookup only - the native detector is created on the worker below, so a slow model
        // load never stalls the evaluation thread and a native failure surfaces off the frame path.
        if (!MediaPipeModelResolver.TryResolve(modelName, this, out var fullPath))
        {
            // Not Debug-gated: a missing asset is the one failure a user has to act on, and the retry
            // backoff keeps this to one message per backoff window.
            Log.Warning($"[ObjectDetection] Model not found: {modelName}", this);
            return;
        }

        var acceleration = _activeAcceleration ?? MediaPipeAcceleration.Cpu;
        var inferenceThreads = _activeInferenceThreads;

        var allowList = string.IsNullOrEmpty(categoryAllowlist)
                            ? null
                            : categoryAllowlist.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        var denyList = string.IsNullOrEmpty(categoryDenylist)
                           ? null
                           : categoryDenylist.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        // The CTS and the task exist before any native work starts, so StopWorker always has
        // something to cancel and Update can tell "initializing" from "needs initialization".
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        _processingTask = Task.Run(() => InitializeAndRunWorker(fullPath, acceleration, inferenceThreads, maxObjects,
                                                               minDetectionConfidence, allowList, denyList, token, debug), token);
    }

    private void InitializeAndRunWorker(string fullPath, MediaPipeAcceleration acceleration, int inferenceThreads,
                                        int maxObjects, float minDetectionConfidence, List<string>? allowList,
                                        List<string>? denyList, CancellationToken token, bool debug)
    {
        ObjectDetector? detector = null;
        try
        {
            var delegateCase = acceleration == MediaPipeAcceleration.Xnnpack ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.Xnnpack
                : acceleration == MediaPipeAcceleration.Gpu ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.GPU
                : Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU;

            ObjectDetectorOptions BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate selected) => new(
                new Mediapipe.Tasks.Core.CoreBaseOptions(modelAssetPath: fullPath, delegateCase: selected, numThreads: inferenceThreads),
                VisionRunningMode.VIDEO,
                maxResults: maxObjects,
                scoreThreshold: minDetectionConfidence,
                categoryAllowList: allowList,
                categoryDenyList: denyList
            );

            try
            {
                detector = ObjectDetector.CreateFromOptions(BuildOptions(delegateCase));
            }
            catch (Exception gpuEx) when (acceleration != MediaPipeAcceleration.Cpu)
            {
                Log.Warning($"[ObjectDetection] GPU acceleration not available ({gpuEx.Message}) - using CPU.", this);
                detector = ObjectDetector.CreateFromOptions(BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU));
            }

            if (detector == null)
                return;

            var published = false;
            lock (_objectDetectorLock)
            {
                if (!token.IsCancellationRequested)
                {
                    _objectDetector = detector;
                    published = true;
                }
            }

            if (!published)
            {
                MediaPipeWorkerReaper.CloseNative(detector);
                return;
            }

            // A later failure starts its backoff from the first step again.
            _initAttempts = 0;

            // The worker loop owns this instance; StopWorker disposes it only after this task exits.
            WorkerLoop(token, detector, debug);
        }
        catch (Exception ex)
        {
            Log.Warning($"[ObjectDetection] Initialization failed: {ex.Message}", this);
            if (detector != null)
            {
                try { MediaPipeWorkerReaper.CloseNative(detector); } catch { }
                lock (_objectDetectorLock)
                {
                    if (ReferenceEquals(_objectDetector, detector))
                        _objectDetector = null;
                }
            }
        }
    }

    private void WorkerLoop(CancellationToken token, ObjectDetector detector, bool debug)
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
                        ProcessFrame(request, detector, debug);
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[ObjectDetection] Worker error: {ex.Message}", this);
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

    private void ProcessFrame(ObjectDetectionRequest request, ObjectDetector detector, bool debug)
    {
        using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        var result = detector.DetectForVideo(image, request.Timestamp);

        if (result.Detections != null && result.Detections.Count > 0)
        {
            var detections = ConvertDetectionResultToDetections(result, request.MinDetectionConfidence, request.MaxObjects, debug);
            var packet = new ObjectDetectionResultPacket
                             {
                                 Detections = detections,
                                 ObjectCount = detections != null ? detections.Length : 0,
                                 ImageWidth = request.Width,
                                 ImageHeight = request.Height
                             };
            _outputQueue.Enqueue(packet);
        }
        else
        {
            _outputQueue.Enqueue(new ObjectDetectionResultPacket
                                     {
                                         Detections = null,
                                         ObjectCount = 0,
                                         ImageWidth = request.Width,
                                         ImageHeight = request.Height
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

            ObjectDetector? nativeTask;
            lock (_objectDetectorLock)
            {
                nativeTask = _objectDetector;
                _objectDetector = null;
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
                ReturnDetectionArrayToPool(staleResult.Detections);
            }
        }
    }
    #endregion Worker Thread

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

    private Detection[] GetDetectionArray(int size)
    {
        if (!_detectionArrayPool.TryGetValue(size, out var pool))
        {
            lock (_poolLock)
            {
                if (!_detectionArrayPool.TryGetValue(size, out pool))
                {
                    pool = new ConcurrentBag<Detection[]>();
                    _detectionArrayPool[size] = pool;
                }
            }
        }

        if (pool.TryTake(out var arr))
        {
            return arr;
        }

        return new Detection[size];
    }

    private void ReturnDetectionArrayToPool(Detection[]? arr)
    {
        if (arr != null)
        {
            var size = arr.Length;
            if (!_detectionArrayPool.TryGetValue(size, out var pool))
            {
                lock (_poolLock)
                {
                    if (!_detectionArrayPool.TryGetValue(size, out pool))
                    {
                        pool = new ConcurrentBag<Detection[]>();
                        _detectionArrayPool[size] = pool;
                    }
                }
            }

            // Bounded per size: the object count varies frame to frame, so an unbounded bag would keep
            // one array per distinct count ever seen.
            if (pool.Count < MaxPooledBuffers)
                pool.Add(arr);
        }
    }

    private ObjectDetectionRequest? CreateRequestFromTexture(Texture2D texture, float minDetectionConfidence, int maxObjects)
    {
        var desc = texture.Description;

        // One readback per evaluation frame serves every MediaPipe operator reading this texture: the
        // mapped copy costs ~2 ms at 1080p and barely less at 640 px, so seven operators on one camera
        // would otherwise pay it seven times for byte-identical pixels.
        if (!MediaPipeFrameCache.TryGet(texture, MaxInferenceWidth, 3, out var frame,
                                        out var inferenceWidth, out var inferenceHeight, out var readbackError))
        {
            Log.Debug($"[ObjectDetection] Texture readback failed: {readbackError}", this);
            return null;
        }

        // The cached array is shared and read-only for this frame; the worker still gets its own pooled
        // copy because the native Image keeps that pointer for the duration of the inference.
        byte[] buffer = GetBuffer(frame.Length);
        System.Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);

        long ts;
        lock (_timestampLock)
        {
            ts = _frameTimestamp;
            _frameTimestamp += 33;
        }

        return new ObjectDetectionRequest
                   {
                       PixelData = buffer,
                       Width = inferenceWidth,
                       Height = inferenceHeight,
                       Timestamp = ts,
                       MinDetectionConfidence = minDetectionConfidence,
                       MaxObjects = maxObjects,
                   };
    }
    #endregion

    private void UpdateOutputsWithResult(ObjectDetectionResultPacket result, bool correctAspectRatio, float zScale)
    {
        UpdateCount.Value++;

        if (result.Detections == null || result.Detections.Length == 0)
        {
            ObjectCount.Value = 0;
            PointBuffer.Value = null;
            ObjectData.Value = null!;
            ActiveCategories.Value = new List<string>();
            return;
        }

        ObjectCount.Value = result.ObjectCount;

        var points = new StructuredList<T3.Core.DataTypes.Point>(result.ObjectCount * 5);
        var objectData = new Dict<float>(0f);
        var activeCategories = new List<string>();

        float imgW = result.ImageWidth > 0 ? result.ImageWidth : 1.0f;
        float imgH = result.ImageHeight > 0 ? result.ImageHeight : 1.0f;
        float aspectRatio = correctAspectRatio ? imgW / imgH : 1.0f;

        for (int i = 0; i < result.ObjectCount; i++)
        {
            var d = result.Detections[i];

            float cx = d.BoundingBox!.CenterX / imgW;
            float cy = d.BoundingBox.CenterY / imgH;
            float w = d.BoundingBox.Width / imgW;
            float h = d.BoundingBox.Height / imgH;
            float halfW = w / 2.0f;
            float halfH = h / 2.0f;

            void AddPoint(int indexOffset, float u, float v)
            {
                var p = new T3.Core.DataTypes.Point();

                p.Position = new System.Numerics.Vector3(
                                                         (u - 0.5f) * 2.0f * aspectRatio,
                                                         (0.5f - v) * 2.0f,
                                                         0
                                                        );
                p.Position.Z *= zScale;
                p.F1 = d.Confidence;
                p.Orientation = System.Numerics.Quaternion.Identity;
                p.Color = new System.Numerics.Vector4(1, 1, 1, 1);
                p.Scale = System.Numerics.Vector3.One;

                points.TypedElements[i * 5 + indexOffset] = p;
            }

            AddPoint(0, cx, cy);
            AddPoint(1, cx - halfW, cy - halfH);
            AddPoint(2, cx + halfW, cy - halfH);
            AddPoint(3, cx + halfW, cy + halfH);
            AddPoint(4, cx - halfW, cy + halfH);

            if (!string.IsNullOrEmpty(d.Label))
            {
                objectData[d.Label] = d.Confidence;
                if (!activeCategories.Contains(d.Label))
                {
                    activeCategories.Add(d.Label);
                }
            }
        }

        ObjectData.Value = objectData;
        ActiveCategories.Value = activeCategories;

        int stride = System.Runtime.InteropServices.Marshal.SizeOf<T3.Core.DataTypes.Point>();

        if (_pointBufferWithViews == null)
        {
            _pointBufferWithViews = new BufferWithViews();
        }

        ResourceManager.SetupStructuredBuffer(points.TypedElements, stride * points.NumElements, stride, ref _pointBufferWithViews.Buffer);

        if (_pointBufferWithViews.Buffer != null)
        {
            if (_pointBufferWithViews.Srv == null || _pointBufferWithViews.Srv.Description.Buffer.ElementCount != points.NumElements)
            {
                _pointBufferWithViews.Srv?.Dispose();
                _pointBufferWithViews.Srv = new ShaderResourceView(ResourceManager.Device, _pointBufferWithViews.Buffer,
                                                                   new ShaderResourceViewDescription
                                                                       {
                                                                           Format = T3.Graphics.Format.Unknown,
                                                                           Dimension = ShaderResourceViewDimension.Buffer,
                                                                           Buffer = new ShaderResourceViewDescription.BufferResource
                                                                                        {
                                                                                            ElementWidth = points.NumElements,
                                                                                            FirstElement = 0
                                                                                        }
                                                                       });
            }

            if (_pointBufferWithViews.Uav == null || _pointBufferWithViews.Uav.Description.Buffer.ElementCount != points.NumElements)
            {
                _pointBufferWithViews.Uav?.Dispose();
                _pointBufferWithViews.Uav = new UnorderedAccessView(ResourceManager.Device, _pointBufferWithViews.Buffer,
                                                                    new UnorderedAccessViewDescription
                                                                        {
                                                                            Format = T3.Graphics.Format.Unknown,
                                                                            Dimension = UnorderedAccessViewDimension.Buffer,
                                                                            Buffer = new UnorderedAccessViewDescription.BufferResource
                                                                                         {
                                                                                             ElementCount = points.NumElements,
                                                                                             FirstElement = 0,
                                                                                             Flags = UnorderedAccessViewBufferFlags.None
                                                                                         }
                                                                        });
            }

            PointBuffer.Value = _pointBufferWithViews;
        }
    }

    private BufferWithViews? _pointBufferWithViews;

    private void DrawDebugVisualsFromDetections(Mat mat, Detection[]? detections)
    {
        if (detections == null || detections.Length == 0) return;

        var boxColor = new Scalar(0, 255, 0, 255);
        var textColor = new Scalar(255, 255, 255, 255);

        int matWidth = mat.Width;
        int matHeight = mat.Height;

        foreach (var detection in detections)
        {
            var bbox = detection.BoundingBox;
            // MediaPipe boxes are normalized to the source image (origin top-left, same as the mat);
            // drawing the raw values as pixels collapsed every box into the top-left corner.
            int x = (int)((bbox!.CenterX - bbox.Width / 2) * matWidth);
            int y = (int)((bbox.CenterY - bbox.Height / 2) * matHeight);
            int w = (int)(bbox.Width * matWidth);
            int h = (int)(bbox.Height * matHeight);

            Cv2.Rectangle(mat, new OpenCvSharp.Rect(x, y, w, h), boxColor, 2);

            string label = $"{detection.Label ?? "Unknown"} {detection.Confidence:0.00}";
            var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.5, 1, out var baseline);

            int textY = y - 5;
            if (textY < textSize.Height) textY = y + textSize.Height + 5;

            Cv2.Rectangle(mat,
                          new OpenCvSharp.Point(x, textY - textSize.Height),
                          new OpenCvSharp.Point(x + textSize.Width, textY + baseline),
                          boxColor, -1);

            Cv2.PutText(mat, label, new OpenCvSharp.Point(x, textY),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 0, 255), 1, LineTypes.AntiAlias);
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

    private Detection[]? ConvertDetectionResultToDetections(DetectionResult result, float minDetectionConfidence, int maxObjects, bool debug)
    {
        if (result.Detections == null) return null;

        var detections = new List<Detection>();

        try
        {
            for (int i = 0; i < result.Detections.Count && i < maxObjects; i++)
            {
                var detection = result.Detections[i];

                if (detection.Categories != null && detection.Categories.Count > 0)
                {
                    var category = detection.Categories[0];
                    if (category.Score >= minDetectionConfidence)
                    {
                        float width = (float)(detection.BoundingBox.Right - detection.BoundingBox.Left);
                        float height = (float)(detection.BoundingBox.Bottom - detection.BoundingBox.Top);
                        float centerX = (float)detection.BoundingBox.Left + width / 2.0f;
                        float centerY = (float)detection.BoundingBox.Top + height / 2.0f;

                        detections.Add(new Detection
                                           {
                                               Label = category.CategoryName,
                                               Confidence = category.Score,
                                               BoundingBox = new BoundingBox
                                                                 {
                                                                     CenterX = centerX,
                                                                     CenterY = centerY,
                                                                     Width = width,
                                                                     Height = height
                                                                 }
                                           });
                    }
                }
            }

            if (detections.Count == 0)
                return null;

            // Rent the result array from the pool instead of ToArray(): the packet carries it across
            // threads and every consumer returns it (frame replacement, teardown), so a per-frame
            // allocation of this size is pure garbage otherwise.
            var rented = GetDetectionArray(detections.Count);
            detections.CopyTo(rented);
            return rented;
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[ConvertDetectionResultToDetections] Error converting MediaPipe detections: {ex.Message}", this);
            return null;
        }
    }

    #region Cleanup
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        // Stops the worker task and hands the detector to its deferred closer - closing it here
        // directly could hit a frame still running on the worker thread.
        _disposed = true;
        StopWorker();

        _debugTexture?.Dispose();
        _debugTexture = null;

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

        lock (_poolLock)
        {
            foreach (var pool in _detectionArrayPool.Values)
            {
                pool.Clear();
            }

            _detectionArrayPool.Clear();
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
    [Input(Guid = "23456789-0abc-def1-2345-67890abcdef1")]
    public readonly InputSlot<Texture2D> InputTexture = new();

    [Input(Guid = "34567890-abcd-ef12-3456-7890abcdef12")]
    public readonly InputSlot<bool> Enabled = new(true);

    [Input(Guid = "4567890a-bcde-f123-4567-890abcdef123")]
    public readonly InputSlot<int> MaxObjects = new(10);

    [Input(Guid = "567890ab-cdef-1234-5678-90abcdef1234")]
    public readonly InputSlot<float> MinDetectionConfidence = new(0.5f);

    [Input(Guid = "67890abc-def1-2345-6789-0abcdef12345", MappedType = typeof(DetectionModel))]
    public readonly InputSlot<int> Model = new();

    [Input(Guid = "7890abcd-ef12-3456-7890-abcdef123456")]
    public readonly InputSlot<bool> Debug = new(false);

    [Input(Guid = "890abcde-f123-4567-890a-bcdef1234567")]
    public readonly InputSlot<string> CategoryAllowlist = new();

    [Input(Guid = "90abcdef-1234-5678-90ab-cdef12345678")]
    public readonly InputSlot<string> CategoryDenylist = new();

    [Input(Guid = "0abcdef1-2345-6789-0abc-def123456789")]
    public readonly InputSlot<bool> CorrectAspectRatio = new(false);

    /// <summary>Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere),
    /// Xnnpack = multithreaded CPU (the fast default; see InferenceThreads), Gpu = OpenGL/EGL
    /// delegate (Linux native has it; the CPU-only Windows/macOS natives fall back to Cpu).</summary>
    [Input(Guid = "a1b2c3d4-0006-4a9b-8c2d-101010101010")]
    public readonly InputSlot<MediaPipeAcceleration> Acceleration = new(MediaPipeAcceleration.Xnnpack);

    /// <summary>Worker threads for the Xnnpack acceleration (0 = library default, which is effectively
        /// single-threaded). 4 is ~2x faster than the default on a 20-core machine; use 2 when
        /// several MediaPipe operators run at once. Ignored by Cpu and Gpu.</summary>
    [Input(Guid = "a1b2c3d4-1006-4a9b-8c2d-101010101010")]
    public readonly InputSlot<int> InferenceThreads = new(DefaultInferenceThreads);

    [Input(Guid = "abcdef12-3456-7890-abcd-ef1234567890")]
    public readonly InputSlot<float> ZScale = new(1.0f);
    #endregion

    private enum DetectionModel
    {
        EfficientDetLite0,
        EfficientDetLite2,
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
        Log.Warning($"[ObjectDetection] Debug readback failed: {ex.Message}", this);
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
        Log.Warning($"[ObjectDetection] Debug readback: row pitch {dataBox.RowPitch} < {rowBytes}.", this);
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

public class Detection
{
    public string? Label;
    public float Confidence;
    public BoundingBox? BoundingBox;
}

public class BoundingBox
{
    public float CenterX;
    public float CenterY;
    public float Width;
    public float Height;
}
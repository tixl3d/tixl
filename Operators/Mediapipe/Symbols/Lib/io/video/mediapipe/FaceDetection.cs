using OpenCvSharp;
using T3.Graphics.Compat;
#nullable enable

using Mediapipe.Tasks.Vision.FaceDetector;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Components.Containers;
using T3.Core.Resource.Assets;
using Image = Mediapipe.Framework.Formats.Image;

namespace Lib.io.video.mediapipe
{
    internal class FaceDetectionRequest
    {
        public byte[]? PixelData;
        public int Width;
        public int Height;
        public long Timestamp;
        public float ConfidenceThreshold;
        public int MaxFaces;
        public bool CorrectAspectRatio;
        public float ZScale;
    }

    internal class FaceDetectionResultPacket
    {
        public Point[]? Detections;
        public Dict<float>? FaceData;
        public int FaceCount;
    }

    [Guid("A1B2C3D4-E5F6-4798-89AB-CDEF12345679")]
    [ExportDependencies("mediapipe_c.dll", "libmediapipe_c.so", "libmediapipe_c.dylib", "libmediapipe_opencl_stub.dylib", "OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib", "Emgu.CV.dll", "cvextern.dll", "OpenGL.Net.dll", "CppSharp.Runtime.dll", "Google.Protobuf.dll")]
    public class FaceDetection : Instance<FaceDetection>
    {
        private const int MaxInferenceWidth = 640;
        private const int MaxCachedStagingTextures = 4;
        private const int MaxPooledBuffers = 4;

        private static readonly int MaxDownscaleThreads = Math.Clamp(Environment.ProcessorCount, 1, 8);

        /// <summary>Xnnpack worker threads used when the InferenceThreads input keeps its default.
        /// See <see cref="MediaPipeInferenceTuning"/> for why the binding's 0 is a bad default.</summary>
        private const int DefaultInferenceThreads = MediaPipeInferenceTuning.DefaultThreads;

        [Output(Guid = "B2C3D4E5-F6A7-489A-9B0C-DEF123456790", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Texture2D> OutputTexture = new();

        [Output(Guid = "C3D4E5F6-A7B8-49AB-AC1D-EF1234567891", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<BufferWithViews> PointBuffer = new();

        [Output(Guid = "D4E5F6A7-B8C9-4AB0-BD2E-F12345678902", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Dict<float>> FaceData = new();

        [Output(Guid = "E5F6A7B8-C9D0-4B01-CE3F-12345678A903", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<int> FaceCount = new();

        [Output(Guid = "F6A7B8C9-D0E1-4C12-DF4A-234567A89004", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<int> UpdateCount = new();

        [Output(Guid = "B2C3D4E5-F6A7-489A-9B0C-DEF123456758", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Texture2D> DebugTexture = new();

        public FaceDetection()
        {
            OutputTexture.UpdateAction = Update;
            PointBuffer.UpdateAction = Update;
            FaceData.UpdateAction = Update;
            FaceCount.UpdateAction = Update;
            UpdateCount.UpdateAction = Update;
            DebugTexture.UpdateAction = Update;
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
            var showDetections = ShowDetections.GetValue(context);
            var showKeypoints = ShowKeypoints.GetValue(context);
            var detectionSize = DetectionSize.GetValue(context);
            var detectionColor = DetectionColor.GetValue(context);
            var keypointColor = KeypointColor.GetValue(context);
            var confidenceThreshold = ConfidenceThreshold.GetValue(context);
            var maxFaces = MaxFaces.GetValue(context);
            var useCombinedAiOutput = UseCombinedAiOutput.GetValue(context);
            var debug = Debug.GetValue(context);
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
            var parametersChanged = maxFaces != _activeMaxFaces
                                    || Math.Abs(confidenceThreshold - _activeConfidenceThreshold) > 0.001f
                                    || acceleration != _activeAcceleration
                                    || inferenceThreads != _activeInferenceThreads;
            if (parametersChanged)
                _initAttempts = 0;

            var initializing = _faceDetector is null && _processingTask is { IsCompleted: false };
            var initNeeded = !initializing && (_processingTask == null || _processingTask.IsCompleted || _faceDetector is null || parametersChanged);
            var retryDelayMs = MediaPipeInitRetry.BackoffMs(_initAttempts);
            var retryBlocked = _faceDetector == null && _initAttempts > 0
                               && Environment.TickCount64 - _lastInitAttemptTicks < retryDelayMs;

            if (initNeeded && !retryBlocked)
            {
                _activeAcceleration = acceleration;
                _activeInferenceThreads = inferenceThreads;
                _lastInitAttemptTicks = Environment.TickCount64;
                _initAttempts++;
                InitializeWorker(maxFaces, confidenceThreshold, debug);
            }

            if (_faceDetector == null)
            {
                ClearOutputs();
                return;
            }

            if (_inputQueue.IsEmpty && !_workerBusy)
            {
                var request = CreateRequestFromTexture(inputTexture, confidenceThreshold, maxFaces, correctAspectRatio, zScale);
                if (request != null)
                {
                    _inputQueue.Enqueue(request);
                }
            }

            while (_outputQueue.TryDequeue(out var result))
            {
                if (_currentResult != null)
                {
                    ReturnPointArrayToPool(_currentResult.Detections);
                }

                // Assign every packet, including an empty one. Keeping the last non-empty result
                // latched the outputs onto the last face/hand/pose ever seen instead of reporting
                // that it left the frame - and made the count-zero paths unreachable.
                _currentResult = result;
            }

            if (_currentResult != null)
            {
                UpdateOutputsWithResult(_currentResult, showDetections, showKeypoints, detectionSize, detectionColor, keypointColor, useCombinedAiOutput, debug);
                UpdateCount.Value++;

                if (debug)
                {
                    using var mat = Texture2DToMat(inputTexture);
                    if (!mat.Empty())
                    {
                        // The stored coordinates carry the conversion-time aspect factor in X - invert
                        // it with the same value, or boxes land compressed on non-square images.
                        DrawDebugVisuals(mat, _currentResult, correctAspectRatio ? (float)mat.Width / mat.Height : 1.0f);
                        UpdateDebugTextureFromMat(mat);
                    }
                }
            }
            else
            {
                FaceCount.Value = 0;
                FaceData.Value = new Dict<float>(0f);
                PointBuffer.Value = null!;
                DebugTexture.Value = null!;
            }
        }

        private void ClearOutputs()
        {
            PointBuffer.Value = null!;
            FaceData.Value = new Dict<float>(0f);
            FaceCount.Value = 0;
            if (_currentResult != null)
            {
                ReturnPointArrayToPool(_currentResult.Detections);
                _currentResult = null;
            }
            DebugTexture.Value = null!;
        }

        #region MediaPipe Integration
        private Mediapipe.Tasks.Vision.FaceDetector.FaceDetector? _faceDetector;
        private long _frameTimestamp;
        private readonly object _faceDetectorLock = new object();
        private readonly object _timestampLock = new object();

        private Task? _processingTask;
        private volatile bool _workerBusy;
        private long _lastInitAttemptTicks;
        private int _initAttempts;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly ConcurrentQueue<FaceDetectionRequest> _inputQueue = new();
        private readonly ConcurrentQueue<FaceDetectionResultPacket> _outputQueue = new();
        private FaceDetectionResultPacket? _currentResult;
        private int _activeMaxFaces = -1;
        private MediaPipeAcceleration? _activeAcceleration;
        private int _activeInferenceThreads = -1;
        private float _activeConfidenceThreshold = -1f;
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
        private void InitializeWorker(int maxFaces, float confidence, bool debug)
        {
            StopWorker();
            _activeMaxFaces = maxFaces;
            _activeConfidenceThreshold = confidence;

            // Registry lookup only - the native detector creation runs on the worker below.
            var modelFound = MediaPipeModelResolver.TryResolve("blaze_face_short_range.tflite", this, out var fullPath);
            if (!modelFound)
            {
                // Not Debug-gated: a missing asset is the one failure a user has to fix, and the retry
                // backoff in Update keeps this to one message per backoff window instead of one per frame.
                Log.Warning($"[FaceDetection] Model not found: {fullPath}", this);
                return;
            }

            var acceleration = _activeAcceleration ?? MediaPipeAcceleration.Cpu;
            var inferenceThreads = _activeInferenceThreads;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            _processingTask = Task.Run(() => InitializeAndRunWorker(fullPath, acceleration, inferenceThreads, maxFaces, token, debug), token);
        }

        private void InitializeAndRunWorker(string fullPath, MediaPipeAcceleration acceleration, int inferenceThreads, int maxFaces, CancellationToken token, bool debug)
        {
            Mediapipe.Tasks.Vision.FaceDetector.FaceDetector? detector = null;
            try
            {
                var delegateCase = acceleration == MediaPipeAcceleration.Xnnpack ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.Xnnpack
                    : acceleration == MediaPipeAcceleration.Gpu ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.GPU
                    : Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU;

                FaceDetectorOptions BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate selected) => new(
                    new Mediapipe.Tasks.Core.CoreBaseOptions(modelAssetPath: fullPath, delegateCase: selected, numThreads: inferenceThreads),
                    VisionRunningMode.VIDEO,
                    minDetectionConfidence: 0.5f,
                    minSuppressionThreshold: 0.3f
                );

                try
                {
                    detector = Mediapipe.Tasks.Vision.FaceDetector.FaceDetector.CreateFromOptions(BuildOptions(delegateCase));
                }
                catch (Exception gpuEx) when (acceleration != MediaPipeAcceleration.Cpu)
                {
                    Log.Warning($"[FaceDetection] GPU acceleration not available ({gpuEx.Message}) - using CPU.", this);
                    detector = Mediapipe.Tasks.Vision.FaceDetector.FaceDetector.CreateFromOptions(BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU));
                }

                if (detector == null)
                    return;

                var published = false;
                lock (_faceDetectorLock)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _faceDetector = detector;
                        published = true;
                    }
                }

                if (!published)
                {
                    try { MediaPipeWorkerReaper.CloseNative(detector); } catch { }
                    return;
                }

                // A later failure starts its backoff from the first step again rather than from wherever
                // the previous outage ended.
                _initAttempts = 0;

                // The worker loop owns this instance; StopWorker closes it only after this task exits.
                WorkerLoop(token, detector, debug);
            }
            catch (Exception ex)
            {
                // Warning, not Debug-gated: this is the path a missing native library, a rejected
                // delegate or an unreadable model takes, and "nothing happens" without a log is the
                // worst possible failure mode. The retry backoff limits it to one message per window.
                Log.Warning($"[FaceDetection] Initialization failed: {ex.Message}", this);
                if (detector != null)
                {
                    try { MediaPipeWorkerReaper.CloseNative(detector); } catch { }
                    lock (_faceDetectorLock)
                    {
                        if (ReferenceEquals(_faceDetector, detector))
                            _faceDetector = null;
                    }
                }
            }
        }
        private void WorkerLoop(CancellationToken token, Mediapipe.Tasks.Vision.FaceDetector.FaceDetector detector, bool debug)
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
                        if (debug) Log.Error($"[FaceDetection] Worker error: {ex.Message}", this);
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

        private void ProcessFrame(FaceDetectionRequest request, Mediapipe.Tasks.Vision.FaceDetector.FaceDetector detector, bool debug)
        {
            using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgba, request.Width, request.Height, request.Width * 4, request.PixelData!);

            var result = detector.DetectForVideo(image, request.Timestamp);

            if (result is { } validResult && validResult.Detections != null && validResult.Detections.Count > 0)
            {
                var packet = new FaceDetectionResultPacket
                {
                    FaceCount = validResult.Detections.Count,
                    Detections = ConvertDetectionResultToPoints(validResult, request.Width, request.Height, request.ConfidenceThreshold, request.MaxFaces, request.CorrectAspectRatio, request.ZScale, debug),
                    FaceData = CalculateFaceData(validResult, request.Width, request.Height, request.ConfidenceThreshold, request.MaxFaces)
                };
                _outputQueue.Enqueue(packet);
            }
            else
            {
                _outputQueue.Enqueue(new FaceDetectionResultPacket
                {
                    FaceCount = 0,
                    Detections = Array.Empty<Point>(),
                    FaceData = null
                });
            }
        }

        private Point[]? ConvertDetectionResultToPoints(DetectionResult result, int imageWidth, int imageHeight, float confidenceThreshold, int maxFaces, bool correctAspectRatio, float zScale, bool debug)
        {
            if (result.Detections == null) return null;

            float aspectRatio = correctAspectRatio ? (float)imageWidth / imageHeight : 1.0f;

            try
            {
                // Count the valid faces first so the array can come from the pool - the
                // result-replacement path in Update returns it, keeping rentals and returns balanced.
                var validFaces = 0;
                foreach (var detection in result.Detections)
                {
                    if (validFaces >= maxFaces) break;

                    float confidence = 0f;
                    if (detection.Categories?.Count > 0)
                    {
                        confidence = detection.Categories[0].Score;
                    }

                    if (confidence >= confidenceThreshold
                        && detection.BoundingBox.Right > detection.BoundingBox.Left
                        && detection.BoundingBox.Bottom > detection.BoundingBox.Top)
                    {
                        validFaces++;
                    }
                }
                if (validFaces == 0) return null;

                var points = GetPointArray(validFaces * 10);
                int pointIndex = 0;
                int detectedFaces = 0;
                foreach (var detection in result.Detections)
                {
                    if (detectedFaces >= maxFaces) break;

                    float confidence = 0f;
                    if (detection.Categories?.Count > 0)
                    {
                        confidence = detection.Categories[0].Score;
                    }

                    if (confidence < confidenceThreshold)
                    {
                        continue;
                    }

                    if (detection.BoundingBox.Right > detection.BoundingBox.Left && detection.BoundingBox.Bottom > detection.BoundingBox.Top)
                    {
                        var bbox = detection.BoundingBox;

                        points[pointIndex++] = new Point
                        {
                            Position = new Vector3(((float)bbox.Left / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Top / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 0,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                        points[pointIndex++] = new Point
                        {
                            Position = new Vector3(((float)bbox.Right / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Top / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 1,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                        points[pointIndex++] = new Point
                        {
                            Position = new Vector3(((float)bbox.Right / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Bottom / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 2,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                        points[pointIndex++] = new Point
                        {
                            Position = new Vector3(((float)bbox.Left / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Bottom / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 3,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };

                        if (detection.Keypoints != null && detection.Keypoints.Count >= 6)
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                var keypoint = detection.Keypoints[i];
                                points[pointIndex++] = new Point
                                {
                                    Position = new Vector3((keypoint.X * 2.0f - 1.0f) * aspectRatio, 1.0f - keypoint.Y * 2.0f, 0),
                                    F1 = detectedFaces * 10 + 4 + i,
                                    F2 = confidence,
                                    Color = Vector4.One,
                                    Scale = Vector3.One,
                                    Orientation = Quaternion.Identity
                                };
                            }
                        }
                        else
                        {
                            var centerX = bbox.Left + (bbox.Right - bbox.Left) * 0.5f;
                            var centerY = bbox.Top + (bbox.Bottom - bbox.Top) * 0.5f;
                            for (int i = 0; i < 6; i++)
                            {
                                points[pointIndex++] = new Point
                                {
                                    Position = new Vector3((centerX / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - centerY / imageHeight * 2.0f, 0),
                                    F1 = detectedFaces * 10 + 4 + i,
                                    F2 = confidence,
                                    Color = Vector4.One,
                                    Scale = Vector3.One,
                                    Orientation = Quaternion.Identity
                                };
                            }
                        }

                        detectedFaces++;
                    }
                }

                return pointIndex > 0 ? points : null;
            }
            catch (Exception ex)
            {
                if (debug) Log.Error($"[ConvertDetectionResultToPoints] Error converting MediaPipe detections: {ex.Message}", this);
                return null;
            }
        }

        private Dict<float> CalculateFaceData(DetectionResult result, int width, int height, float confidenceThreshold, int maxFaces)
        {
            var dict = new Dict<float>(0f);
            int detectedFaces = 0;

            foreach (var detection in result.Detections)
            {
                if (detectedFaces >= maxFaces) break;

                float confidence = 0f;
                if (detection.Categories?.Count > 0)
                {
                    confidence = detection.Categories[0].Score;
                }

                if (confidence < confidenceThreshold)
                {
                    continue;
                }

                var faceIndex = detectedFaces;
                dict[$"face_{faceIndex}_confidence"] = confidence;

                if (detection.BoundingBox.Right > detection.BoundingBox.Left && detection.BoundingBox.Bottom > detection.BoundingBox.Top)
                {
                    var bbox = detection.BoundingBox;
                    dict[$"face_{faceIndex}_bbox_x"] = (float)bbox.Left / width;
                    dict[$"face_{faceIndex}_bbox_y"] = 1.0f - (float)bbox.Bottom / height;
                    dict[$"face_{faceIndex}_bbox_width"] = (float)(bbox.Right - bbox.Left) / width;
                    dict[$"face_{faceIndex}_bbox_height"] = (float)(bbox.Bottom - bbox.Top) / height;
                }

                if (detection.Keypoints != null && detection.Keypoints.Count >= 6)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var keypoint = detection.Keypoints[i];
                        dict[$"face_{faceIndex}_keypoint_{i}_x"] = keypoint.X;
                        dict[$"face_{faceIndex}_keypoint_{i}_y"] = 1.0f - keypoint.Y;
                        dict[$"face_{faceIndex}_keypoint_{i}_z"] = 0f;
                    }
                }

                detectedFaces++;
            }

            return dict;
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

                Mediapipe.Tasks.Vision.FaceDetector.FaceDetector? detector;
                lock (_faceDetectorLock)
                {
                    detector = _faceDetector;
                    _faceDetector = null;
                }

                // DetectForVideo cannot be interrupted and runs on the worker outside every lock, so the
                // native instance may only be closed after that task has fully exited - closing it earlier
                // is a use-after-free in native code. The reaper waits on the thread pool, so the
                // evaluation thread never blocks on an in-flight inference.
                _reaper.Queue(task, detector, cancellation);

                while (_inputQueue.TryDequeue(out var pendingRequest))
                {
                    ReturnBufferToPool(pendingRequest.PixelData);
                }

                // Results that were in flight when the worker stopped still own rented arrays.
                while (_outputQueue.TryDequeue(out var staleResult))
                {
                    ReturnPointArrayToPool(staleResult.Detections);
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
                // cache would keep one full-resolution staging texture per distinct size for the life of
                // the operator. Drop the older ones; only the current size is reused frame to frame.
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
            // Bounded: the buffer is pinned for its whole life, so a camera or window change that alters
            // the inference size must not leave one permanently-pinned array per historic size behind.
            if (b != null && _bufferPool.Count < MaxPooledBuffers)
                _bufferPool.Add(b);
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
            // Empty results carry the shared Array.Empty instance, never rented - pooling it would add
            // one entry per face-less inference without bound.
            if (arr is { Length: > 0 })
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

        private FaceDetectionRequest? CreateRequestFromTexture(Texture2D texture, float confidenceThreshold, int maxFaces, bool correctAspectRatio, float zScale)
        {
            var desc = texture.Description;

            // One readback per evaluation frame serves every MediaPipe operator reading this texture: the
            // mapped copy costs ~2 ms at 1080p and barely less at 640 px, so seven operators on one camera
            // would otherwise pay it seven times for byte-identical pixels.
            if (!MediaPipeFrameCache.TryGet(texture, MaxInferenceWidth, 4, out var frame,
                                            out var inferenceWidth, out var inferenceHeight, out var readbackError))
            {
                Log.Debug($"[FaceDetection] Texture readback failed: {readbackError}", this);
                return null;
            }

            // The cached array is shared and read-only for this frame; the worker still gets its own pooled
            // copy because the native Image keeps that pointer for the duration of the inference.
            byte[] buffer = GetBuffer(frame.Length);
            System.Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);

            long ts;
            lock(_timestampLock) { ts = _frameTimestamp; _frameTimestamp += 33; }

            return new FaceDetectionRequest
            {
                PixelData = buffer,
                Width = inferenceWidth,
                Height = inferenceHeight,
                Timestamp = ts,
                ConfidenceThreshold = confidenceThreshold,
                MaxFaces = maxFaces,
                CorrectAspectRatio = correctAspectRatio,
                ZScale = zScale
            };
        }
        #endregion Memory Management

        private void UpdateOutputsWithResult(FaceDetectionResultPacket result, bool showDetections, bool showKeypoints, float detectionSize, Vector4 detectionColor, Vector4 keypointColor, bool useCombinedAiOutput, bool debug)
        {
            if (result.Detections == null || result.Detections.Length == 0)
            {
                FaceCount.Value = 0;
                FaceData.Value = new Dict<float>(0f);
                PointBuffer.Value = null!;
                return;
            }

            FaceCount.Value = result.FaceCount;
            FaceData.Value = result.FaceData ?? new Dict<float>(0f);

            if (result.Detections != null && result.Detections.Length > 0)
            {
                UpdateDetectionBuffer(result.Detections, showDetections, showKeypoints, detectionSize, detectionColor, keypointColor, debug);
            }
            else
            {
                PointBuffer.Value = null!;
            }
        }

        private void DrawDebugVisuals(Mat mat, FaceDetectionResultPacket result, float aspectRatio)
        {
            if (result.Detections == null || result.Detections.Length == 0) return;

            var bboxColor = new Scalar(0, 255, 0, 255);
            var keypointColor = new Scalar(255, 0, 255, 255);
            var textColor = new Scalar(255, 255, 255, 255);

            int faceCount = result.FaceCount;

            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                int baseIdx = faceIndex * 10;
                if (baseIdx + 3 >= result.Detections.Length) continue;

                var p1 = result.Detections[baseIdx];
                var p2 = result.Detections[baseIdx + 1];
                var p3 = result.Detections[baseIdx + 2];
                var p4 = result.Detections[baseIdx + 3];

                int x1 = (int)((p1.Position.X / aspectRatio + 1.0f) / 2.0f * mat.Width);
                int y1 = (int)((1.0f - p1.Position.Y) / 2.0f * mat.Height);
                int x2 = (int)((p3.Position.X / aspectRatio + 1.0f) / 2.0f * mat.Width);
                int y2 = (int)((1.0f - p3.Position.Y) / 2.0f * mat.Height);

                int width = x2 - x1;
                int height = y2 - y1;

                Cv2.Rectangle(mat, new OpenCvSharp.Rect(x1, y1, width, height), bboxColor, 2);

                float confidence = p1.F2;
                var scoreText = $"{confidence:F2}";
                var textSize = Cv2.GetTextSize(scoreText, HersheyFonts.HersheySimplex, 0.5, 1, out var baseline);
                int textX = x1;
                int textY = Math.Max(0, y1 - textSize.Height - 5);
                Cv2.PutText(mat, scoreText, new OpenCvSharp.Point(textX, textY),
                            HersheyFonts.HersheySimplex, 0.5, textColor, 1, LineTypes.AntiAlias);

                for (int i = 0; i < 6; i++)
                {
                    int kpIdx = baseIdx + 4 + i;
                    if (kpIdx >= result.Detections.Length) break;

                    var kp = result.Detections[kpIdx];
                    int kpX = (int)((kp.Position.X / aspectRatio + 1.0f) / 2.0f * mat.Width);
                    int kpY = (int)((1.0f - kp.Position.Y) / 2.0f * mat.Height);

                    Cv2.Circle(mat, new OpenCvSharp.Point(kpX, kpY), 2, keypointColor, -1);
                }
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

        #region Texture Conversion (Minimal OpenCV Usage)
        /// <summary>
        /// Reads the input texture back as a BGRA Mat for the debug overlay. The caller owns (and
        /// disposes) the returned Mat. Driver row pitch is honoured: a plain contiguous copy of
        /// <c>width * height * 4</c> bytes shears the image whenever the driver pads rows, and on a
        /// tightly allocated staging texture it would read past the mapped region.
        /// </summary>
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
        Log.Warning($"[FaceDetection] Debug readback failed: {ex.Message}", this);
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
        Log.Warning($"[FaceDetection] Debug readback: row pitch {dataBox.RowPitch} < {rowBytes}.", this);
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

        #region Output Management
        private BufferWithViews? _detectionBuffer;
        private readonly object _bufferLock = new object();

        private void UpdateDetectionBuffer(Point[] points, bool showDetections, bool showKeypoints, float detectionSize, Vector4 detectionColor, Vector4 keypointColor, bool debug)
        {
            if (points == null! || points.Length == 0)
            {
                return;
            }

            lock (_bufferLock)
            {
                var pointCount = (showDetections ? points.Length / 10 * 4 : 0) + (showKeypoints ? points.Length / 10 * 6 : 0);

                if (pointCount <= 0)
                {
                    _detectionBuffer?.Dispose();
                    _detectionBuffer = null;
                    PointBuffer.Value = null!;
                    return;
                }

                var filteredPoints = new List<Point>(pointCount);
                for (int i = 0; i < points.Length; i++)
                {
                    var pointType = points[i].F1 % 10;
                    if ((showDetections && pointType < 4) || (showKeypoints && pointType >= 4))
                    {
                        var p = points[i];
                        p.Scale = new Vector3(detectionSize);
                        p.Color = pointType < 4 ? detectionColor : keypointColor;
                        filteredPoints.Add(p);
                    }
                }

                var filteredArray = filteredPoints.ToArray();
                var newSize = filteredArray.Length * Point.Stride;

                try
                {
                    var currentBuffer = _detectionBuffer;
                    if (currentBuffer == null || currentBuffer.Buffer!.Description.SizeInBytes != newSize)
                    {
                        _detectionBuffer?.Dispose();

                        if (filteredArray.Length > 0)
                        {
                            _detectionBuffer = new BufferWithViews();
                            ResourceManager.SetupStructuredBuffer(filteredArray,
                                newSize,
                                Point.Stride,
                                ref _detectionBuffer.Buffer);
                            ResourceManager.CreateStructuredBufferSrv(_detectionBuffer.Buffer!, ref _detectionBuffer.Srv);
                            ResourceManager.CreateStructuredBufferUav(_detectionBuffer.Buffer!,
                                UnorderedAccessViewBufferFlags.None,
                                ref _detectionBuffer.Uav);
                        }
                    }
                    else if (filteredArray.Length > 0)
                    {
                        ResourceManager.Device.ImmediateContext.UpdateSubresource(filteredArray, currentBuffer.Buffer!);
                    }
                    else
                    {
                        _detectionBuffer?.Dispose();
                        _detectionBuffer = null;
                    }

                    PointBuffer.Value = _detectionBuffer!;
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[FaceDetection] BUFFER MANAGEMENT: Exception during buffer operations: {ex.Message}", this);
                    _detectionBuffer?.Dispose();
                    _detectionBuffer = null;
                    PointBuffer.Value = null!;
                }
            }
        }
        #endregion

        #region Cleanup
        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                // Stops the worker task and hands the detector to the deferred closer - closing it here
                // directly could hit a frame still running on the worker thread. The wait is bounded so a
                // wedged native library can delay shutdown by at most this much, never hang it.
                _disposed = true;
                StopWorker();
                _reaper.WaitForPending(2000);

                lock (_bufferLock)
                {
                    try
                    {
                        _detectionBuffer?.Dispose();
                        _detectionBuffer = null;
                    }
                    catch (Exception)
                    {
                    }
                }

                _debugTexture?.Dispose();
                _debugTexture = null;

                lock (_textureCacheLock)
                {
                    foreach (var kvp in _cachedStagingTextures)
                    {
                        kvp.Value?.Dispose();
                    }
                    _cachedStagingTextures.Clear();
                }

                lock (_poolLock)
                {
                    foreach (var kvp in _pointArrayPool)
                    {
                        kvp.Value.Clear();
                    }
                    _pointArrayPool.Clear();
                }

                _bufferPool.Clear();

                base.Dispose(isDisposing);
            }
        }
        #endregion

        #region Input Parameters
        [Input(Guid = "A7B8C9D0-E1F2-4D23-E05B-345678901235")]
        public readonly InputSlot<Texture2D> InputTexture = new();

        [Input(Guid = "B8C9D0E1-F2A3-4E34-F16C-456789012346")]
        public readonly InputSlot<bool> Enabled = new(true);

        [Input(Guid = "C9D0E1F2-A3B4-4F45-A27D-567890123457")]
        public readonly InputSlot<float> ConfidenceThreshold = new(0.5f);

        [Input(Guid = "D0E1F2A3-B4C5-4056-B38E-678901234568")]
        public readonly InputSlot<int> MaxFaces = new(5);

        [Input(Guid = "E1F2A3B4-C6D7-4167-C49F-789012345679")]
        public readonly InputSlot<bool> ShowDetections = new(true);

        [Input(Guid = "F2A3B4C5-D6E7-4278-D5A0-890123456680")]
        public readonly InputSlot<bool> ShowKeypoints = new(true);

        [Input(Guid = "A3B4C5D6-E7F8-4389-E6B1-901234567681")]
        public readonly InputSlot<float> DetectionSize = new(3.0f);

        [Input(Guid = "B4C5D6E7-F8A9-4490-F7C2-012345678682")]
        public readonly InputSlot<Vector4> DetectionColor = new(Vector4.One);

        [Input(Guid = "C5D6E7F8-A9B0-45A1-A8D3-123456789683")]
        public readonly InputSlot<Vector4> KeypointColor = new(Vector4.One);

        [Input(Guid = "D6E7F8A9-B0C1-45A2-B9E4-234567890684")]
        public readonly InputSlot<bool> UseCombinedAiOutput = new(false);

        [Input(Guid = "E2F3A4B5-C6D7-58E9-F0A1-234567890BD3")]
        public readonly InputSlot<bool> Debug = new(false);

        [Input(Guid = "F3A4B5C6-D7E8-59F0-01A2-345678901CD4")]
        public readonly InputSlot<bool> CorrectAspectRatio = new(false);

        /// <summary>Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere),
        /// Xnnpack = multithreaded CPU (the fast default; see InferenceThreads), Gpu = OpenGL/EGL
        /// delegate (Linux native has it; the CPU-only Windows/macOS natives fall back to Cpu).</summary>
        [Input(Guid = "a1b2c3d4-0002-4a9b-8c2d-101010101010")]
        public readonly InputSlot<MediaPipeAcceleration> Acceleration = new(MediaPipeAcceleration.Xnnpack);

        /// <summary>Worker threads for the Xnnpack acceleration (0 = library default, which is effectively
        /// single-threaded). 4 is ~2x faster than the default on a 20-core machine; use 2 when
        /// several MediaPipe operators run at once. Ignored by Cpu and Gpu.</summary>
        [Input(Guid = "a1b2c3d4-1002-4a9b-8c2d-101010101010")]
        public readonly InputSlot<int> InferenceThreads = new(DefaultInferenceThreads);


        [Input(Guid = "A4B5C6D7-E8F9-60A1-12B3-456789012DE5")]
        public readonly InputSlot<float> ZScale = new(1.0f);
        #endregion
    }
}

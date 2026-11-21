#nullable enable

using System.Threading;
using Mediapipe;
using OpenCvSharp;
using T3.Graphics.Compat;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using T3.Core.Resource.Assets;
using Image = Mediapipe.Framework.Formats.Image;
// ReSharper disable EmptyGeneralCatchClause
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

#nullable enable

namespace Lib.io.video.mediapipe
{
    internal sealed class FaceLandmarkRequest
    {
        public byte[]? PixelData;
        public int Width;
        public int Height;
        public long Timestamp;
    }

    internal sealed class FaceLandmarkResultPacket
    {
        public Point[]? Landmarks;
        public Dict<float>? FaceData;
        public Dict<float>? Blendshapes;
        public int FaceCount;
    }

    [Guid("9b2c3d4e-5f6a-4798-89ab-cdef12345678")]
    [ExportDependencies("mediapipe_c.dll", "libmediapipe_c.so", "libmediapipe_c.dylib", "libmediapipe_opencl_stub.dylib", "OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib", "Emgu.CV.dll", "cvextern.dll", "OpenGL.Net.dll", "CppSharp.Runtime.dll", "Google.Protobuf.dll")]
    public class FaceLandmarkDetection : Instance<FaceLandmarkDetection>
    {
        [Output(Guid = "a1b2c3d4-e5f6-4798-89ab-cdef12345679", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Texture2D?> OutputTexture = new();

        [Output(Guid = "b2c3d4e5-f6a7-489a-9b0c-def123456780", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Dict<float>> FaceData = new();

        [Output(Guid = "c3d4e5f6-a7b8-49ab-ac1d-ef1234567891", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<int> FaceCount = new();

        [Output(Guid = "d4e5f6a7-b8c9-4ab0-bd2e-f12345678902", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Texture2D?> DebugTexture = new();

        [Output(Guid = "e5f6a7b8-c9d0-4b01-ce3f-123456789013", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<BufferWithViews?> PointBuffer = new();

        [Output(Guid = "f6a7b8c9-d0e1-4c12-df4a-234567a89014", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly Slot<Dict<float>> Blendshapes = new();

        public FaceLandmarkDetection()
        {
            OutputTexture.UpdateAction = Update;
            FaceData.UpdateAction = Update;
            FaceCount.UpdateAction = Update;
            DebugTexture.UpdateAction = Update;
            PointBuffer.UpdateAction = Update;
            Blendshapes.UpdateAction = Update;
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
            var showLandmarks = ShowLandmarks.GetValue(context);
            var scale = LandmarkScale.GetValue(context);
            var normalize = NormalizeLandmarks.GetValue(context);
            var maxFaces = MaxFaces.GetValue(context);

            var minFaceDetectionConfidence = MinFaceDetectionConfidence.GetValue(context);
            var minFacePresenceConfidence = MinFacePresenceConfidence.GetValue(context);
            var minTrackingConfidence = MinTrackingConfidence.GetValue(context);

            var debug = Debug.GetValue(context);
            var useCombinedAIOutput = UseCombinedAIOutput.GetValue(context);
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
            var parametersChanged = maxFaces != _activeMaxFaces
                                    || acceleration != _activeAcceleration
                                    || inferenceThreads != _activeInferenceThreads
                                    || Math.Abs(minFaceDetectionConfidence - _activeMinFaceDetectionConfidence) > 0.001f
                                    || Math.Abs(minFacePresenceConfidence - _activeMinFacePresenceConfidence) > 0.001f
                                    || Math.Abs(minTrackingConfidence - _activeMinTrackingConfidence) > 0.001f;
            if (parametersChanged)
                _initAttempts = 0;

            var initializing = _faceLandmarker is null && _processingTask is { IsCompleted: false };
            var initNeeded = !initializing && (_processingTask == null || _processingTask.IsCompleted || _faceLandmarker is null || parametersChanged);
            var retryDelayMs = MediaPipeInitRetry.BackoffMs(_initAttempts);
            var retryBlocked = _faceLandmarker == null && _initAttempts > 0
                               && Environment.TickCount64 - _lastInitAttemptTicks < retryDelayMs;

            if (initNeeded && !retryBlocked)
            {
                _activeAcceleration = acceleration;
                _activeInferenceThreads = inferenceThreads;
                _lastInitAttemptTicks = Environment.TickCount64;
                _initAttempts++;
                InitializeWorker(maxFaces, minFaceDetectionConfidence, minFacePresenceConfidence, minTrackingConfidence, debug);
            }

            if (_faceLandmarker == null)
            {
                ClearOutputs();
                return;
            }

            if (_inputQueue.IsEmpty && !_workerBusy)
            {
                var request = CreateRequestFromTexture(inputTexture);
                if (request != null)
                {
                    _inputQueue.Enqueue(request);
                }
            }

            float aspectRatio = 1.0f;
            if (correctAspectRatio && inputTexture != null)
            {
                aspectRatio = (float)inputTexture.Description.Width / inputTexture.Description.Height;
            }

            while (_outputQueue.TryDequeue(out var result))
            {
                if (_currentResult != null)
                {
                    ReturnPointArrayToPool(_currentResult.Landmarks);
                }

                // Assign every packet, including an empty one. Keeping the last non-empty result
                // latched the outputs onto the last face/hand/pose ever seen instead of reporting
                // that it left the frame - and made the count-zero paths unreachable.
                _currentResult = result;
            }

            if (_currentResult != null)
            {
                UpdateOutputsWithProcessing(_currentResult, scale, normalize, showLandmarks, aspectRatio, zScale, useCombinedAIOutput);

                if (debug)
                {
                    using var mat = Texture2DToMat(inputTexture!);
                    if (!mat.Empty())
                    {
                        DrawDebugVisuals(mat, _currentResult);
                        UpdateDebugTextureFromMat(mat);
                    }
                }
            }
            else
            {
                FaceCount.Value = 0;
                FaceData.Value = new Dict<float>(0f);
                PointBuffer.Value = null;
                Blendshapes.Value = new Dict<float>(0f);
            }
        }

        private void ClearOutputs()
        {
            FaceData.Value = new Dict<float>(0f);
            FaceCount.Value = 0;
            if (_currentResult != null)
            {
                ReturnPointArrayToPool(_currentResult.Landmarks);
                _currentResult = null;
            }
            PointBuffer.Value = null;
            Blendshapes.Value = new Dict<float>(0f);
        }

        #region Worker Thread
        private FaceLandmarker? _faceLandmarker;
        private const int MaxInferenceWidth = 640;
        private const int MaxCachedStagingTextures = 4;
        private const int MaxPooledBuffers = 4;

        private static readonly int MaxDownscaleThreads = Math.Clamp(Environment.ProcessorCount, 1, 8);

        /// <summary>Xnnpack worker threads used when the InferenceThreads input keeps its default.
        /// See <see cref="MediaPipeInferenceTuning"/> for why the binding's 0 is a bad default.</summary>
        private const int DefaultInferenceThreads = MediaPipeInferenceTuning.DefaultThreads;

        private Task? _processingTask;
        private volatile bool _workerBusy;
        private long _lastInitAttemptTicks;
        private int _initAttempts;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly ConcurrentQueue<FaceLandmarkRequest> _inputQueue = new();
        private readonly ConcurrentQueue<FaceLandmarkResultPacket> _outputQueue = new();
        private FaceLandmarkResultPacket? _currentResult;

        private int _activeMaxFaces = -1;
        private MediaPipeAcceleration? _activeAcceleration;
        private int _activeInferenceThreads = -1;
        private float _activeMinFaceDetectionConfidence = -1f;
        private float _activeMinFacePresenceConfidence = -1f;
        private float _activeMinTrackingConfidence = -1f;

        private long _frameCounter = 0;
        private readonly object _timestampLock = new object();
        private readonly object _faceLandmarkerLock = new object();
        private readonly object _landmarksLock = new object();

        private readonly ConcurrentDictionary<(int width, int height, T3.Graphics.Format format), T3.Graphics.Compat.Texture2D> _cachedStagingTextures = new();

        private readonly object _textureCacheLock = new object();

        private readonly ConcurrentBag<byte[]> _bufferPool = new();
        private readonly ConcurrentDictionary<int, ConcurrentBag<Point[]>> _pointArrayPool = new();
        private readonly object _poolLock = new object();
        private volatile bool _disposed;
        private readonly object _workerLock = new object();
        private readonly MediaPipeWorkerReaper _reaper = new();

        private void InitializeWorker(int maxFaces, float minFaceDetectionConfidence, float minFacePresenceConfidence, float minTrackingConfidence, bool debug)
        {
            StopWorker();
            _activeMaxFaces = maxFaces;
            _activeMinFaceDetectionConfidence = minFaceDetectionConfidence;
            _activeMinFacePresenceConfidence = minFacePresenceConfidence;
            _activeMinTrackingConfidence = minTrackingConfidence;

            // Registry lookup only - the native landmarker creation runs on the worker below.
            var modelFound = MediaPipeModelResolver.TryResolve("face_landmarker.task", this, out var fullPath);
            if (!modelFound)
            {
                // Not Debug-gated: a missing asset is the one failure a user has to act on, and the retry
                // backoff keeps this to one message per backoff window.
                Log.Warning($"[FaceLandmark] Model not found: {fullPath}", this);
                return;
            }

            var acceleration = _activeAcceleration ?? MediaPipeAcceleration.Cpu;
            var inferenceThreads = _activeInferenceThreads;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            _processingTask = Task.Run(() => InitializeAndRunWorker(fullPath, acceleration, inferenceThreads, maxFaces,
                                                                    minFaceDetectionConfidence, minFacePresenceConfidence,
                                                                    minTrackingConfidence, token, debug), token);
        }

        private void InitializeAndRunWorker(string fullPath, MediaPipeAcceleration acceleration, int inferenceThreads, int maxFaces,
                                            float minFaceDetectionConfidence, float minFacePresenceConfidence, float minTrackingConfidence,
                                            CancellationToken token, bool debug)
        {
            FaceLandmarker? landmarker = null;
            try
            {
                var delegateCase = acceleration == MediaPipeAcceleration.Xnnpack ? CoreBaseOptions.Delegate.Xnnpack
                    : acceleration == MediaPipeAcceleration.Gpu ? CoreBaseOptions.Delegate.GPU
                    : CoreBaseOptions.Delegate.CPU;

                FaceLandmarkerOptions BuildOptions(CoreBaseOptions.Delegate selected) => new(
                    baseOptions: new CoreBaseOptions(modelAssetPath: fullPath, delegateCase: selected, numThreads: inferenceThreads),
                    runningMode: VisionRunningMode.VIDEO,
                    numFaces: maxFaces,
                    minFaceDetectionConfidence: minFaceDetectionConfidence,
                    minFacePresenceConfidence: minFacePresenceConfidence,
                    minTrackingConfidence: minTrackingConfidence,
                    outputFaceBlendshapes: true,
                    outputFaceTransformationMatrixes: false
                );

                try
                {
                    landmarker = FaceLandmarker.CreateFromOptions(BuildOptions(delegateCase));
                }
                catch (Exception gpuEx) when (acceleration != MediaPipeAcceleration.Cpu)
                {
                    Log.Warning($"[FaceLandmark] GPU acceleration not available ({gpuEx.Message}) - using CPU.", this);
                    landmarker = FaceLandmarker.CreateFromOptions(BuildOptions(CoreBaseOptions.Delegate.CPU));
                }

                if (landmarker == null)
                    return;

                var published = false;
                lock (_faceLandmarkerLock)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _faceLandmarker = landmarker;
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
                Log.Warning($"[FaceLandmark] Initialization failed: {ex.Message}", this);
                if (landmarker != null)
                {
                    try { MediaPipeWorkerReaper.CloseNative(landmarker); } catch { }
                    lock (_faceLandmarkerLock)
                    {
                        if (ReferenceEquals(_faceLandmarker, landmarker))
                            _faceLandmarker = null;
                    }
                }
            }
        }
        private void WorkerLoop(CancellationToken token, FaceLandmarker landmarker, bool debug)
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
                        if (debug) Log.Error($"[FaceLandmark] Worker error: {ex.Message}", this);
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

        private void ProcessFrame(FaceLandmarkRequest request, FaceLandmarker landmarker, bool debug)
        {
            using var image = new Image(ImageFormat.Types.Format.Srgba, request.Width, request.Height, request.Width * 4, request.PixelData!);

            var result = landmarker.DetectForVideo(image, request.Timestamp);

            if (result.FaceLandmarks != null && result.FaceLandmarks.Count > 0)
            {
                var packet = new FaceLandmarkResultPacket
                {
                    FaceCount = result.FaceLandmarks.Count,
                    Landmarks = RawToPoints(result),
                    FaceData = CalculateRawBounds(result),
                    Blendshapes = CalculateBlendshapes(result)
                };
                _outputQueue.Enqueue(packet);
            }
            else
            {
                _outputQueue.Enqueue(new FaceLandmarkResultPacket
                {
                    FaceCount = 0,
                    Landmarks = Array.Empty<Point>(),
                    FaceData = null,
                    Blendshapes = null
                });
            }
        }

        private void DrawDebugVisuals(Mat mat, FaceLandmarkResultPacket result)
        {
            if (result.Landmarks == null || result.FaceCount == 0) return;

            var landmarkColor = new Scalar(0, 255, 0, 255); // Green
            var connectionColor = new Scalar(255, 0, 0, 255); // Red

            const int landmarksPerFace = 478;

            for (int faceIndex = 0; faceIndex < result.FaceCount; faceIndex++)
            {
                int startIdx = faceIndex * landmarksPerFace;

                for (int i = 0; i < landmarksPerFace; i++)
                {
                    int idx = startIdx + i;
                    if (idx >= result.Landmarks.Length) break;

                    var p = result.Landmarks[idx];
                    // Convert centered coordinates back to pixel coordinates for debug drawing
                    // X: (x + 0.5) * width
                    // Y: (0.5 - y) * height
                    int x = (int)((p.Position.X + 0.5f) * mat.Width);
                    int y = (int)((0.5f - p.Position.Y) * mat.Height);
                    Cv2.Circle(mat, x, y, 2, landmarkColor, -1);
                }

                // Draw face oval (simplified face mesh)
                int[] faceOval = { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109 };

                for (int i = 0; i < faceOval.Length - 1; i++)
                {
                    int idx1 = startIdx + faceOval[i];
                    int idx2 = startIdx + faceOval[i + 1];

                    if (idx1 < result.Landmarks.Length && idx2 < result.Landmarks.Length)
                    {
                        var p1 = result.Landmarks[idx1];
                        var p2 = result.Landmarks[idx2];

                        var pt1 = new OpenCvSharp.Point((int)((p1.Position.X + 0.5f) * mat.Width), (int)((0.5f - p1.Position.Y) * mat.Height));
                        var pt2 = new OpenCvSharp.Point((int)((p2.Position.X + 0.5f) * mat.Width), (int)((0.5f - p2.Position.Y) * mat.Height));
                        Cv2.Line(mat, pt1, pt2, connectionColor, 1);
                    }
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
                    Format = T3.Graphics.Format.B8G8R8A8_UNorm, // Mat is BGRA
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
        Log.Warning($"[FaceLandmark] Debug readback failed: {ex.Message}", this);
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
        Log.Warning($"[FaceLandmark] Debug readback: row pitch {dataBox.RowPitch} < {rowBytes}.", this);
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

        private Point[] RawToPoints(FaceLandmarkerResult result)
        {
            // Count first so the array can come from the pool - the result-replacement path
            // in Update returns it, keeping rentals and returns balanced.
            var total = 0;
            foreach (var face in result.FaceLandmarks)
            {
                if (face.landmarks == null) continue;
                total += Math.Min(face.landmarks.Count, 478);
            }
            if (total == 0) return Array.Empty<Point>();

            var points = GetPointArray(total);
            int pointIndex = 0;
            foreach (var face in result.FaceLandmarks)
            {
                if (face.landmarks == null) continue;
                int count = Math.Min(face.landmarks.Count, 478);
                for (int i = 0; i < count; i++)
                {
                    var l = face.landmarks[i];
                    // Convert to Centered Normalized Coordinates (-0.5 to 0.5)
                    // X: Right is Positive
                    // Y: Up is Positive (Flip MediaPipe Y)
                    // Z: Forward/Depth (Flip MediaPipe Z)

                    var centeredX = l.X - 0.5f;
                    var centeredY = 0.5f - l.Y;
                    var z = -l.Z;

                    points[pointIndex++] = new Point { Position = new Vector3(centeredX, centeredY, z), F1 = i, Orientation = Quaternion.Identity };
                }
            }
            return points;
        }

        private Dict<float> CalculateRawBounds(FaceLandmarkerResult result)
        {
            var dict = new Dict<float>(0f);
            for (int i = 0; i < result.FaceLandmarks.Count; i++)
            {
                var face = result.FaceLandmarks[i];
                if (face.landmarks == null) continue;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                foreach (var l in face.landmarks)
                {
                    if (l.X < minX) minX = l.X;
                    if (l.Y < minY) minY = l.Y;
                    if (l.X > maxX) maxX = l.X;
                    if (l.Y > maxY) maxY = l.Y;
                }

                string p = $"face_{i}";
                dict[$"{p}_raw_x"] = minX;
                dict[$"{p}_raw_y"] = minY;
                dict[$"{p}_raw_w"] = maxX - minX;
                dict[$"{p}_raw_h"] = maxY - minY;
            }
            return dict;
        }

        // Keys repeat every frame for the same face/category - intern them so camera-rate
        // blendshape updates stop allocating interpolated strings.
        private readonly ConcurrentDictionary<(int FaceIndex, string Category), string> _blendshapeKeyCache = new();

        private Dict<float> CalculateBlendshapes(FaceLandmarkerResult result)
        {
            var dict = new Dict<float>(0f);

            if (result.FaceBlendshapes == null) return dict;

            for (int faceIndex = 0; faceIndex < result.FaceBlendshapes.Count; faceIndex++)
            {
                var faceBlendshapes = result.FaceBlendshapes[faceIndex];
                if (faceBlendshapes.Categories == null) continue;

                foreach (var category in faceBlendshapes.Categories)
                {
                    string key = _blendshapeKeyCache.GetOrAdd((faceIndex, category.CategoryName ?? ""),
                                                              static k => $"face_{k.FaceIndex}_blendshape_{k.Category}");
                    dict[key] = category.Score;
                }
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

                FaceLandmarker? landmarker;
                lock (_faceLandmarkerLock)
                {
                    landmarker = _faceLandmarker;
                    _faceLandmarker = null;
                }

                // DetectForVideo cannot be interrupted and runs on the worker outside every lock, so the
                // native instance may only be closed after that task has fully exited - closing it earlier
                // is a use-after-free in native code. The reaper waits on the thread pool, so the
                // evaluation thread never blocks on an in-flight inference.
                _reaper.Queue(task, landmarker, cancellation);

                while (_inputQueue.TryDequeue(out var pendingRequest))
                {
                    ReturnBufferToPool(pendingRequest.PixelData);
                }

                // Results that were in flight when the worker stopped still own rented arrays.
                while (_outputQueue.TryDequeue(out var staleResult))
                {
                    ReturnPointArrayToPool(staleResult.Landmarks);
                }
            }
        }
        #endregion

        #region Main Thread Processing
        private void UpdateOutputsWithProcessing(FaceLandmarkResultPacket raw, float scale, bool normalize, bool showLandmarks, float aspectRatio, float zScale, bool useCombinedAIOutput)
        {
            if (raw.Landmarks == null || raw.Landmarks.Length == 0)
            {
                FaceCount.Value = 0;
                FaceData.Value = new Dict<float>(0f);
                PointBuffer.Value = null;
                return;
            }

            FaceCount.Value = raw.FaceCount;
            FaceData.Value = raw.FaceData ?? new Dict<float>(0f);
            Blendshapes.Value = raw.Blendshapes ?? new Dict<float>(0f);

            var finalPoints = (Point[])raw.Landmarks.Clone();

            int pointsPerFace = 478;
            int faces = raw.FaceCount;

            for (int f = 0; f < faces; f++)
            {
                float rawMinX = FaceData.Value[$"face_{f}_raw_x"];
                float rawMinY = FaceData.Value[$"face_{f}_raw_y"];
                float rawW = FaceData.Value[$"face_{f}_raw_w"];
                float rawH = FaceData.Value[$"face_{f}_raw_h"];

                float rawCenterX = rawMinX + rawW * 0.5f;
                float rawCenterY = rawMinY + rawH * 0.5f;

                float displayY = 1.0f - (rawMinY + rawH);

                int startIdx = f * pointsPerFace;
                int endIdx = Math.Min((f + 1) * pointsPerFace, finalPoints.Length);

                for (int i = startIdx; i < endIdx; i++)
                {
                    Vector3 pos = finalPoints[i].Position;

                    if (normalize)
                    {
                        // Normalize relative to face bounding box
                        // pos is currently centered (-0.5 to 0.5)
                        // rawCenterX/Y are 0-1

                        // Convert centered pos back to 0-1 for calculation
                        float posX01 = pos.X + 0.5f;
                        float posY01 = 0.5f - pos.Y; // Flip back Y

                        if (rawW > 0.001f) pos.X = (posX01 - rawCenterX) / rawW;

                        // For Y, we want Y up.
                        // rawCenterY is 0 at top.
                        // We want relative to center, Y up.
                        // (0.5 - pos.Y) is 0-1 Y (top-down)
                        // (0.5 - pos.Y) - rawCenterY is delta Y (top-down)
                        // -(delta Y) is delta Y (bottom-up)
                        // So: (rawCenterY - (0.5 - pos.Y)) / rawH
                        // = (rawCenterY - 0.5 + pos.Y) / rawH

                        if (rawH > 0.001f) pos.Y = (rawCenterY - posY01) / rawH;

                        // Z is already flipped, just scale relative to width?
                        // Usually normalized landmarks are relative to width
                        if (rawW > 0.001f) pos.Z = pos.Z / rawW;
                    }
                    else
                    {
                        // Standardize to -1..1 range
                        pos.X *= 2.0f * aspectRatio;
                        pos.Y *= 2.0f;
                        pos.Z *= zScale;
                    }

                    pos.X *= scale;
                    pos.Y *= scale;
                    pos.Z *= scale; // Apply uniform scale
                    finalPoints[i].Position = pos;
                }

                if (normalize)
                {
                    FaceData.Value[$"face_{f}_bbox_x"] = -0.5f * scale;
                    FaceData.Value[$"face_{f}_bbox_y"] = -0.5f * scale;
                    FaceData.Value[$"face_{f}_bbox_width"] = 1.0f * scale;
                    FaceData.Value[$"face_{f}_bbox_height"] = 1.0f * scale;
                }
                else
                {
                    FaceData.Value[$"face_{f}_bbox_x"] = rawMinX * scale;
                    FaceData.Value[$"face_{f}_bbox_y"] = displayY * scale;
                    FaceData.Value[$"face_{f}_bbox_width"] = rawW * scale;
                    FaceData.Value[$"face_{f}_bbox_height"] = rawH * scale;
                }
            }

            lock (_landmarksLock)
            {
            }
            UpdateLandmarkBuffer(finalPoints, showLandmarks);
            PointBuffer.Value = showLandmarks ? _landmarkBuffer : null;

            if (useCombinedAIOutput)
            {
                // GenerateAITextures(finalPoints, faces);
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

        private FaceLandmarkRequest? CreateRequestFromTexture(Texture2D texture)
        {
            var desc = texture.Description;

            // One readback per evaluation frame serves every MediaPipe operator reading this texture: the
            // mapped copy costs ~2 ms at 1080p and barely less at 640 px, so seven operators on one camera
            // would otherwise pay it seven times for byte-identical pixels.
            if (!MediaPipeFrameCache.TryGet(texture, MaxInferenceWidth, 4, out var frame,
                                            out var inferenceWidth, out var inferenceHeight, out var readbackError))
            {
                Log.Debug($"[FaceLandmark] Texture readback failed: {readbackError}", this);
                return null;
            }

            // The cached array is shared and read-only for this frame; the worker still gets its own pooled
            // copy because the native Image keeps that pointer for the duration of the inference.
            byte[] buffer = GetBuffer(frame.Length);
            System.Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);

            long ts;
            lock(_timestampLock) { ts = _frameCounter; _frameCounter += 33; }

            return new FaceLandmarkRequest { PixelData = buffer, Width = inferenceWidth, Height = inferenceHeight, Timestamp = ts };
        }
        #endregion Memory Management

        #region Render Buffer
        private BufferWithViews? _landmarkBuffer;
        private readonly object _bufferLock = new object();

        private void UpdateLandmarkBuffer(Point[] landmarks, bool showLandmarks)
        {
            if (landmarks == null || landmarks.Length == 0)
            {
                return;
            }

            lock (_bufferLock)
            {
                int sizeInBytes = Point.Stride * landmarks.Length;

                try
                {
                    var currentBuffer = _landmarkBuffer;
                    if (currentBuffer == null || currentBuffer.Buffer!.Description.SizeInBytes != sizeInBytes)
                    {
                        _landmarkBuffer?.Dispose();
                        _landmarkBuffer = new BufferWithViews();
                        ResourceManager.SetupStructuredBuffer(landmarks, sizeInBytes, Point.Stride, ref _landmarkBuffer.Buffer);
                        ResourceManager.CreateStructuredBufferSrv(_landmarkBuffer.Buffer!, ref _landmarkBuffer.Srv);
                        ResourceManager.CreateStructuredBufferUav(_landmarkBuffer.Buffer!, UnorderedAccessViewBufferFlags.None, ref _landmarkBuffer.Uav);
                    }
                    else
                    {
                        ResourceManager.Device.ImmediateContext.UpdateSubresource(landmarks, currentBuffer.Buffer!);
                    }
                }
                catch (Exception)
                {
                    _landmarkBuffer?.Dispose();
                    _landmarkBuffer = null;
                }
            }
        }
        #endregion

        #region AI Texture Generation
        private Texture2D? _aiDataTexture;
        private Texture2D? _aiDataHighPrecisionTexture;
        private Texture2D? _aiDataSegmentationTexture;
        private readonly object _aiTextureLock = new object();

        private void GenerateAITextures(Point[] landmarks, int faceCount)
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
                    int maxFaces = 5;
                    int maxLandmarksPerFace = 478;
                    int totalDataPoints = 4 + (maxFaces * maxLandmarksPerFace);

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

                    FillAITextures(landmarks, faceCount, textureWidth, textureHeight);
                }
                catch (Exception)
                {
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

        private void FillAITextures(Point[] landmarks, int faceCount, int textureWidth, int textureHeight)
        {
            var rgba32FData = new float[textureWidth * textureHeight * 4];
            var rg16FData = new float[textureWidth * textureHeight * 2];
            var r8Data = new byte[textureWidth * textureHeight];

            rgba32FData[0] = 1.0f;
            rgba32FData[1] = 2.0f;
            rgba32FData[2] = 5.0f;
            rgba32FData[3] = 0.0f;

            rgba32FData[4] = (float)faceCount;
            rgba32FData[5] = 0.0f;
            rgba32FData[6] = 0.0f;
            rgba32FData[7] = 0.0f;

            for (int i = 8; i < 16; i++)
            {
                rgba32FData[i] = 0.0f;
            }

            int dataOffset = 16;
            int highPrecisionOffset = 0;

            for (int faceIndex = 0; faceIndex < Math.Min(faceCount, 5); faceIndex++)
            {
                float bboxX = FaceData.Value[$"face_{faceIndex}_bbox_x"];
                float bboxY = FaceData.Value[$"face_{faceIndex}_bbox_y"];
                float bboxWidth = FaceData.Value[$"face_{faceIndex}_bbox_width"];
                float bboxHeight = FaceData.Value[$"face_{faceIndex}_bbox_height"];

                rgba32FData[dataOffset + 0] = bboxX + bboxWidth * 0.5f;
                rgba32FData[dataOffset + 1] = bboxY + bboxHeight * 0.5f;
                rgba32FData[dataOffset + 2] = bboxWidth;
                rgba32FData[dataOffset + 3] = bboxHeight;
                dataOffset += 4;

                for (int landmarkIndex = 0; landmarkIndex < 478; landmarkIndex++)
                {
                    int pointIndex = faceIndex * 478 + landmarkIndex;
                    if (pointIndex < landmarks.Length)
                    {
                        var point = landmarks[pointIndex];

                        rgba32FData[dataOffset + 0] = point.Position.X;
                        rgba32FData[dataOffset + 1] = point.Position.Y;
                        rgba32FData[dataOffset + 2] = point.Position.Z;
                        rgba32FData[dataOffset + 3] = 1.0f;
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

            for (int i = 0; i < r8Data.Length; i++)
            {
                r8Data[i] = 0;
            }

            for (int faceIndex = 0; faceIndex < Math.Min(faceCount, 5); faceIndex++)
            {
                float bboxX = FaceData.Value[$"face_{faceIndex}_bbox_x"];
                float bboxY = FaceData.Value[$"face_{faceIndex}_bbox_y"];
                float bboxWidth = FaceData.Value[$"face_{faceIndex}_bbox_width"];
                float bboxHeight = FaceData.Value[$"face_{faceIndex}_bbox_height"];

                int pixelX = (int)(bboxX * textureWidth);
                int pixelY = (int)(bboxY * textureHeight);
                int pixelWidth = (int)(bboxWidth * textureWidth);
                int pixelHeight = (int)(bboxHeight * textureHeight);

                for (int y = Math.Max(0, pixelY); y < Math.Min(textureHeight, pixelY + pixelHeight); y++)
                {
                    for (int x = Math.Max(0, pixelX); x < Math.Min(textureWidth, pixelX + pixelWidth); x++)
                    {
                        int index = y * textureWidth + x;
                        if (index < r8Data.Length)
                        {
                            r8Data[index] = (byte)(faceIndex + 1);
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
        #endregion

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                _disposed = true;
                StopWorker();

                // The reaper owns the native instance now; give a running inference a moment to
                // return, but never hang shutdown on a wedged native library.
                _reaper.WaitForPending(2000);

                _debugTexture?.Dispose();
                _debugTexture = null;

                lock (_bufferLock)
                {
                    _landmarkBuffer?.Dispose();
                    _landmarkBuffer = null;
                }


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

                lock (_landmarksLock)
                {
                }

                lock (_aiTextureLock)
                {
                    _aiDataTexture?.Dispose();
                    _aiDataTexture = null;
                    _aiDataHighPrecisionTexture?.Dispose();
                    _aiDataHighPrecisionTexture = null;
                    _aiDataSegmentationTexture?.Dispose();
                    _aiDataSegmentationTexture = null;
                }
            }
            base.Dispose(isDisposing);
        }

        [Input(Guid = "90abcdef-1234-5678-90ab-cdef12345678")]
        public readonly InputSlot<Texture2D> InputTexture = new();

        [Input(Guid = "abcdef12-3456-7890-abcd-ef1234567890")]
        public readonly InputSlot<bool> Enabled = new(true);

        [Input(Guid = "567890ab-cdef-1234-5678-90abcdef1234")]
        public readonly InputSlot<int> MaxFaces = new(1);

        [Input(Guid = "cdef1234-5678-90ab-cdef-1234567890ab")]
        public readonly InputSlot<bool> ShowLandmarks = new(true);

        [Input(Guid = "34567890-abcd-ef12-3456-7890abcdef12")]
        public readonly InputSlot<float> LandmarkSize = new(3.0f);

        [Input(Guid = "7890abcd-ef12-3456-7890-abcdef123456")]
        public readonly InputSlot<Vector4> LandmarkColor = new(new(1, 1, 1, 1));

        [Input(Guid = "bcdef123-4567-890a-bcde-f1234567890a")]
        public readonly InputSlot<float> LandmarkScale = new(1.0f);

        [Input(Guid = "f1234567-890a-bcde-f123-4567890abcde")]
        public readonly InputSlot<bool> NormalizeLandmarks = new();

        [Input(Guid = "23456789-0abc-def1-2345-67890abcdef1")]
        public readonly InputSlot<float> MinFaceDetectionConfidence = new(0.5f);

        [Input(Guid = "67890abc-def1-2345-6789-0abcdef12345")]
        public readonly InputSlot<float> MinFacePresenceConfidence = new(0.5f);

        [Input(Guid = "0abcdef1-2345-6789-0abc-def123456789")]
        public readonly InputSlot<float> MinTrackingConfidence = new(0.5f);

        [Input(Guid = "4567890a-bcde-f123-4567-890abcdef123")]
        public readonly InputSlot<bool> Debug = new(false);

        [Input(Guid = "890abcde-f123-4567-890a-bcdef1234567")]
        public readonly InputSlot<bool> UseCombinedAIOutput = new(false);

        [Input(Guid = "cdef0123-4567-890a-bcde-f01234567890")]
        public readonly InputSlot<bool> CorrectAspectRatio = new(false);

        /// <summary>Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere),
        /// Xnnpack = multithreaded CPU (the fast default; see InferenceThreads), Gpu = OpenGL/EGL
        /// delegate (Linux native has it; the CPU-only Windows/macOS natives fall back to Cpu).</summary>
        [Input(Guid = "a1b2c3d4-0003-4a9b-8c2d-101010101010")]
        public readonly InputSlot<MediaPipeAcceleration> Acceleration = new(MediaPipeAcceleration.Xnnpack);

        /// <summary>Worker threads for the Xnnpack acceleration (0 = library default, which is effectively
        /// single-threaded). 4 is ~2x faster than the default on a 20-core machine; use 2 when
        /// several MediaPipe operators run at once. Ignored by Cpu and Gpu.</summary>
        [Input(Guid = "a1b2c3d4-1003-4a9b-8c2d-101010101010")]
        public readonly InputSlot<int> InferenceThreads = new(DefaultInferenceThreads);

        [Input(Guid = "01234567-890a-bcde-f012-34567890abcd")]
        public readonly InputSlot<float> ZScale = new(1.0f);
    }
}
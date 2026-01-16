#nullable enable

using System.Threading;
using Mediapipe;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Image = Mediapipe.Framework.Formats.Image;

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
                StopWorker(debug);
                ClearOutputs();
                return;
            }

            OutputTexture.Value = inputTexture;

            if (_processingTask == null || _processingTask.IsCompleted || _faceLandmarker == null || 
                maxFaces != _activeMaxFaces || 
                Math.Abs(minFaceDetectionConfidence - _activeMinFaceDetectionConfidence) > 0.001f ||
                Math.Abs(minFacePresenceConfidence - _activeMinFacePresenceConfidence) > 0.001f ||
                Math.Abs(minTrackingConfidence - _activeMinTrackingConfidence) > 0.001f)
            {
                InitializeWorker(maxFaces, minFaceDetectionConfidence, minFacePresenceConfidence, minTrackingConfidence, debug);
            }

            if (_faceLandmarker == null)
            {
                ClearOutputs();
                return;
            }

            if (_inputQueue.Count < 1)
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
                if (result.FaceCount > 0)
                {
                    _currentResult = result;
                }
            }

            if (_currentResult != null)
            {
                UpdateOutputsWithProcessing(_currentResult, scale, normalize, showLandmarks, aspectRatio, zScale, useCombinedAIOutput);
                
                if (debug)
                {
                    using var mat = Texture2DToMat(inputTexture);
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
            _landmarksArray = null;
            _currentResult = null;
            PointBuffer.Value = null;
            Blendshapes.Value = new Dict<float>(0f);
        }

        #region Worker Thread
        private FaceLandmarker? _faceLandmarker;
        private Task? _processingTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly ConcurrentQueue<FaceLandmarkRequest> _inputQueue = new();
        private readonly ConcurrentQueue<FaceLandmarkResultPacket> _outputQueue = new();
        private FaceLandmarkResultPacket? _currentResult;
        private Point[]? _landmarksArray;
        
        private int _activeMaxFaces = -1;
        private float _activeMinFaceDetectionConfidence = -1f;
        private float _activeMinFacePresenceConfidence = -1f;
        private float _activeMinTrackingConfidence = -1f;
        
        private long _frameCounter = 0;
        private readonly object _timestampLock = new object();
        private readonly object _faceLandmarkerLock = new object();
        private readonly object _landmarksLock = new object();
        
        private readonly ConcurrentDictionary<(int width, int height), SharpDX.Direct3D11.Texture2D> _cachedStagingTextures = new();
        private readonly object _textureCacheLock = new object();
        
        private readonly ConcurrentBag<Mat> _matPool = new();
        private readonly ConcurrentBag<byte[]> _bufferPool = new();
        private readonly ConcurrentDictionary<int, ConcurrentBag<Point[]>> _pointArrayPool = new();
        private readonly object _poolLock = new object();
        private readonly object _workerLock = new object();

        private void InitializeWorker(int maxFaces, float minFaceDetectionConfidence, float minFacePresenceConfidence, float minTrackingConfidence, bool debug)
        {
            StopWorker(debug);
            _activeMaxFaces = maxFaces;
            _activeMinFaceDetectionConfidence = minFaceDetectionConfidence;
            _activeMinFacePresenceConfidence = minFacePresenceConfidence;
            _activeMinTrackingConfidence = minTrackingConfidence;

            try
            {
                lock (_workerLock)
                {
                    string modelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "mediapipe", "face_landmarker.task"));
                    if (!File.Exists(modelPath))
                    {
                        string[] possiblePaths = {
                            "../../../../Operators/Mediapipe/Resources/face_landmarker.task"
                        };
                        foreach (var p in possiblePaths)
                        {
                            var abs = Path.GetFullPath(p);
                            if (File.Exists(abs))
                            {
                                modelPath = abs;
                                break;
                            }
                        }
                    }

                    if (!File.Exists(modelPath))
                    {
                        if (debug) Log.Error($"[FaceLandmark] Model not found: {modelPath}", this);
                        return;
                    }

                    var baseOptions = new CoreBaseOptions(modelAssetPath: modelPath, delegateCase: CoreBaseOptions.Delegate.CPU);
                    var options = new FaceLandmarkerOptions(
                        baseOptions: baseOptions,
                        runningMode: VisionRunningMode.VIDEO,
                        numFaces: _activeMaxFaces,
                        minFaceDetectionConfidence: _activeMinFaceDetectionConfidence,
                        minFacePresenceConfidence: _activeMinFacePresenceConfidence,
                        minTrackingConfidence: _activeMinTrackingConfidence,
                        outputFaceBlendshapes: true,
                        outputFaceTransformationMatrixes: false
                    );

                    lock (_faceLandmarkerLock)
                    {
                        _faceLandmarker = FaceLandmarker.CreateFromOptions(options);
                    }
                    _cancellationTokenSource = new CancellationTokenSource();
                    var token = _cancellationTokenSource.Token;
                    _processingTask = Task.Run(() => WorkerLoop(token, debug), token);
                }
            }
            catch (Exception ex)
            {
                if (debug) Log.Error($"[FaceLandmark] Init Failed: {ex.Message}", this);
                lock (_faceLandmarkerLock)
                {
                    _faceLandmarker = null;
                }
            }
        }

        private void WorkerLoop(CancellationToken token, bool debug)
        {
            while (!token.IsCancellationRequested)
            {
                if (_inputQueue.TryDequeue(out var request))
                {
                    try
                    {
                        if (_faceLandmarker != null && request.PixelData != null)
                        {
                            ProcessFrame(request, debug);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debug) Log.Error($"[FaceLandmark] Worker error: {ex.Message}", this);
                    }
                    finally
                    {
                        ReturnBufferToPool(request.PixelData);
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        private void ProcessFrame(FaceLandmarkRequest request, bool debug)
        {
            using var image = new Image(ImageFormat.Types.Format.Srgba, request.Width, request.Height, request.Width * 4, request.PixelData!);
            
            FaceLandmarker? landmarker;
            lock (_faceLandmarkerLock)
            {
                landmarker = _faceLandmarker;
            }
            
            var result = landmarker?.DetectForVideo(image, request.Timestamp);

            if (result != null && result.Value.FaceLandmarks != null && result.Value.FaceLandmarks.Count > 0)
            {
                var packet = new FaceLandmarkResultPacket
                {
                    FaceCount = result.Value.FaceLandmarks.Count,
                    Landmarks = RawToPoints(result.Value),
                    FaceData = CalculateRawBounds(result.Value),
                    Blendshapes = CalculateBlendshapes(result.Value)
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
                    Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm, // Mat is BGRA
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                    OptionFlags = ResourceOptionFlags.None
                };
                _debugTexture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, desc));
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
            device.ImmediateContext.CopyResource(texture, stagingTexture);
 
            var dataBox = device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);
            if (dataBox.DataPointer == IntPtr.Zero)
            {
                device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                return new Mat();
            }
 
            var mat = GetMat(desc.Height, desc.Width, MatType.CV_8UC4);
            try
            {
                Utilities.CopyMemory(mat.Data, dataBox.DataPointer, (int)mat.Total() * mat.ElemSize());
            }
            finally
            {
                device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
            }
 
            return mat;
        }
        #endregion
    
        private Point[] RawToPoints(FaceLandmarkerResult result)
        {
            var pointsList = new List<Point>();
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

                    pointsList.Add(new Point { Position = new Vector3(centeredX, centeredY, z), F1 = i, Orientation = Quaternion.Identity });
                }
            }
            return pointsList.ToArray();
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
                    string key = $"face_{faceIndex}_blendshape_{category.CategoryName}";
                    dict[key] = category.Score;
                }
            }
            
            return dict;
        }

        private void StopWorker(bool debug)
        {
            lock (_workerLock)
            {
                _cancellationTokenSource?.Cancel();
                try
                {
                    if (_processingTask != null && !_processingTask.IsCompleted)
                    {
                        _processingTask.Wait(200);
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[FaceLandmark] Error waiting for worker task: {ex.Message}", this);
                }
                
                try
                {
                    lock (_faceLandmarkerLock)
                    {
                        _faceLandmarker?.Close();
                        _faceLandmarker = null;
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[FaceLandmark] Error closing FaceLandmarker: {ex.Message}", this);
                }
                
                while (_inputQueue.TryDequeue(out var req))
                {
                    ReturnBufferToPool(req.PixelData);
                }
                while (_outputQueue.TryDequeue(out _)) { }
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
                _landmarksArray = finalPoints;
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
        private SharpDX.Direct3D11.Texture2D GetOrCreateStagingTexture(int width, int height, SharpDX.DXGI.Format format)
        {
            var key = (width, height);
            
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
                var newTexture = new SharpDX.Direct3D11.Texture2D(device, new Texture2DDescription
                {
                    Width = width, Height = height, MipLevels = 1, ArraySize = 1,
                    Format = format, SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
                });
                
                _cachedStagingTextures[key] = newTexture;
                return newTexture;
            }
        }
        
        private Mat GetMat(int rows, int cols, MatType type)
        {
            if (_matPool.TryTake(out var mat))
            {
                if (mat.Rows == rows && mat.Cols == cols && mat.Type() == type)
                {
                    return mat;
                }
                else
                {
                    mat.Dispose();
                }
            }
            return new Mat(rows, cols, type);
        }
        
        private void ReturnMatToPool(Mat? mat)
        {
            if (mat != null && !mat.IsDisposed)
            {
                _matPool.Add(mat);
            }
        }
        
        private byte[] GetBuffer(int size)
        {
            if (_bufferPool.TryTake(out var b) && b.Length == size) return b;
            return new byte[size];
        }

        private void ReturnBufferToPool(byte[]? b)
        {
            if (b != null) _bufferPool.Add(b);
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

        private FaceLandmarkRequest? CreateRequestFromTexture(Texture2D texture)
        {
            var device = ResourceManager.Device;
            var desc = texture.Description;
            int width = desc.Width;
            int height = desc.Height;

            var stagingTexture = GetOrCreateStagingTexture(width, height, desc.Format);

            device.ImmediateContext.CopyResource(texture, stagingTexture);
            var dataBox = device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);
            
            if (dataBox.DataPointer == IntPtr.Zero)
            {
                device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
                return null;
            }

            try
            {
                byte[] buffer = GetBuffer(width * height * 4);
                unsafe
                {
                    byte* src = (byte*)dataBox.DataPointer;
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)src, buffer, 0, width * height * 4);
                    
                    Parallel.For(0, width * height, i =>
                    {
                        int idx = i * 4;
                        byte b = buffer[idx];
                        byte r = buffer[idx + 2];
                        buffer[idx] = r;
                        buffer[idx + 2] = b;
                    });
                }

                long ts;
                lock(_timestampLock) { ts = _frameCounter; _frameCounter += 33333; }

                return new FaceLandmarkRequest { PixelData = buffer, Width = width, Height = height, Timestamp = ts };
            }
            finally
            {
                device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
            }
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
                    if (_landmarkBuffer == null || _landmarkBuffer.Buffer.Description.SizeInBytes != sizeInBytes)
                    {
                        _landmarkBuffer?.Dispose();
                        _landmarkBuffer = new BufferWithViews();
                        ResourceManager.SetupStructuredBuffer(landmarks, sizeInBytes, Point.Stride, ref _landmarkBuffer.Buffer);
                        ResourceManager.CreateStructuredBufferSrv(_landmarkBuffer.Buffer, ref _landmarkBuffer.Srv);
                        ResourceManager.CreateStructuredBufferUav(_landmarkBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _landmarkBuffer.Uav);
                    }
                    else
                    {
                        ResourceManager.Device.ImmediateContext.UpdateSubresource(landmarks, _landmarkBuffer.Buffer);
                    }
                }
                catch (Exception ex)
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

                    CreateOrUpdateTexture(ref _aiDataTexture, textureWidth, textureHeight, SharpDX.DXGI.Format.R32G32B32A32_Float);
                    CreateOrUpdateTexture(ref _aiDataHighPrecisionTexture, textureWidth, textureHeight, SharpDX.DXGI.Format.R16G16_Float);
                    CreateOrUpdateTexture(ref _aiDataSegmentationTexture, textureWidth, textureHeight, SharpDX.DXGI.Format.R8_UNorm);

                    FillAITextures(landmarks, faceCount, textureWidth, textureHeight);
                }
                catch (Exception ex)
                {
                }
            }
        }

        private void CreateOrUpdateTexture(ref Texture2D? texture, int width, int height, SharpDX.DXGI.Format format)
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
                
                texture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, desc));
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
                StopWorker(false);
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
                
                foreach (var mat in _matPool)
                {
                    mat?.Dispose();
                }
                _matPool.Clear();
                
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
                    _landmarksArray = null;
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

        [Input(Guid = "01234567-890a-bcde-f012-34567890abcd")]
        public readonly InputSlot<float> ZScale = new(1.0f);

        private Resource? _stagingTexture;
    }
}
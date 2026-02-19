using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
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
    public class FaceDetection : Instance<FaceDetection>
    {
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
                StopWorker(debug);
                ClearOutputs();
                return;
            }

            OutputTexture.Value = inputTexture;

            if (_processingTask == null || _processingTask.IsCompleted || _faceDetector == null ||
                maxFaces != _activeMaxFaces || Math.Abs(confidenceThreshold - _activeConfidenceThreshold) > 0.001f)
            {
                InitializeWorker(maxFaces, confidenceThreshold, debug);
            }

            if (_faceDetector == null)
            {
                ClearOutputs();
                return;
            }

            if (_inputQueue.Count < 1)
            {
                var request = CreateRequestFromTexture(inputTexture, confidenceThreshold, maxFaces, correctAspectRatio, zScale);
                if (request != null)
                {
                    _inputQueue.Enqueue(request);
                }
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
                UpdateOutputsWithResult(_currentResult, showDetections, showKeypoints, detectionSize, detectionColor, keypointColor, useCombinedAiOutput, debug);
                
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
                PointBuffer.Value = null!;
                DebugTexture.Value = null!;
            }
        }

        private void ClearOutputs()
        {
            PointBuffer.Value = null!;
            FaceData.Value = new Dict<float>(0f);
            FaceCount.Value = 0;
            _currentResult = null;
            DebugTexture.Value = null!;
        }

        #region MediaPipe Integration
        private Mediapipe.Tasks.Vision.FaceDetector.FaceDetector? _faceDetector;
        private long _frameTimestamp;
        private readonly object _faceDetectorLock = new object();
        private readonly object _timestampLock = new object();
        
        private Task? _processingTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly ConcurrentQueue<FaceDetectionRequest> _inputQueue = new();
        private readonly ConcurrentQueue<FaceDetectionResultPacket> _outputQueue = new();
        private FaceDetectionResultPacket? _currentResult;
        private int _activeMaxFaces = -1;
        private float _activeConfidenceThreshold = -1f;
        private readonly object _workerLock = new object();
        
        private readonly ConcurrentDictionary<(int width, int height), SharpDX.Direct3D11.Texture2D> _cachedStagingTextures = new();
        private readonly object _textureCacheLock = new object();
        
        private readonly ConcurrentBag<Mat> _matPool = new();
        private readonly ConcurrentBag<byte[]> _bufferPool = new();
        private readonly ConcurrentDictionary<int, ConcurrentBag<Point[]>> _pointArrayPool = new();
        private readonly object _poolLock = new object();

        #endregion
        
        #region Worker Thread
        private void InitializeWorker(int maxFaces, float confidence, bool debug)
        {
            StopWorker(debug);
            _activeMaxFaces = maxFaces;
            _activeConfidenceThreshold = confidence;

            try
            {
                lock (_workerLock)
                {
                    var modelFound = AssetRegistry.TryResolveAddress("Mediapipe:blaze_face_short_range.tflite", this, out var fullPath, out _);
                    
                    // string modelPath = "../../../../Operators/Mediapipe/Resources/blaze_face_short_range.tflite";
                    // string fullPath = System.IO.Path.GetFullPath(modelPath);
                    //
                    // string[] possibleModelPaths = {
                    //     fullPath,
                    //     System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "mediapipe", "blaze_face_short_range.tflite"),
                    //     System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "blaze_face_short_range.tflite"),
                    //     System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "blaze_face_short_range.tflite"),
                    //     System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Models", "blaze_face_short_range.tflite"),
                    //     "../../Mediapipe-Sharp/src/Mediapipe/Models/blaze_face_short_range.tflite"
                    // };
                    
                    // bool modelFound = false;
                    // foreach (string path in possibleModelPaths)
                    // {
                    //     if (System.IO.File.Exists(path))
                    //     {
                    //         fullPath = System.IO.Path.GetFullPath(path);
                    //         modelFound = true;
                    //         break;
                    //     }
                    // }
                    
                    if (!modelFound)
                    {
                        if (debug) Log.Error($"[FaceDetection] Model not found: {fullPath}", this);
                        return;
                    }

                    var baseOptions = new Mediapipe.Tasks.Core.CoreBaseOptions(
                        modelAssetPath: fullPath,
                        delegateCase: Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU
                    );

                    FaceDetectorOptions options = new(
                        baseOptions,
                        VisionRunningMode.VIDEO,
                        minDetectionConfidence: 0.5f,
                        minSuppressionThreshold: 0.3f
                    );

                    lock (_faceDetectorLock)
                    {
                        _faceDetector = Mediapipe.Tasks.Vision.FaceDetector.FaceDetector.CreateFromOptions(options);
                    }
                    _cancellationTokenSource = new CancellationTokenSource();
                    var token = _cancellationTokenSource.Token;
                    _processingTask = Task.Run(() => WorkerLoop(token, debug), token);
                }
            }
            catch (Exception ex)
            {
                if (debug) Log.Error($"[FaceDetection] Init Failed: {ex.Message}", this);
                lock (_faceDetectorLock)
                {
                    _faceDetector = null;
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
                        if (_faceDetector != null && request.PixelData != null)
                        {
                            ProcessFrame(request, debug);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debug) Log.Error($"[FaceDetection] Worker error: {ex.Message}", this);
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

        private void ProcessFrame(FaceDetectionRequest request, bool debug)
        {
            using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);
            
            Mediapipe.Tasks.Vision.FaceDetector.FaceDetector? detector;
            lock (_faceDetectorLock)
            {
                detector = _faceDetector;
            }
            
            var result = detector?.DetectForVideo(image, request.Timestamp);

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

            var points = new List<Point>();

            float aspectRatio = correctAspectRatio ? (float)imageWidth / imageHeight : 1.0f;

            try
            {
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
                        
                        var bboxPoint1 = new Point
                        {
                            Position = new Vector3(((float)bbox.Left / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Top / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 0,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                        points.Add(bboxPoint1);
                        
                        var bboxPoint2 = new Point
                        {
                            Position = new Vector3(((float)bbox.Right / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Top / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 1,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                        points.Add(bboxPoint2);
                        
                        var bboxPoint3 = new Point
                        {
                            Position = new Vector3(((float)bbox.Right / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Bottom / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 2,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                        points.Add(bboxPoint3);
                        
                        var bboxPoint4 = new Point
                        {
                            Position = new Vector3(((float)bbox.Left / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - (float)bbox.Bottom / imageHeight * 2.0f, 0),
                            F1 = detectedFaces * 10 + 3,
                            F2 = confidence,
                            Color = Vector4.One,
                            Scale = Vector3.One,
                            Orientation = Quaternion.Identity
                        };
                        points.Add(bboxPoint4);
                        
                        if (detection.Keypoints != null && detection.Keypoints.Count >= 6)
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                var keypoint = detection.Keypoints[i];
                                var keypointPoint = new Point
                                {
                                    Position = new Vector3((keypoint.X * 2.0f - 1.0f) * aspectRatio, 1.0f - keypoint.Y * 2.0f, 0),
                                    F1 = detectedFaces * 10 + 4 + i,
                                    F2 = confidence,
                                    Color = Vector4.One,
                                    Scale = Vector3.One,
                                    Orientation = Quaternion.Identity
                                };
                                points.Add(keypointPoint);
                            }
                        }
                        else
                        {
                            var centerX = bbox.Left + (bbox.Right - bbox.Left) * 0.5f;
                            var centerY = bbox.Top + (bbox.Bottom - bbox.Top) * 0.5f;
                            for (int i = 0; i < 6; i++)
                            {
                                var placeholderPoint = new Point
                                {
                                    Position = new Vector3((centerX / imageWidth * 2.0f - 1.0f) * aspectRatio, 1.0f - centerY / imageHeight * 2.0f, 0),
                                    F1 = detectedFaces * 10 + 4 + i,
                                    F2 = confidence,
                                    Color = Vector4.One,
                                    Scale = Vector3.One,
                                    Orientation = Quaternion.Identity
                                };
                                points.Add(placeholderPoint);
                            }
                        }
                    }

                    detectedFaces++;
                }

                return points.Count > 0 ? points.ToArray() : null;
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

        private void StopWorker(bool debug)
        {
            lock (_workerLock)
            {
                _cancellationTokenSource?.Cancel();
                try
                {
                    if (_processingTask != null && !_processingTask.IsCompleted)
                    {
                        if (!_processingTask.Wait(1000))
                        {
                            if (debug) Log.Warning("[FaceDetection] THREAD SAFETY: Worker task did not complete within timeout", this);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[FaceDetection] THREAD SAFETY: Error waiting for worker task: {ex.Message}", this);
                }
                
                try
                {
                    lock (_faceDetectorLock)
                    {
                        _faceDetector?.Close();
                        _faceDetector = null;
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[FaceDetection] RESOURCE MANAGEMENT: Error closing FaceDetector: {ex.Message}", this);
                }
                
                while (_inputQueue.TryDequeue(out var req))
                {
                    ReturnBufferToPool(req.PixelData);
                }
                while (_outputQueue.TryDequeue(out _)) { }
            }
        }
    #endregion Worker Thread

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

        private FaceDetectionRequest? CreateRequestFromTexture(Texture2D texture, float confidenceThreshold, int maxFaces, bool correctAspectRatio, float zScale)
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
                device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
                return null;
            }

            try
            {
                byte[] buffer = GetBuffer(width * height * 3);
                unsafe
                {
                    IntPtr srcPtr = dataBox.DataPointer;
                    int rowPitch = dataBox.RowPitch;
                    
                    fixed (byte* dst = buffer)
                    {
                        IntPtr dstPtr = (IntPtr)dst;
                        Parallel.For(0, height, y =>
                        {
                            byte* rowSrc = (byte*)srcPtr + (y * rowPitch);
                            byte* rowDst = (byte*)dstPtr + (y * width * 3);
                            
                            for (int x = 0; x < width; x++)
                            {
                                rowDst[x * 3] = rowSrc[x * 4 + 2];     // R
                                rowDst[x * 3 + 1] = rowSrc[x * 4 + 1]; // G
                                rowDst[x * 3 + 2] = rowSrc[x * 4];     // B
                            }
                        });
                    }
                }

                long ts;
                lock(_timestampLock) { ts = _frameTimestamp; _frameTimestamp += 33333; }

                return new FaceDetectionRequest 
                { 
                    PixelData = buffer, 
                    Width = width, 
                    Height = height, 
                    Timestamp = ts,
                    ConfidenceThreshold = confidenceThreshold,
                    MaxFaces = maxFaces,
                    CorrectAspectRatio = correctAspectRatio,
                    ZScale = zScale
                };
            }
            finally
            {
                device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
            }
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

        private void DrawDebugVisuals(Mat mat, FaceDetectionResultPacket result)
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
                
                int x1 = (int)((p1.Position.X + 1.0f) / 2.0f * mat.Width);
                int y1 = (int)((1.0f - p1.Position.Y) / 2.0f * mat.Height);
                int x2 = (int)((p3.Position.X + 1.0f) / 2.0f * mat.Width);
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
                    int kpX = (int)((kp.Position.X + 1.0f) / 2.0f * mat.Width);
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
                    Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
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
                    if (_detectionBuffer == null || _detectionBuffer.Buffer.Description.SizeInBytes != newSize)
                    {
                        _detectionBuffer?.Dispose();
                        
                        if (filteredArray.Length > 0)
                        {
                            _detectionBuffer = new BufferWithViews();
                            ResourceManager.SetupStructuredBuffer(filteredArray,
                                newSize,
                                Point.Stride,
                                ref _detectionBuffer.Buffer);
                            ResourceManager.CreateStructuredBufferSrv(_detectionBuffer.Buffer, ref _detectionBuffer.Srv);
                            ResourceManager.CreateStructuredBufferUav(_detectionBuffer.Buffer,
                                UnorderedAccessViewBufferFlags.None,
                                ref _detectionBuffer.Uav);
                        }
                    }
                    else if (filteredArray.Length > 0)
                    {
                        ResourceManager.Device.ImmediateContext.UpdateSubresource(filteredArray, _detectionBuffer.Buffer);
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
                try
                {
                    lock (_faceDetectorLock)
                    {
                        _faceDetector?.Close();
                        _faceDetector = null;
                    }
                }
                catch (Exception)
                {
                }
                
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

        [Input(Guid = "A4B5C6D7-E8F9-60A1-12B3-456789012DE5")]
        public readonly InputSlot<float> ZScale = new(1.0f);
        #endregion
    }
}

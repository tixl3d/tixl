using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using Google.Protobuf;
using SharpDX.Direct3D;
#nullable enable

using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Framework.Formats;
using Landmark = Mediapipe.Landmark;
using Image = Mediapipe.Framework.Formats.Image;
using ImageFormat = Mediapipe.RegionFlowComputationOptions.Types.ImageFormat;

namespace Lib.io.video.mediapipe;

internal class HandLandmarkDetectionRequest
{
    public byte[]? PixelData;
    public int Width;
    public int Height;
    public long Timestamp;
}

internal class HandLandmarkDetectionResultPacket
{
    public Point[]? Landmarks;
    public int HandCount;
    public Point[]? WorldLandmarks;
}

[Guid("4567890a-bcde-f123-4567-890abcdef123")]
public class HandLandmarkDetection : Instance<HandLandmarkDetection>
{
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
        var inputTexture = InputTexture.GetValue(context);
        var enabled = Enabled.GetValue(context);
        var debug = Debug.GetValue(context);
        
        var minHandDetectionConfidence = MinHandDetectionConfidence.GetValue(context);
        var minHandPresenceConfidence = MinHandPresenceConfidence.GetValue(context);
        var minTrackingConfidence = MinTrackingConfidence.GetValue(context);
        var maxHands = MaxHands.GetValue(context);
        
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

        if (_processingTask == null || _processingTask.IsCompleted || _handLandmarker == null ||
            maxHands != _activeMaxHands ||
            Math.Abs(minHandDetectionConfidence - _activeMinHandDetectionConfidence) > 0.001f ||
            Math.Abs(minHandPresenceConfidence - _activeMinHandPresenceConfidence) > 0.001f ||
            Math.Abs(minTrackingConfidence - _activeMinTrackingConfidence) > 0.001f)
        {
            InitializeWorker(maxHands, minHandDetectionConfidence, minHandPresenceConfidence, minTrackingConfidence, debug);
        }

        if (_handLandmarker == null)
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

        while (_outputQueue.TryDequeue(out var result))
        {
            if (result.HandCount > 0)
            {
                if (_currentResult != null)
                {
                    ReturnPointArrayToPool(_currentResult.Landmarks);
                    ReturnPointArrayToPool(_currentResult.WorldLandmarks);
                }
                _currentResult = result;
            }
            else
            {
                ReturnPointArrayToPool(result.Landmarks);
                ReturnPointArrayToPool(result.WorldLandmarks);
            }
        }

        if (_currentResult != null)
        {
            UpdateOutputsWithResult(_currentResult, correctAspectRatio, zScale, inputTexture);

            if (debug)
            {
                using var mat = Texture2DToMat(inputTexture);
                if (!mat.Empty())
                {
                    DrawDebugVisualsFromLandmarks(mat, _currentResult.Landmarks, correctAspectRatio);
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
        
        DebugTexture.Value = null;
        WorldLandmarksBuffer.Value = null;
        PointBuffer.Value = null;
    }

    #region MediaPipe Integration
    private HandLandmarker? _handLandmarker;
    private Point[]? _landmarksArray;
    private long _frameTimestamp;
    private readonly object _handLandmarkerLock = new object();
    private readonly object _timestampLock = new object();
    private readonly object _landmarksLock = new object();

    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<HandLandmarkDetectionRequest> _inputQueue = new();
    private readonly ConcurrentQueue<HandLandmarkDetectionResultPacket> _outputQueue = new();
    private HandLandmarkDetectionResultPacket? _currentResult;
    
    private int _activeMaxHands = -1;
    private float _activeMinHandDetectionConfidence = -1f;
    private float _activeMinHandPresenceConfidence = -1f;
    private float _activeMinTrackingConfidence = -1f;
    
    private readonly object _workerLock = new object();
    
    private readonly ConcurrentDictionary<(int width, int height), SharpDX.Direct3D11.Texture2D> _cachedStagingTextures = new();
    private readonly object _textureCacheLock = new object();
    
    private readonly ConcurrentBag<Mat> _matPool = new();
    private readonly ConcurrentBag<byte[]> _bufferPool = new();
    private readonly ConcurrentDictionary<int, ConcurrentBag<Point[]>> _pointArrayPool = new();
    private readonly object _poolLock = new object();

    #endregion
    
    #region Worker Thread
    private void InitializeWorker(int maxHands, float minHandDetectionConfidence, float minHandPresenceConfidence, float minTrackingConfidence, bool debug)
    {
        StopWorker(debug);
        _activeMaxHands = maxHands;
        _activeMinHandDetectionConfidence = minHandDetectionConfidence;
        _activeMinHandPresenceConfidence = minHandPresenceConfidence;
        _activeMinTrackingConfidence = minTrackingConfidence;

        try
        {
            lock (_workerLock)
            {
                string modelPath = "../../../../Operators/Mediapipe/Resources/hand_landmarker.task";
                string fullPath = System.IO.Path.GetFullPath(modelPath);

                string[] possibleModelPaths = {
                    fullPath,
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "hand_landmarker.task"),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Models", "hand_landmarker.task"),
                    "../../Mediapipe-Sharp/src/Mediapipe/Models/hand_landmarker.task",
                    "../../../Mediapipe-Sharp/src/Mediapipe/Models/hand_landmarker.task"
                };

                bool modelFound = false;
                foreach (string path in possibleModelPaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        fullPath = System.IO.Path.GetFullPath(path);
                        modelFound = true;
                        break;
                    }
                }

                if (!modelFound)
                {
                    if (debug) Log.Error($"[HandLandmarkDetection] Model not found: {fullPath}", this);
                    return;
                }

                var baseOptions = new Mediapipe.Tasks.Core.CoreBaseOptions(
                    modelAssetPath: fullPath,
                    delegateCase: Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU
                );

                HandLandmarkerOptions options = new(
                    baseOptions,
                    VisionRunningMode.VIDEO,
                    maxHands,
                    minHandDetectionConfidence,
                    minHandPresenceConfidence,
                    minTrackingConfidence
                );

                lock (_handLandmarkerLock)
                {
                    _handLandmarker = HandLandmarker.CreateFromOptions(options);
                }
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;
                _processingTask = Task.Run(() => WorkerLoop(token, debug), token);
            }
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[HandLandmarkDetection] Init Failed: {ex.Message}", this);
            lock (_handLandmarkerLock)
            {
                _handLandmarker = null;
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
                    if (_handLandmarker != null && request.PixelData != null)
                    {
                        ProcessFrame(request, debug);
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[HandLandmarkDetection] Worker error: {ex.Message}", this);
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

    private void ProcessFrame(HandLandmarkDetectionRequest request, bool debug)
    {
        using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        HandLandmarker? landmarker;
        lock (_handLandmarkerLock)
        {
            landmarker = _handLandmarker;
        }

        var result = landmarker?.DetectForVideo(image, request.Timestamp);

        if (result != null && result.Value.HandLandmarks != null && result.Value.HandLandmarks.Count > 0)
        {
            var landmarks = ConvertHandLandmarkerResultToLandmarks(result.Value, _activeMaxHands);
            var worldLandmarks = ConvertHandLandmarkerResultToWorldLandmarks(result.Value, _activeMaxHands);
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
                if (debug) Log.Error($"[HandLandmarkDetection] Error waiting for worker task: {ex.Message}", this);
            }

            try
            {
                lock (_handLandmarkerLock)
                {
                    _handLandmarker?.Close();
                    _handLandmarker = null;
                }
            }
            catch (Exception ex)
            {
                if (debug) Log.Error($"[HandLandmarkDetection] Error closing HandLandmarker: {ex.Message}", this);
            }

            while (_inputQueue.TryDequeue(out var req))
            {
                ReturnBufferToPool(req.PixelData);
            }
            while (_outputQueue.TryDequeue(out _)) { }
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

    private HandLandmarkDetectionRequest? CreateRequestFromTexture(Texture2D texture)
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

            return new HandLandmarkDetectionRequest
            {
                PixelData = buffer,
                Width = width,
                Height = height,
                Timestamp = ts
            };
        }
        finally
        {
            device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        }
    }
    #endregion

    private void UpdateOutputsWithResult(HandLandmarkDetectionResultPacket result, bool correctAspectRatio, float zScale, Texture2D inputTexture)
    {
        if (result.Landmarks == null || result.Landmarks.Length == 0)
        {
            HandCount.Value = 0;
            WorldLandmarksBuffer.Value = null;
            PointBuffer.Value = null;
            return;
        }

        HandCount.Value = result.HandCount;
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
        
        UpdateBuffer(result.WorldLandmarks, ref _worldLandmarksBufferWithViews, WorldLandmarksBuffer);
        UpdateBuffer(processedLandmarks, ref _pointBufferWithViews, PointBuffer);
    }

    private BufferWithViews? _worldLandmarksBufferWithViews;
    private BufferWithViews? _pointBufferWithViews;

    private void UpdateBuffer(Point[]? points, ref BufferWithViews? bufferWithViews, Slot<BufferWithViews> slot)
    {
        if (points == null || points.Length == 0)
        {
            slot.Value = null;
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
                         Format = SharpDX.DXGI.Format.Unknown,
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
                         Format = SharpDX.DXGI.Format.Unknown,
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

    private void DrawDebugVisualsFromLandmarks(Mat mat, Point[]? landmarks, bool correctAspectRatio)
    {
        if (landmarks == null || landmarks.Length == 0) return;

        var landmarkColor = new Scalar(0, 255, 0, 255);
        int matWidth = mat.Width;
        int matHeight = mat.Height;
        float aspectRatio = (float)matWidth / matHeight;

        for (int i = 0; i < landmarks.Length; i++)
        {
            var point = landmarks[i];
            
            float x = point.Position.X / 2.0f;
            if (correctAspectRatio) x /= aspectRatio;
            x += 0.5f;
            
            float y = 0.5f - (point.Position.Y / 2.0f);
            
            int px = (int)(x * matWidth);
            int py = (int)(y * matHeight);
            
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
                    for (int i = 0; i < handLandmarks.landmarks.Count && i < 21; i++)
                    {
                        var landmark = handLandmarks.landmarks[i];

                        var centeredX = landmark.X - 0.5f;
                        var centeredY = 0.5f - landmark.Y;
                        var z = -landmark.Z; // Flip Z to match FaceLandmarkDetection

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
        catch (Exception ex)
        {
            return null;
        }
    }

    private Point[]? ConvertHandLandmarkerResultToWorldLandmarks(HandLandmarkerResult result, int maxHands)
    {
        if (result.HandWorldLandmarks == null) return null;

        int totalLandmarks = 0;
        for (int handIndex = 0; handIndex < result.HandWorldLandmarks.Count && handIndex < maxHands; handIndex++)
        {
            var handLandmarks = result.HandWorldLandmarks[handIndex];
            if (handLandmarks.landmarks != null && handLandmarks.landmarks.Count > 0)
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

            return landmarks.Length > 0 ? landmarks.ToArray() : null;
        }
        catch (Exception ex)
        {
            ReturnPointArrayToPool(landmarks);
            return null;
        }
    }

    #region Cleanup
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        try
        {
            lock (_handLandmarkerLock)
            {
                _handLandmarker?.Close();
                _handLandmarker = null;
            }
        }
        catch (Exception ex)
        {
        }

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
            while (_matPool.TryTake(out var mat))
            {
                mat?.Dispose();
            }
            _matPool.Clear();
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

    [Input(Guid = "cdef1234-5678-90ab-cdef-1234567890ab")]
    public readonly InputSlot<float> ZScale = new(1.0f);
    #endregion
    
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
}

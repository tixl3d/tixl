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
using Mediapipe;
using SharpDX.Direct3D;
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
    public string[]? CategoryAllowlist;
    public string[]? CategoryDenylist;
}

internal class ObjectDetectionResultPacket
{
    public Detection[]? Detections;
    public int ObjectCount;
    public int ImageWidth;
    public int ImageHeight;
}

[Guid("12345678-90ab-cdef-1234-567890abcdef")]
public class ObjectDetection : Instance<ObjectDetection>
{
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
            StopWorker(debug);
            ClearOutputs();
            return;
        }

        OutputTexture.Value = inputTexture;

        bool paramsChanged = maxObjects != _activeMaxObjects || Math.Abs(minDetectionConfidence - _activeMinDetectionConfidence) > 0.001f;
        bool taskDead = _processingTask == null || _processingTask.IsCompleted;
        bool detectorMissing = _objectDetector == null;
        bool modelChanged = model != _activeModel;
        bool allowlistChanged = categoryAllowlist != _activeCategoryAllowlist;
        bool denylistChanged = categoryDenylist != _activeCategoryDenylist;

        if (taskDead || detectorMissing || paramsChanged || modelChanged || allowlistChanged || denylistChanged)
        {
            InitializeWorker(maxObjects, minDetectionConfidence, debug, model, categoryAllowlist!, categoryDenylist!);
        }

        if (_objectDetector == null)
        {
            ClearOutputs();
            return;
        }

        if (_inputQueue.Count < 1)
        {
            var request = CreateRequestFromTexture(inputTexture, minDetectionConfidence, maxObjects, categoryAllowlist!, categoryDenylist!);
            if (request != null)
            {
                _inputQueue.Enqueue(request);
            }
        }

        while (_outputQueue.TryDequeue(out var result))
        {
            if (result.ObjectCount > 0)
            {
                _currentResult = result;
            }
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
        _detectionsArray = null;
        _currentResult = null;
        DebugTexture.Value = null;
        PointBuffer.Value = null;
        ObjectData.Value = null!;
        ActiveCategories.Value = new List<string>();
    }

    #region MediaPipe Integration
    private ObjectDetector? _objectDetector;
    private Detection[]? _detectionsArray;
    private long _frameTimestamp;
    private readonly object _objectDetectorLock = new object();
    private readonly object _timestampLock = new object();

    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<ObjectDetectionRequest> _inputQueue = new();
    private readonly ConcurrentQueue<ObjectDetectionResultPacket> _outputQueue = new();
    private ObjectDetectionResultPacket? _currentResult;
    private int _activeMaxObjects = -1;
    private float _activeMinDetectionConfidence = -1f;
    private DetectionModel _activeModel;
    private string _activeCategoryAllowlist = "";
    private string _activeCategoryDenylist = "";
    private readonly object _workerLock = new object();

    private readonly ConcurrentDictionary<(int width, int height), SharpDX.Direct3D11.Texture2D> _cachedStagingTextures = new();
    private readonly object _textureCacheLock = new object();

    private readonly ConcurrentBag<Mat> _matPool = new();
    private readonly ConcurrentBag<byte[]> _bufferPool = new();
    private readonly ConcurrentDictionary<int, ConcurrentBag<Detection[]>> _detectionArrayPool = new();
    private readonly object _poolLock = new object();
    #endregion

    #region Worker Thread
    private void InitializeWorker(int maxObjects, float minDetectionConfidence, bool debug, DetectionModel model, string categoryAllowlist,
                                  string categoryDenylist)
    {
        StopWorker(debug);
        _activeMaxObjects = maxObjects;
        _activeMinDetectionConfidence = minDetectionConfidence;
        _activeModel = model;
        _activeCategoryAllowlist = categoryAllowlist;
        _activeCategoryDenylist = categoryDenylist;

        try
        {
            lock (_workerLock)
            {
                string modelName = model == DetectionModel.EfficientDetLite2
                                       ? "efficientdet_lite2.tflite"
                                       : "efficientdet_lite0.tflite";

                var modelFound = AssetRegistry.TryResolveAddress($"Mediapipe:{modelName}", this, out var fullPath, out _, logWarnings: true);

                // string modelPath = $"../../../../Operators/Mediapipe/Resources/{modelName}";
                // string fullPath = System.IO.Path.GetFullPath(modelPath);
                //
                // string[] possibleModelPaths = {
                //     fullPath,
                //     System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", modelName),
                //     System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Models", modelName),
                //     $"../../Mediapipe-Sharp/src/Mediapipe/Models/{modelName}",
                //     $"../../../Mediapipe-Sharp/src/Mediapipe/Models/{modelName}"
                // };
                //
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
                    if (debug) Log.Error($"[ObjectDetection] Model not found: {fullPath}", this);
                    return;
                }

                var baseOptions = new Mediapipe.Tasks.Core.CoreBaseOptions(
                                                                           modelAssetPath: fullPath,
                                                                           delegateCase: Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU
                                                                          );

                var allowList = string.IsNullOrEmpty(categoryAllowlist)
                                    ? null
                                    : categoryAllowlist.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                var denyList = string.IsNullOrEmpty(categoryDenylist)
                                   ? null
                                   : categoryDenylist.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();

                ObjectDetectorOptions options = new(
                                                    baseOptions,
                                                    VisionRunningMode.VIDEO,
                                                    maxResults: maxObjects,
                                                    scoreThreshold: minDetectionConfidence,
                                                    categoryAllowList: allowList,
                                                    categoryDenyList: denyList
                                                   );

                lock (_objectDetectorLock)
                {
                    _objectDetector = ObjectDetector.CreateFromOptions(options);
                }

                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;
                _processingTask = Task.Run(() => WorkerLoop(token, debug), token);
            }
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[ObjectDetection] Init Failed: {ex.Message}", this);
            lock (_objectDetectorLock)
            {
                _objectDetector = null;
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
                    if (_objectDetector != null && request.PixelData != null)
                    {
                        ProcessFrame(request, debug);
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[ObjectDetection] Worker error: {ex.Message}", this);
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

    private void ProcessFrame(ObjectDetectionRequest request, bool debug)
    {
        using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        ObjectDetector? detector;
        lock (_objectDetectorLock)
        {
            detector = _objectDetector;
        }

        var result = detector?.DetectForVideo(image, request.Timestamp);

        if (result != null && result.Value.Detections != null && result.Value.Detections.Count > 0)
        {
            var detections = ConvertDetectionResultToDetections(result.Value, request.MinDetectionConfidence, request.MaxObjects, debug);
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

    private void StopWorker(bool debug)
    {
        lock (_workerLock)
        {
            if (_objectDetector == null && (_processingTask == null || _processingTask.IsCompleted)) return;

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
                if (debug) Log.Error($"[ObjectDetection] THREAD SAFETY: Error waiting for worker task: {ex.Message}", this);
            }

            try
            {
                lock (_objectDetectorLock)
                {
                    _objectDetector?.Close();
                    _objectDetector = null;
                }
            }
            catch (Exception ex)
            {
                if (debug) Log.Error($"[ObjectDetection] RESOURCE MANAGEMENT: Error closing ObjectDetector: {ex.Message}", this);
            }

            while (_inputQueue.TryDequeue(out var req))
            {
                ReturnBufferToPool(req.PixelData);
            }

            while (_outputQueue.TryDequeue(out _))
            {
            }
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

            pool.Add(arr);
        }
    }

    private ObjectDetectionRequest? CreateRequestFromTexture(Texture2D texture, float minDetectionConfidence, int maxObjects, string categoryAllowlist,
                                                             string categoryDenylist)
    {
        if (texture == null || texture.IsDisposed) return null;

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
                                                    rowDst[x * 3] = rowSrc[x * 4 + 2]; // R
                                                    rowDst[x * 3 + 1] = rowSrc[x * 4 + 1]; // G
                                                    rowDst[x * 3 + 2] = rowSrc[x * 4]; // B
                                                }
                                            });
                }
            }

            long ts;
            lock (_timestampLock)
            {
                ts = _frameTimestamp;
                _frameTimestamp += 33333;
            }

            return new ObjectDetectionRequest
                       {
                           PixelData = buffer,
                           Width = width,
                           Height = height,
                           Timestamp = ts,
                           MinDetectionConfidence = minDetectionConfidence,
                           MaxObjects = maxObjects,
                           CategoryAllowlist = string.IsNullOrEmpty(categoryAllowlist) ? null : categoryAllowlist.Split(','),
                           CategoryDenylist = string.IsNullOrEmpty(categoryDenylist) ? null : categoryDenylist.Split(',')
                       };
        }
        finally
        {
            device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        }
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
        _detectionsArray = result.Detections;

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
                                                                           Format = SharpDX.DXGI.Format.Unknown,
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
                                                                            Format = SharpDX.DXGI.Format.Unknown,
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
            int x = (int)(bbox!.CenterX - bbox.Width / 2);
            int y = (int)(bbox.CenterY - bbox.Height / 2);
            int w = (int)(bbox.Width);
            int h = (int)(bbox.Height);

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

            return detections.Count > 0 ? detections.ToArray() : null;
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

        try
        {
            lock (_objectDetectorLock)
            {
                _objectDetector?.Close();
                _objectDetector = null;
            }
        }
        catch (Exception)
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
using System.Threading;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
#nullable enable

using Mediapipe.Tasks.Vision.ImageSegmenter;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Framework.Formats;
using Image = Mediapipe.Framework.Formats.Image;
// ReSharper disable InconsistentNaming

namespace Lib.io.video.mediapipe;

internal sealed class ImageSegmentationRequest
{
    public byte[]? PixelData;
    public int Width;
    public int Height;
    public long Timestamp;
    public string? SelectedCategories;
    public string[]? CategoryAllowlist;
}

internal sealed class ImageSegmentationResultPacket
{
    public byte[]? MaskData;
    public int Width;
    public int Height;
    public float Confidence;
    public byte[]? CategoryMaskData;
    public float[]? ConfidenceMaskData;
}

[Guid("23456789-0abc-def1-2345-67890abcdef1")]
public class ImageSegmentation : Instance<ImageSegmentation>
{
    [Output(Guid = "90abcdef-1234-5678-90ab-cdef12345678", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> OutputTexture = new();

    [Output(Guid = "0abcdef1-2345-6789-0abc-def123456789", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> MaskTexture = new();

    [Output(Guid = "abcdef12-3456-7890-abcd-ef1234567890", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Output(Guid = "bcdef123-4567-890a-bcde-f1234567890a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Confidence = new();

    [Output(Guid = "cdef1234-5678-90ab-cdef-1234567890ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> CategoryMask = new();

    [Output(Guid = "def01234-5678-90ab-cdef-1234567890ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> ConfidenceMask = new();

    [Output(Guid = "ef012345-6789-0abc-def1-234567890abc", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> DebugTexture = new();

    public ImageSegmentation()
    {
        OutputTexture.UpdateAction = Update;
        MaskTexture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        Confidence.UpdateAction = Update;
        CategoryMask.UpdateAction = Update;
        ConfidenceMask.UpdateAction = Update;
        DebugTexture.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var inputTexture = InputTexture.GetValue(context);
        var enabled = Enabled.GetValue(context);
        var debug = Debug.GetValue(context);
        var model = (SegmentationModel)Model.GetValue(context);
        var selectedCategories = SelectedCategories.GetValue(context);
        var categoryAllowlist = CategoryAllowlist.GetValue(context);

        if (!enabled || inputTexture == null || inputTexture.IsDisposed)
        {
            OutputTexture.Value = inputTexture;
            StopWorker(debug);
            ClearOutputs();
            return;
        }

        OutputTexture.Value = inputTexture;

        if (_processingTask == null || _processingTask.IsCompleted || _imageSegmenter == null || model != _activeModel)
        {
            InitializeWorker(debug, model);
        }

        if (_imageSegmenter == null)
        {
            ClearOutputs();
            return;
        }

        if (_inputQueue.Count < 1)
        {
            var request = CreateRequestFromTexture(inputTexture, selectedCategories, categoryAllowlist);
            if (request != null)
            {
                _inputQueue.Enqueue(request);
            }
        }

        while (_outputQueue.TryDequeue(out var result))
        {
            if (result.MaskData != null)
            {
                _currentResult = result;
            }
        }

        if (_currentResult != null)
        {
            UpdateOutputsWithResult(_currentResult, debug);
            
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
            MaskTexture.Value = null;
            Confidence.Value = 0.0f;
            CategoryMask.Value = null;
            ConfidenceMask.Value = null;
            DebugTexture.Value = null;
        }
    }

    private void ClearOutputs()
    {
        _maskData = null;
        _currentResult = null;
        MaskTexture.Value = null;
        Confidence.Value = 0.0f;
        CategoryMask.Value = null;
        ConfidenceMask.Value = null;
        DebugTexture.Value = null;
    }

    #region MediaPipe Integration
    private ImageSegmenter? _imageSegmenter;
    private byte[]? _maskData;
    private long _frameTimestamp;
    private readonly object _imageSegmenterLock = new object();
    private readonly object _timestampLock = new object();

    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<ImageSegmentationRequest> _inputQueue = new();
    private readonly ConcurrentQueue<ImageSegmentationResultPacket> _outputQueue = new();
    private ImageSegmentationResultPacket? _currentResult;
    private readonly object _workerLock = new object();
    private SegmentationModel _activeModel;
    
    private readonly ConcurrentDictionary<(int width, int height), SharpDX.Direct3D11.Texture2D> _cachedStagingTextures = new();
    private readonly object _textureCacheLock = new object();
    
    private readonly ConcurrentBag<Mat> _matPool = new();
    private readonly ConcurrentBag<byte[]> _bufferPool = new();
    private readonly object _poolLock = new object();

    #endregion
    
    #region Worker Thread
    private void InitializeWorker(bool debug, SegmentationModel model)
    {
        StopWorker(debug);
        _activeModel = model;

        try
        {
            lock (_workerLock)
            {
                string modelName = "";
                switch (model)
                {
                    case SegmentationModel.SelfieMulticlass:
                        modelName = "selfie_multiclass_256x256.tflite";
                        break;
                    case SegmentationModel.DeepLabV3:
                        modelName = "deeplabv3.tflite";
                        break;
                    case SegmentationModel.MaskRCNN:
                        modelName = "mask_rcnn_inception_resnet_v2_atrous_coco.tflite";
                        break;
                    case SegmentationModel.SelfieSegmenter:
                    default:
                        modelName = "selfie_segmenter.tflite";
                        break;
                }

                string modelPath = $"../../../../Operators/Mediapipe/Resources/{modelName}";
                string fullPath = System.IO.Path.GetFullPath(modelPath);

                string[] possibleModelPaths = {
                    fullPath,
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", modelName),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Models", modelName),
                    $"../../Mediapipe-Sharp/src/Mediapipe/Models/{modelName}",
                    $"../../../Mediapipe-Sharp/src/Mediapipe/Models/{modelName}"
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
                    if (debug) Log.Error($"[ImageSegmentation] Model not found: {fullPath}", this);
                    return;
                }

                var baseOptions = new Mediapipe.Tasks.Core.CoreBaseOptions(
                    modelAssetPath: fullPath,
                    delegateCase: Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU
                );

                ImageSegmenterOptions options = new(
                    baseOptions,
                    VisionRunningMode.VIDEO,
                    outputCategoryMask: true,
                    outputConfidenceMasks: true
                );

                lock (_imageSegmenterLock)
                {
                    _imageSegmenter = ImageSegmenter.CreateFromOptions(options);
                }
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;
                _processingTask = Task.Run(() => WorkerLoop(token, debug), token);
            }
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[ImageSegmentation] Init Failed: {ex.Message}", this);
            lock (_imageSegmenterLock)
            {
                _imageSegmenter = null;
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
                    if (_imageSegmenter != null && request.PixelData != null)
                    {
                        ProcessFrame(request, debug);
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[ImageSegmentation] Worker error: {ex.Message}", this);
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

    private void ProcessFrame(ImageSegmentationRequest request, bool debug)
    {
        using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        ImageSegmenter? segmenter;
        lock (_imageSegmenterLock)
        {
            segmenter = _imageSegmenter;
        }

        var result = segmenter?.SegmentForVideo(image, request.Timestamp);

        if (result is { } validResult && validResult.ConfidenceMasks != null && validResult.ConfidenceMasks.Count > 0)
        {
            var firstMask = validResult.ConfidenceMasks[0];
            int maskWidth = firstMask.Width();
            int maskHeight = firstMask.Height();

            var maskData = ConvertSegmentationResultToMask(validResult, maskWidth, maskHeight, request.SelectedCategories, debug);
            
            byte[]? categoryMaskData = null;
            float[]? confidenceMaskData = null;
            
            if (validResult.CategoryMask != null)
            {
                categoryMaskData = ConvertCategoryMask(validResult, maskWidth, maskHeight, debug);
            }
            
            if (validResult.ConfidenceMasks != null && validResult.ConfidenceMasks.Count > 0)
            {
                confidenceMaskData = ConvertConfidenceMask(validResult, maskWidth, maskHeight, debug);
            }
            
            var packet = new ImageSegmentationResultPacket
                             {
                                 MaskData = maskData,
                                 Width = maskWidth,
                                 Height = maskHeight,
                                 Confidence = 1.0f,
                                 CategoryMaskData = categoryMaskData,
                                 ConfidenceMaskData = confidenceMaskData
                             };
            _outputQueue.Enqueue(packet);
        }
        else
        {
            _outputQueue.Enqueue(new ImageSegmentationResultPacket
            {
                MaskData = null,
                Width = request.Width,
                Height = request.Height,
                Confidence = 0.0f
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
                if (debug) Log.Error($"[ImageSegmentation] THREAD SAFETY: Error waiting for worker task: {ex.Message}", this);
            }

            try
            {
                lock (_imageSegmenterLock)
                {
                    _imageSegmenter?.Close();
                    _imageSegmenter = null;
                }
            }
            catch (Exception ex)
            {
                if (debug) Log.Error($"[ImageSegmentation] RESOURCE MANAGEMENT: Error closing ImageSegmenter: {ex.Message}", this);
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

    private ImageSegmentationRequest? CreateRequestFromTexture(Texture2D texture, string? selectedCategories, string categoryAllowlist)
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

            return new ImageSegmentationRequest
            {
                PixelData = buffer,
                Width = width,
                Height = height,
                Timestamp = ts,
                SelectedCategories = selectedCategories,
                CategoryAllowlist = string.IsNullOrEmpty(categoryAllowlist) ? null : categoryAllowlist.Split(',')
            };
        }
        finally
        {
            device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        }
    }
    #endregion

    private void UpdateOutputsWithResult(ImageSegmentationResultPacket result, bool debug)
    {
        if (result.MaskData == null)
        {
            MaskTexture.Value = null;
            Confidence.Value = 0.0f;
            CategoryMask.Value = null;
            ConfidenceMask.Value = null;
            return;
        }
        
        _maskData = result.MaskData;
        Confidence.Value = result.Confidence;
        
        if (_maskTexture == null || _maskTexture.Description.Width != result.Width || _maskTexture.Description.Height != result.Height || _maskTexture.Description.Format != SharpDX.DXGI.Format.R8G8B8A8_UNorm)
        {
            _maskTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = result.Width,
                Height = result.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _maskTexture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, desc));
        }
        
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(result.MaskData, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var dataBox = new DataBox(handle.AddrOfPinnedObject(), result.Width * 4, 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _maskTexture);
        }
        finally
        {
            handle.Free();
        }
        
        MaskTexture.Value = _maskTexture;
        
        if (result.CategoryMaskData != null)
        {
            UpdateCategoryMaskTexture(result.CategoryMaskData, result.Width, result.Height);
        }
        
        if (result.ConfidenceMaskData != null)
        {
            UpdateConfidenceMaskTexture(result.ConfidenceMaskData, result.Width, result.Height);
        }
    }

    private byte[]? ConvertSegmentationResultToMask(ImageSegmenterResult result, int width, int height, string? selectedCategories, bool debug)
    {
        if (result.ConfidenceMasks == null || result.ConfidenceMasks.Count == 0) return null;

        try
        {
            var singleChannelMask = new byte[width * height];
            var indices = new List<int>();

            if (string.IsNullOrEmpty(selectedCategories))
            {
                indices.Add(0);
            }
            else
            {
                var parts = selectedCategories.Split(',');
                foreach (var part in parts)
                {
                    if (int.TryParse(part.Trim(), out int idx))
                    {
                        if (idx >= 0 && idx < result.ConfidenceMasks.Count)
                        {
                            indices.Add(idx);
                        }
                    }
                }
                
                if (indices.Count == 0) indices.Add(0);
            }

            foreach (var index in indices)
            {
                var mask = result.ConfidenceMasks[index];
                
                try
                {
                    using var pixelLock = new PixelWriteLock(mask);
                    nint pixelsPtr = pixelLock.Pixels();
                    
                    if (pixelsPtr != 0)
                    {
                        int byteCount = width * height * 4;
                        byte[] tempBytes = new byte[byteCount];
                        Marshal.Copy((IntPtr)pixelsPtr, tempBytes, 0, byteCount);
                        
                        for (int i = 0; i < width * height; i++)
                        {
                            float val = BitConverter.ToSingle(tempBytes, i * 4);
                            byte pixelVal = (byte)(val * 255);
                            
                            singleChannelMask[i] = Math.Max(singleChannelMask[i], pixelVal);
                        }
                    }
                }
                catch (Exception ex1)
                {
                    if (debug) Log.Debug($"[ConvertSegmentationResultToMask] Failed to process mask {index}: {ex1.Message}", this);
                }
            }

            var rgbaData = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                byte val = singleChannelMask[i];
                int rgbaIdx = i * 4;
                rgbaData[rgbaIdx + 0] = val; // R
                rgbaData[rgbaIdx + 1] = val; // G
                rgbaData[rgbaIdx + 2] = val; // B
                rgbaData[rgbaIdx + 3] = 255; // A
            }

            return rgbaData;
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[ConvertSegmentationResultToMask] Error converting segmentation result: {ex.Message}", this);
            return null;
        }
    }

    private byte[]? ConvertCategoryMask(ImageSegmenterResult result, int width, int height, bool debug)
    {
        if (result.CategoryMask == null)
        {
            return null;
        }
        
        try
        {
            var maskData = new byte[width * height];
            
            try
            {
                using var pixelLock = new PixelWriteLock(result.CategoryMask);
                nint pixelsPtr = pixelLock.Pixels();
                
                if (pixelsPtr != 0)
                {
                    int copyLength = Math.Min(width * height, maskData.Length);
                    Marshal.Copy((IntPtr)pixelsPtr, maskData, 0, copyLength);
                    return maskData;
                }
            }
            catch (Exception ex1)
            {
                if (debug) Log.Debug($"[ConvertCategoryMask] PixelWriteLock method failed: {ex1.Message}, using fallback", this);
            }
            
            for (int i = 0; i < maskData.Length; i++)
            {
                maskData[i] = 0;
            }
            
            return maskData;
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[ConvertCategoryMask] Error converting category mask: {ex.Message}", this);
            return null;
        }
    }

    private float[]? ConvertConfidenceMask(ImageSegmenterResult result, int width, int height, bool debug)
    {
        if (result.ConfidenceMasks == null || result.ConfidenceMasks.Count == 0)
        {
            return null;
        }
        
        try
        {
            var mask = result.ConfidenceMasks[0];
            var maskData = new float[width * height];
            
            try
            {
                using var pixelLock = new PixelWriteLock(mask);
                nint pixelsPtr = pixelLock.Pixels();
                
                if (pixelsPtr != 0)
                {
                    int byteCount = Math.Min(width * height * 4, maskData.Length * 4);
                    byte[] tempBytes = new byte[byteCount];
                    Marshal.Copy((IntPtr)pixelsPtr, tempBytes, 0, byteCount);
                    
                    int floatCount = Math.Min(byteCount / 4, maskData.Length);
                    for (int i = 0; i < floatCount; i++)
                    {
                        maskData[i] = BitConverter.ToSingle(tempBytes, i * 4);
                    }
                    
                    return maskData;
                }
            }
            catch (Exception ex1)
            {
                if (debug) Log.Debug($"[ConvertConfidenceMask] PixelWriteLock method failed: {ex1.Message}, using fallback", this);
            }
            
            for (int i = 0; i < maskData.Length; i++)
            {
                maskData[i] = 0.0f;
            }
            
            return maskData;
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[ConvertConfidenceMask] Error converting confidence mask: {ex.Message}", this);
            return null;
        }
    }

    private void UpdateCategoryMaskTexture(byte[] maskData, int width, int height)
    {
        if (maskData == null || maskData.Length == 0)
        {
            CategoryMask.Value = null;
            return;
        }
        
        if (_categoryMaskTexture == null || _categoryMaskTexture.Description.Width != width || _categoryMaskTexture.Description.Height != height || _categoryMaskTexture.Description.Format != SharpDX.DXGI.Format.R8_UNorm)
        {
            _categoryMaskTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.R8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _categoryMaskTexture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, desc));
        }
        
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(maskData, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var dataBox = new DataBox(handle.AddrOfPinnedObject(), width, 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _categoryMaskTexture);
        }
        finally
        {
            handle.Free();
        }
        
        CategoryMask.Value = _categoryMaskTexture;
    }

    private void UpdateConfidenceMaskTexture(float[] maskData, int width, int height)
    {
        if (maskData == null || maskData.Length == 0)
        {
            ConfidenceMask.Value = null;
            return;
        }
        
        if (_confidenceMaskTexture == null || _confidenceMaskTexture.Description.Width != width || _confidenceMaskTexture.Description.Height != height || _confidenceMaskTexture.Description.Format != SharpDX.DXGI.Format.R32_Float)
        {
            _confidenceMaskTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SharpDX.DXGI.Format.R32_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _confidenceMaskTexture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, desc));
        }
        
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(maskData, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var dataBox = new DataBox(handle.AddrOfPinnedObject(), width * 4, 0);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _confidenceMaskTexture);
        }
        finally
        {
            handle.Free();
        }
        
        ConfidenceMask.Value = _confidenceMaskTexture;
    }

    private Texture2D? _maskTexture;
    private Texture2D? _categoryMaskTexture;
    private Texture2D? _confidenceMaskTexture;

    #region Debug Visualization
    private Texture2D? _debugTexture;
    private readonly object _debugTextureLock = new object();

    private void DrawDebugVisuals(Mat mat, ImageSegmentationResultPacket result)
    {
        if (result.CategoryMaskData == null && result.ConfidenceMaskData == null) return;
        
        var categoryColors = new Scalar[]
        {
            new Scalar(0, 0, 0, 0),
            new Scalar(0, 255, 0, 128),
            new Scalar(255, 0, 0, 128),
            new Scalar(0, 0, 255, 128),
            new Scalar(255, 255, 0, 128),
            new Scalar(255, 0, 255, 128),
            new Scalar(0, 255, 255, 128),
            new Scalar(128, 0, 255, 128),
            new Scalar(255, 128, 0, 128),
            new Scalar(128, 255, 0, 128)
        };

        int width = result.Width;
        int height = result.Height;
        
        if (result.CategoryMaskData != null)
        {
            var maskData = result.CategoryMaskData;
            
            unsafe 
            {
                byte* matPtr = mat.DataPointer;
                long step = mat.Step();
                int channels = mat.Channels();

                Parallel.For(0, height, y =>
                {
                    byte* rowPtr = matPtr + y * step;
                    int rowOffset = y * width;
                    
                    for (int x = 0; x < width; x++)
                    {
                        int maskIndex = rowOffset + x;
                        if (maskIndex < maskData.Length)
                        {
                            int category = maskData[maskIndex];
                            if (category > 0 && category < categoryColors.Length)
                            {
                                var color = categoryColors[category];
                                int pixelIdx = x * channels;
                                
                                float alpha = (float)(color.Val3 / 255.0f);
                                
                                byte b = rowPtr[pixelIdx];
                                byte g = rowPtr[pixelIdx + 1];
                                byte r = rowPtr[pixelIdx + 2];
                                
                                rowPtr[pixelIdx] = (byte)(b * (1 - alpha) + color.Val0 * alpha);
                                rowPtr[pixelIdx + 1] = (byte)(g * (1 - alpha) + color.Val1 * alpha);
                                rowPtr[pixelIdx + 2] = (byte)(r * (1 - alpha) + color.Val2 * alpha);
                            }
                        }
                    }
                });
            }
        }
    }

    private void UpdateDebugTextureFromMat(Mat mat)
    {
        lock (_debugTextureLock)
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
    }
    #endregion

    #region Cleanup
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        try
        {
            lock (_imageSegmenterLock)
            {
                _imageSegmenter?.Close();
                _imageSegmenter = null;
            }
        }
        catch (Exception ex)
        {
        }

        _maskTexture?.Dispose();
        _maskTexture = null;
        
        _categoryMaskTexture?.Dispose();
        _categoryMaskTexture = null;
        
        _confidenceMaskTexture?.Dispose();
        _confidenceMaskTexture = null;
        
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

        base.Dispose(isDisposing);
    }
    #endregion

    #region Input Parameters
    [Input(Guid = "34567890-abcd-ef12-3456-7890abcdef12")]
    public readonly InputSlot<Texture2D> InputTexture = new();

    [Input(Guid = "4567890a-bcde-f123-4567-890abcdef123")]
    public readonly InputSlot<bool> Enabled = new(true);

    [Input(Guid = "567890ab-cdef-1234-5678-90abcdef1234", MappedType = typeof(SegmentationModel))]
    public readonly InputSlot<int> Model = new();

    [Input(Guid = "67890abc-def1-2345-6789-0abcdef12345")]
    public readonly InputSlot<string> SelectedCategories = new();

    [Input(Guid = "7890abcd-ef12-3456-7890-abcdef123456")]
    public readonly InputSlot<bool> Debug = new(false);

    [Input(Guid = "890abcde-f123-4567-890a-bcdef1234567")]
    public readonly InputSlot<string> CategoryAllowlist = new();
    #endregion

    private enum SegmentationModel
    {
        SelfieSegmenter,
        SelfieMulticlass,
        DeepLabV3,
        MaskRCNN,
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

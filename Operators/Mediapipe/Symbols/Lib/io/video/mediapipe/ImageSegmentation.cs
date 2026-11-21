using System.Threading;
using OpenCvSharp;
using T3.Graphics.Compat;
#nullable enable

using Mediapipe.Tasks.Vision.ImageSegmenter;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Framework.Formats;
using T3.Core.Resource.Assets;
using Image = Mediapipe.Framework.Formats.Image;
// ReSharper disable InconsistentNaming
// ReSharper disable EmptyGeneralCatchClause

namespace Lib.io.video.mediapipe;

internal sealed class ImageSegmentationRequest
{
    public byte[]? PixelData;
    public int Width;
    public int Height;
    public long Timestamp;
    public string? SelectedCategories;
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
[ExportDependencies("mediapipe_c.dll", "libmediapipe_c.so", "libmediapipe_c.dylib", "libmediapipe_opencl_stub.dylib", "OpenCvSharpExtern.dll", "libOpenCvSharpExtern.so", "libOpenCvSharpExtern.dylib", "Emgu.CV.dll", "cvextern.dll", "OpenGL.Net.dll", "CppSharp.Runtime.dll", "Google.Protobuf.dll")]
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
        // The host can evaluate an instance after it has been disposed (hot reload, package
        // unload races). Starting a worker here would leak its native task and its thread for the
        // process lifetime, because Dispose has already run.
        if (_disposed)
            return;

        var inputTexture = InputTexture.GetValue(context);
        var enabled = Enabled.GetValue(context);
        var debug = Debug.GetValue(context);
        var model = (SegmentationModel)Model.GetValue(context);
        var selectedCategories = SelectedCategories.GetValue(context);

        if (!enabled || inputTexture == null || inputTexture.IsDisposed)
        {
            OutputTexture.Value = inputTexture;
            StopWorker();
            ClearOutputs();
            return;
        }

        OutputTexture.Value = inputTexture;

        // A failed init (missing model, missing native, rejected delegate) must not rebuild the
        // worker every frame: wait between attempts, backing off up to 30 s, and retry immediately
        // when the configuration itself changed. `initializing` tells a first attempt that is still
        // running apart from one that never started.
        var parametersChanged = model != _activeModel
                                || Acceleration.GetValue(context) != _activeAcceleration
                                || InferenceThreads.GetValue(context) != _activeInferenceThreads;
        if (parametersChanged)
            _initAttempts = 0;

        var initializing = _imageSegmenter == null && _processingTask is { IsCompleted: false };
        var retryDelayMs = MediaPipeInitRetry.BackoffMs(_initAttempts);
        var retryBlocked = _imageSegmenter == null && _initAttempts > 0
                           && Environment.TickCount64 - _lastInitAttemptTicks < retryDelayMs;

        if (!initializing && !retryBlocked
            && (_processingTask == null || _processingTask.IsCompleted || _imageSegmenter == null || parametersChanged))
        {
            _activeAcceleration = Acceleration.GetValue(context);
            _activeInferenceThreads = InferenceThreads.GetValue(context);
            _lastInitAttemptTicks = Environment.TickCount64;
            _initAttempts++;
            InitializeWorker(debug, model);
        }

        if (_imageSegmenter == null)
        {
            ClearOutputs();
            return;
        }

        if (_inputQueue.IsEmpty && !_workerBusy)
        {
            var request = CreateRequestFromTexture(inputTexture, selectedCategories);
            if (request != null)
            {
                _inputQueue.Enqueue(request);
            }
        }

        while (_outputQueue.TryDequeue(out var result))
        {
            // Assign every packet, including the empty one the worker enqueues when the segmenter
            // returned no mask. Keeping only the last non-null mask latched the outputs (and froze
            // UpdateCount/Confidence) on the last good frame, hiding a segmenter that stopped
            // producing masks; UpdateOutputsWithResult clears the outputs for a null mask.
            _currentResult = result;
        }

        if (_currentResult != null)
        {
            UpdateOutputsWithResult(_currentResult, debug);
            UpdateCount.Value++;

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
    private readonly ConcurrentQueue<ImageSegmentationRequest> _inputQueue = new();
    private readonly ConcurrentQueue<ImageSegmentationResultPacket> _outputQueue = new();
    private ImageSegmentationResultPacket? _currentResult;
    private volatile bool _disposed;
    private readonly object _workerLock = new object();
    private readonly MediaPipeWorkerReaper _reaper = new();
    private SegmentationModel _activeModel;
    private MediaPipeAcceleration? _activeAcceleration;
    private int _activeInferenceThreads = -1;

    private readonly ConcurrentDictionary<(int width, int height, T3.Graphics.Format format), T3.Graphics.Compat.Texture2D> _cachedStagingTextures = new();

    private readonly object _textureCacheLock = new object();

    private readonly ConcurrentBag<byte[]> _bufferPool = new();
    private readonly object _poolLock = new object();

    #endregion

    #region Worker Thread
    private void InitializeWorker(bool debug, SegmentationModel model)
    {
        StopWorker();
        _activeModel = model;

        var modelName = model switch
        {
            SegmentationModel.SelfieMulticlass => "selfie_multiclass_256x256.tflite",
            SegmentationModel.DeepLabV3 => "deeplabv3.tflite",
            _ => "selfie_segmenter.tflite"
        };

        // Registry lookup only - the native segmenter is created on the worker below, so a slow model
        // load never stalls the evaluation thread and a native failure surfaces off the frame path.
        if (!MediaPipeModelResolver.TryResolve(modelName, this, out var fullPath))
        {
            // Not Debug-gated: a missing asset is the one failure a user has to act on, and the retry
            // backoff keeps this to one message per backoff window.
            Log.Warning($"[ImageSegmentation] Model not found: {modelName}", this);
            return;
        }

        var acceleration = _activeAcceleration ?? MediaPipeAcceleration.Cpu;
        var inferenceThreads = _activeInferenceThreads;

        // The CTS and the task exist before any native work starts, so StopWorker always has
        // something to cancel and Update can tell "initializing" from "needs initialization".
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        _processingTask = Task.Run(() => InitializeAndRunWorker(fullPath, acceleration, inferenceThreads, token, debug), token);
    }

    private void InitializeAndRunWorker(string fullPath, MediaPipeAcceleration acceleration, int inferenceThreads,
                                        CancellationToken token, bool debug)
    {
        ImageSegmenter? segmenter = null;
        try
        {
            var delegateCase = acceleration == MediaPipeAcceleration.Xnnpack ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.Xnnpack
                : acceleration == MediaPipeAcceleration.Gpu ? Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.GPU
                : Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU;

            ImageSegmenterOptions BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate selected) => new(
                new Mediapipe.Tasks.Core.CoreBaseOptions(modelAssetPath: fullPath, delegateCase: selected, numThreads: inferenceThreads),
                VisionRunningMode.VIDEO,
                outputCategoryMask: true,
                outputConfidenceMasks: true
            );

            try
            {
                segmenter = ImageSegmenter.CreateFromOptions(BuildOptions(delegateCase));
            }
            catch (Exception gpuEx) when (acceleration != MediaPipeAcceleration.Cpu)
            {
                Log.Warning($"[ImageSegmentation] GPU acceleration not available ({gpuEx.Message}) - using CPU.", this);
                segmenter = ImageSegmenter.CreateFromOptions(BuildOptions(Mediapipe.Tasks.Core.CoreBaseOptions.Delegate.CPU));
            }

            if (segmenter == null)
                return;

            var published = false;
            lock (_imageSegmenterLock)
            {
                if (!token.IsCancellationRequested)
                {
                    _imageSegmenter = segmenter;
                    published = true;
                }
            }

            if (!published)
            {
                MediaPipeWorkerReaper.CloseNative(segmenter);
                return;
            }

            // A later failure starts its backoff from the first step again.
            _initAttempts = 0;

            // The worker loop owns this instance; StopWorker disposes it only after this task exits.
            WorkerLoop(token, segmenter, debug);
        }
        catch (Exception ex)
        {
            Log.Warning($"[ImageSegmentation] Initialization failed: {ex.Message}", this);
            if (segmenter != null)
            {
                try { MediaPipeWorkerReaper.CloseNative(segmenter); } catch { }
                lock (_imageSegmenterLock)
                {
                    if (ReferenceEquals(_imageSegmenter, segmenter))
                        _imageSegmenter = null;
                }
            }
        }
    }

    private void WorkerLoop(CancellationToken token, ImageSegmenter segmenter, bool debug)
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
                        ProcessFrame(request, segmenter, debug);
                    }
                }
                catch (Exception ex)
                {
                    if (debug) Log.Error($"[ImageSegmentation] Worker error: {ex.Message}", this);
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

    private void ProcessFrame(ImageSegmentationRequest request, ImageSegmenter segmenter, bool debug)
    {
        using var image = new Image(Mediapipe.ImageFormat.Types.Format.Srgb, request.Width, request.Height, request.Width * 3, request.PixelData!);

        var result = segmenter.SegmentForVideo(image, request.Timestamp);

        if (result is { } validResult && validResult.ConfidenceMasks != null && validResult.ConfidenceMasks.Count > 0)
        {
            try
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

                confidenceMaskData = ConvertConfidenceMask(validResult, maskWidth, maskHeight, debug);

                var packet = new ImageSegmentationResultPacket
                                 {
                                     MaskData = maskData,
                                     Width = maskWidth,
                                     Height = maskHeight,
                                     Confidence = MeanConfidence(confidenceMaskData),
                                     CategoryMaskData = categoryMaskData,
                                     ConfidenceMaskData = confidenceMaskData
                                 };
                _outputQueue.Enqueue(packet);
            }
            finally
            {
                // The segmenter allocates the mask images per inference and the result owns
                // them - not disposing them leaks native memory every frame.
                foreach (var mask in validResult.ConfidenceMasks)
                {
                    mask?.Dispose();
                }
                validResult.CategoryMask?.Dispose();
            }
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

    private void StopWorker()
    {
        lock (_workerLock)
        {
            var cancellation = _cancellationTokenSource;
            _cancellationTokenSource = null;
            cancellation?.Cancel();

            var task = _processingTask;
            _processingTask = null;

            ImageSegmenter? nativeTask;
            lock (_imageSegmenterLock)
            {
                nativeTask = _imageSegmenter;
                _imageSegmenter = null;
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

            // Results still in flight when the worker stopped are dropped, not pooled: MaskData comes
            // from the mask converter as a plain array, while _bufferPool holds POH-pinned inference
            // inputs whose pointer the native Image keeps - mixing them would be a use-after-move.
            while (_outputQueue.TryDequeue(out _))
            {
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

    private ImageSegmentationRequest? CreateRequestFromTexture(Texture2D texture, string? selectedCategories)
    {
        var desc = texture.Description;

        // One readback per evaluation frame serves every MediaPipe operator reading this texture: the
        // mapped copy costs ~2 ms at 1080p and barely less at 640 px, so seven operators on one camera
        // would otherwise pay it seven times for byte-identical pixels.
        if (!MediaPipeFrameCache.TryGet(texture, MaxInferenceWidth, 3, out var frame,
                                        out var inferenceWidth, out var inferenceHeight, out var readbackError))
        {
            Log.Debug($"[ImageSegmentation] Texture readback failed: {readbackError}", this);
            return null;
        }

        // The cached array is shared and read-only for this frame; the worker still gets its own pooled
        // copy because the native Image keeps that pointer for the duration of the inference.
        byte[] buffer = GetBuffer(frame.Length);
        System.Buffer.BlockCopy(frame, 0, buffer, 0, frame.Length);

        long ts;
        lock(_timestampLock) { ts = _frameTimestamp; _frameTimestamp += 33; }

        return new ImageSegmentationRequest
        {
            PixelData = buffer,
            Width = inferenceWidth,
            Height = inferenceHeight,
            Timestamp = ts,
            SelectedCategories = selectedCategories
        };
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

        if (_maskTexture == null || _maskTexture.Description.Width != result.Width || _maskTexture.Description.Height != result.Height || _maskTexture.Description.Format != T3.Graphics.Format.R8G8B8A8_UNorm)
        {
            _maskTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = result.Width,
                Height = result.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = T3.Graphics.Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _maskTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, desc));
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


    /// <summary>
    /// Mean of the confidence mask the segmenter returned, i.e. how sure the model is overall. The
    /// output used to be hard-coded to 1.0, which made it useless for driving anything.
    /// </summary>
    private static float MeanConfidence(float[]? confidenceMask)
    {
        if (confidenceMask is not { Length: > 0 })
            return 0f;

        var sum = 0.0;
        var count = 0;
        foreach (var value in confidenceMask)
        {
            if (float.IsNaN(value))
                continue;

            sum += Math.Clamp(value, 0f, 1f);
            count++;
        }

        return count == 0 ? 0f : (float)(sum / count);
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
                            byte pixelVal = (byte)(Math.Clamp(float.IsNaN(val) ? 0f : val, 0f, 1f) * 255f);

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

        if (_categoryMaskTexture == null || _categoryMaskTexture.Description.Width != width || _categoryMaskTexture.Description.Height != height || _categoryMaskTexture.Description.Format != T3.Graphics.Format.R8_UNorm)
        {
            _categoryMaskTexture?.Dispose();
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
            _categoryMaskTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, desc));
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

        if (_confidenceMaskTexture == null || _confidenceMaskTexture.Description.Width != width || _confidenceMaskTexture.Description.Height != height || _confidenceMaskTexture.Description.Format != T3.Graphics.Format.R32_Float)
        {
            _confidenceMaskTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = T3.Graphics.Format.R32_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _confidenceMaskTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, desc));
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

            // The segmenter runs on a downscaled inference image, so its masks are smaller than
            // the camera frame - stretch them over the whole frame (nearest neighbour) instead of
            // covering only the top-left corner.
            int matWidth = mat.Width;
            int matHeight = mat.Height;
            float maskXStep = (float)width / matWidth;
            float maskYStep = (float)height / matHeight;

            unsafe
            {
                byte* matPtr = mat.DataPointer;
                long step = mat.Step();
                int channels = mat.Channels();

                Parallel.For(0, matHeight, y =>
                {
                    byte* rowPtr = matPtr + y * step;
                    int maskRow = (int)(y * maskYStep) * width;

                    for (int x = 0; x < matWidth; x++)
                    {
                        int maskIndex = maskRow + (int)(x * maskXStep);
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
    }
    #endregion

    #region Cleanup
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        // Stops the worker task and hands the segmenter to its deferred closer - closing it here
        // directly could hit a frame still running on the worker thread.
        _disposed = true;
        StopWorker();

        // The reaper owns the native instance now; give a running inference a moment to
        // return, but never hang shutdown on a wedged native library.
        _reaper.WaitForPending(2000);

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

    /// <summary>Inference acceleration: Cpu = plain single-threaded TF Lite (works everywhere),
    /// Xnnpack = multithreaded CPU (the fast default; see InferenceThreads), Gpu = OpenGL/EGL
    /// delegate (Linux native has it; the CPU-only Windows/macOS natives fall back to Cpu).</summary>
    [Input(Guid = "a1b2c3d4-0005-4a9b-8c2d-101010101010")]
    public readonly InputSlot<MediaPipeAcceleration> Acceleration = new(MediaPipeAcceleration.Xnnpack);

    /// <summary>Worker threads for the Xnnpack acceleration (0 = library default, which is effectively
        /// single-threaded). 4 is ~2x faster than the default on a 20-core machine; use 2 when
        /// several MediaPipe operators run at once. Ignored by Cpu and Gpu.</summary>
    [Input(Guid = "a1b2c3d4-1005-4a9b-8c2d-101010101010")]
    public readonly InputSlot<int> InferenceThreads = new(DefaultInferenceThreads);

    [Input(Guid = "7890abcd-ef12-3456-7890-abcdef123456")]
    public readonly InputSlot<bool> Debug = new(false);

    #endregion

    private enum SegmentationModel
    {
        SelfieSegmenter,
        SelfieMulticlass,
        DeepLabV3,
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
        Log.Warning($"[ImageSegmentation] Debug readback failed: {ex.Message}", this);
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
        Log.Warning($"[ImageSegmentation] Debug readback: row pitch {dataBox.RowPitch} < {rowBytes}.", this);
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

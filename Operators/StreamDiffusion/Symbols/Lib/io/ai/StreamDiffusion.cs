#pragma warning disable CA1416 // Platform compatibility warnings for Windows-specific APIs
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Logging;
using T3.Graphics.Compat;
using T3.Core.Utils;
using T3.Core.Operator.Attributes;
using t3.streamdiffusion.Onnx;
using t3.streamdiffusion.V2;

namespace Lib.io.ai;

[Guid("9A7B3C8D-4E2F-5A6B-7C8D-9E0F1A2B3C4D")]
[ExportDependencies("Microsoft.ML.OnnxRuntime.dll", "onnxruntime.dll", "onnxruntime_providers_shared.dll",
    "onnxruntime_providers_cuda.dll", "onnxruntime_providers_tensorrt.dll",
    "cudart64_13.dll", "cublas64_13.dll", "cublasLt64_13.dll", "cufft64_12.dll",
    "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "curand64_10.dll",
    "cudnn64_9.dll", "cudnn_*64_9.dll", "libonnxruntime*")]
public sealed class StreamDiffusion : Instance<StreamDiffusion>, IStatusProvider, ICustomDropdownHolder
{
    // Expose pipeline readiness for tests
    internal bool IsPipelineReady() => _pipeline != null;

    private enum InputPixelFormat
    {
        Rgba8,
        Bgra8,
        Rgba16F,
        Rgba32F,
    }

    private sealed record GenerationRequest(
        string Prompt,
        int Steps,
        float Strength,
        int Seed,
        int Width,
        int Height,
        int ResizeMode,
        bool ResetStream,
        bool MatchInputAspect,
        byte[]? InputData,
        int InputWidth,
        int InputHeight,
        InputPixelFormat InputFormat);

    // Generation tags stale results from a replaced worker so they cannot be applied to the new model
    private sealed record GenerationResult(
        byte[]? Data,
        int Width,
        int Height,
        float Time,
        string? Error,
        int Generation = 0,
        /** The resize target when MatchInputAspect pooled one; the render thread returns it after upload. */
        byte[]? PooledBuffer = null);

    // Immutable so the render thread can read the failure state while a worker updates it
    private sealed record FailureKey(string? Path, int Device, int Provider);

    #region Outputs

    [Output(Guid = "D8E2F9A1-7B3C-4D5E-8F9A-1B2C3D4E5F6A", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> Output = new();

    [Output(Guid = "E9F3A2B1-8C4D-5E6F-9A1B-2C3D4E5F6A7B")]
    public readonly Slot<Int2> OutputSize = new();

    [Output(Guid = "F1A4B3C2-9D5E-6F7A-0B2C-3D4E5F6A7B8C")]
    public readonly Slot<float> GenerationTime = new();

    /// <summary>Effective noise scale after the motion-aware controller: starts at Strength, lowered on fast motion, raised on still scenes.</summary>
    [Output(Guid = "F6B7E8C9-A01B-4B2D-9C3E-4D5F6A7B8C9E")]
    public readonly Slot<float> NoiseScale = new();

    [Output(Guid = "07C8F9DA-B12C-4C3E-8D4F-5E6A7B8C9D0F")]
    public readonly Slot<float> EncodeMs = new();

    [Output(Guid = "18D9A0EB-C23D-4D4F-9E5A-6F7B8C9D0E10")]
    public readonly Slot<float> DenoiseMs = new();

    [Output(Guid = "29EAB1FC-D34E-4E5A-8F6B-7A8C9D0E1F21")]
    public readonly Slot<float> DecodeMs = new();

    #endregion

    #region Inputs

    /// <summary>
    /// Which export in the model store to run. Empty = the first valid export. The dropdown lists
    /// the exports the scan finds, so no paths are typed anywhere; the model store is managed in
    /// Settings > AI Models.
    /// </summary>
    [Input(Guid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public readonly InputSlot<string> Model = new();

    [Input(Guid = "C3D4E5F6-A7B8-9012-CDEF-123456789012")]
    public readonly InputSlot<string> Prompt = new();

    [Input(Guid = "E1F2A3B4-C5D6-7890-4567-901234567890")]
    public readonly InputSlot<Texture2D?> InputImage = new();

    [Input(Guid = "E5F6A7B8-C9D0-1234-EF01-345678901234")]
    public readonly InputSlot<int> Seed = new(-1);

    /// <summary>
    /// How strongly each Stream frame is re-noised (the V2 stream strength): lower keeps more of the
    /// previous frame, higher follows the input faster. (In pre-V2 builds this input was the img2img
    /// strength; the semantics carry over, so old graphs keep working.)
    /// </summary>
    [Input(Guid = "C9D0E1F2-A3B4-5678-2345-789012345678")]
    public readonly InputSlot<float> Strength = new(0.8f);

    [Input(Guid = "D0E1F2A3-B4C5-6789-3456-890123456789")]
    public readonly InputSlot<int> Steps = new(2);

    [Input(Guid = "E2F3A4B5-C6D7-8901-2345-678901234567", MappedType = typeof(ResizeModeOptions))]
    public readonly InputSlot<int> ResizeMode = new(0);

    /// <summary>
    /// Temporal feedback (like ossia's "Feed prev. output"): blends the newly generated frame with the
    /// previously published one. 0 keeps the pipeline output untouched; values up to 1 smooth out the
    /// frame-to-frame flicker of continuous restyle (trails decay exponentially). Ignored on the
    /// GPU-latent fast path (TIXL_SD_GPU_LATENTS=1), which publishes GPU-resident frames directly.
    /// </summary>
    [Input(Guid = "C9D0E1F2-A3B4-5678-9ABC-DEF012345678")]
    public readonly InputSlot<float> Feedback = new(0.0f);

    /// <summary>
    /// Pace the stream at this many frames per second (default 16, matching capture and downstream
    /// consumers). 0 runs the model as fast as the GPU allows. The model already skips identical
    /// input frames, so a frozen source costs nothing at any rate.
    /// </summary>
    [Input(Guid = "C1D2E3F4-A5B6-4C7D-8E9F-0A1B2C3D4E5F")]
    public readonly InputSlot<float> TargetFps = new(16f);

    /// <summary>
    /// Stretches the model's baked output (e.g. 256x256) back over the input image's dimensions, so
    /// the published output matches the source's aspect ratio and pixel size for compositing. The
    /// model itself always renders square - this undoes the fit stretch on the way out.
    /// </summary>
    [Input(Guid = "B4C5D6E7-C8D9-4E0F-A1B2-3C4D5E6F7A8B")]
    public readonly InputSlot<bool> MatchInputAspect = new();

    [Input(Guid = "A3B4C5D6-E7F8-9012-6789-123456789012")]
    public readonly InputSlot<int> CudaDevice = new(0);

    [Input(Guid = "C7D8E9F0-A1B2-3456-7890-1234567890AB", MappedType = typeof(t3.streamdiffusion.Onnx.ExecutionProvider))]
    public readonly InputSlot<int> ExecutionProvider = new((int)t3.streamdiffusion.Onnx.ExecutionProvider.Auto);

    /// <summary>V2 only: decode one latent instead of a three-latent window. Faster, with less temporal smoothing.</summary>
    [Input(Guid = "B7C8D9E0-F1A2-3456-7890-123456789012")]
    public readonly InputSlot<bool> FastDecode = new();

    [Input(Guid = "B4C5D6E7-F8A9-0123-7890-234567890123")]
    public readonly InputSlot<bool> TriggerGenerate = new();

    [Input(Guid = "C5D6E7F8-A9B0-1234-8901-345678901234")]
    public readonly InputSlot<bool> AutoGenerate = new();

    /// <summary>
    /// Momentarily true to drop the rolling KV cache and re-prime the stream from the next input frame -
    /// useful on scene cuts or when the style drifts over a long take.
    /// </summary>
    /// <summary>
    /// Momentarily true to drop the rolling KV cache and re-prime the stream from the next input frame -
    /// useful on scene cuts or when the style drifts over a long take.
    /// </summary>
    [Input(Guid = "E5A6D7C8-9F0B-4A1C-8B2D-3C4E5F6A7B8D")]
    public readonly InputSlot<bool> ResetStream = new();

    [Input(Guid = "D2E3F4A5-B6C7-8901-2345-678901234567")]
    public readonly InputSlot<bool> Debug = new(false);

    #endregion

    #region Constants

    /** Label of the auto entry in the Model dropdown; maps onto the empty value. */
    private const string AutoModelOption = "Auto (first found)";
    private const string PlaceholderTextDevice = "Select device...";
    private const int ReadbackTimeoutFrames = 60;

    private static readonly string[] ResizeModeNames = { "Stretch", "CenterCrop", "Pad" };
    /**
     * The dropdown lists "Auto (recommended)" plus the accelerations that can actually run this model on
     * the current OS: Windows: Auto, CPU, CUDA, TensorRT - Linux: Auto, CPU, CUDA, TensorRT - macOS:
     * Auto, CPU, CoreML. DirectML is excluded even on Windows: the Wan DiT/VAE graphs use 5D reshapes
     * that the DML provider fails with an internal error, so it can never run this model. Auto resolves
     * to the fastest engine at initialization; the explicit entries pin one engine. The value maps onto
     * the ExecutionProvider enum, so the order is free.
     */
    private static readonly (string Name, t3.streamdiffusion.Onnx.ExecutionProvider Provider)[] ExecutionProviderOptions = BuildExecutionProviderOptions();

    private static (string, t3.streamdiffusion.Onnx.ExecutionProvider)[] BuildExecutionProviderOptions()
    {
        var options = new List<(string, t3.streamdiffusion.Onnx.ExecutionProvider)> { ("Auto (recommended)", t3.streamdiffusion.Onnx.ExecutionProvider.Auto) };
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            options.Add(("CPU", t3.streamdiffusion.Onnx.ExecutionProvider.Cpu));
            options.Add(("CUDA", t3.streamdiffusion.Onnx.ExecutionProvider.Cuda));
            options.Add(("TensorRT", t3.streamdiffusion.Onnx.ExecutionProvider.TensorRt));
        }
        else if (OperatingSystem.IsMacOS())
        {
            options.Add(("CPU", t3.streamdiffusion.Onnx.ExecutionProvider.Cpu));
            options.Add(("CoreML", t3.streamdiffusion.Onnx.ExecutionProvider.CoreMl));
        }

        return options.ToArray();
    }

    /** The engine "Auto" stands for on this machine, probed with the cached runtime checks. */
    internal static t3.streamdiffusion.Onnx.ExecutionProvider ResolveAutoProvider()
    {
        if (OperatingSystem.IsMacOS())
            return t3.streamdiffusion.Onnx.ExecutionProvider.CoreMl;

        string reason;
        var trtOk = OperatingSystem.IsWindows()
                        ? WindowsCudaRuntime.TryPrepareTensorRt(out reason, out _)
                        : LinuxCudaRuntime.TryPrepareTensorRt(out reason, out _);
        if (trtOk)
            return t3.streamdiffusion.Onnx.ExecutionProvider.TensorRt;

        var cudaOk = OperatingSystem.IsWindows()
                         ? WindowsCudaRuntime.TryPrepare(out reason)
                         : LinuxCudaRuntime.TryPrepare(out reason);
        if (cudaOk)
            return t3.streamdiffusion.Onnx.ExecutionProvider.Cuda;

        // DirectML cannot run the V2 graphs (the DiT's 5D reshapes fail), so the honest fallback is the CPU
        return t3.streamdiffusion.Onnx.ExecutionProvider.Cpu;
    }

    /**
     * Values that this OS (or this model) cannot run snap onto the closest provider that works:
     * Auto resolves to the fastest engine available, a saved CoreML on Windows, DirectML on Linux,
     * and DirectML everywhere for the V2 model - its DiT/VAE 5D reshapes fail on the DML provider
     * regardless of the machine.
     */
    private static t3.streamdiffusion.Onnx.ExecutionProvider NormalizeProviderForOs(t3.streamdiffusion.Onnx.ExecutionProvider provider)
    {
        if (provider == t3.streamdiffusion.Onnx.ExecutionProvider.Auto)
            return ResolveAutoProvider();

        if (OperatingSystem.IsMacOS())
        {
            return provider == t3.streamdiffusion.Onnx.ExecutionProvider.Cpu ? t3.streamdiffusion.Onnx.ExecutionProvider.Cpu : t3.streamdiffusion.Onnx.ExecutionProvider.CoreMl;
        }

        if (provider == t3.streamdiffusion.Onnx.ExecutionProvider.DirectMl)
        {
            return t3.streamdiffusion.Onnx.ExecutionProvider.Cuda;
        }

        if (OperatingSystem.IsWindows())
        {
            return provider == t3.streamdiffusion.Onnx.ExecutionProvider.CoreMl ? t3.streamdiffusion.Onnx.ExecutionProvider.Cuda : provider;
        }

        // Linux (and any other OS): CoreML cannot run here
        return provider == t3.streamdiffusion.Onnx.ExecutionProvider.CoreMl ? t3.streamdiffusion.Onnx.ExecutionProvider.Cuda : provider;
    }

    public enum ResizeModeOptions { Stretch, CenterCrop, Pad }

    #endregion

    #region Fields

    // Worker-per-pipeline lifecycle (DepthAnything port)
    private volatile StreamDiffusionV2Pipeline? _pipeline;
    private readonly object _stateLock = new();
    private Task? _processingTask;
    private CancellationTokenSource? _workerCts;
    private volatile bool _isInitializing;
    private volatile bool _initFailed;
    private FailureKey? _failedInputs;
    private int _workerGeneration;
    private string? _activeModelPath;
    private int _activeDeviceId = -1;
    private int _activeProvider = -1;
    private readonly AutoResetEvent _workerWake = new(false);

    private readonly ConcurrentQueue<GenerationRequest> _inputQueue = new();
    private readonly ConcurrentQueue<GenerationResult> _outputQueue = new();
    private volatile bool _requestInFlight;
    private volatile bool _debugLogging;

    // Render-thread state
    private bool _hasPendingInput;
    private Texture2D? _outputTexture;
    private Int2 _lastOutputSize;
    private float _lastGenerationTime;
    private string? _lastStatusMessage;
    private string? _lastLoggedError;
    private int _lastHeartbeatFrame;
    private bool _triggerState;
    private bool _resetState;
    private bool _resetPending;
    private long _lastEnqueueTicks;
    private long _cooldownUntilTicks;
    private string? _typedPrompt;
    private string? _settledPrompt;
    private long _typedPromptChangedTicks;
    private const float PromptSettleSec = 0.75f;
    private float _typicalGenerationTime;
    private const float ThrottledStepFactor = 2.5f;
    private const float ThrottleCooldownSec = 0.25f;
    private int _updating;
    private int _lastSkipLogFrame;
    private int _updateFrameCounter;

    // Deferred GPU readback - the map only runs after the copy query signaled,
    // so the render thread never waits for the GPU.
    private Texture2D? _readbackTexture;
    private bool _readbackPending;
    private InputPixelFormat _readbackFormat;
    private int _readbackWaitFrames;
    private bool _loggedUnsupportedFormat;

    // Linear -> encoded sRGB for float render-target inputs; a table keeps the per-pixel path cheap
    private const int SrgbTableSize = 4096;
    private static readonly byte[] SrgbEncodeTable = CreateSrgbEncodeTable();

    // Worker-side img2img/stream fingerprint (mode 1 and 2); only the current worker reads or writes these
    private int _lastProcessedFingerprint;
    private bool _hasProcessedFingerprint;

    // Staging texture + extract buffer pool
    private Texture2D? _stagingTexture;
    private readonly Stack<byte[]> _extractBufferPool = new();
    private readonly object _poolLock = new();

    // UI dropdown cache
    private int _uiResizeMode;
    private int _uiExecutionProvider;

    /** The provider input as saved (before the Auto resolution), so the dropdown can show what Auto picked. */
    private int _uiRawExecutionProvider;

    #endregion

    public StreamDiffusion()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0)
            return;

        try
        {
            var modelPath = Model.GetValue(context);
            var prompt = SettlePrompt(Prompt.GetValue(context));
            var seed = Seed.GetValue(context);
            var strength = Math.Clamp(Strength.GetValue(context), 0f, 1f);
            var steps = Steps.GetValue(context);
            var inputImage = InputImage.GetValue(context);
            var resizeMode = ResizeMode.GetValue(context);
            var deviceId = CudaDevice.GetValue(context);
            var provider = ExecutionProvider.GetValue(context);

            // A provider that cannot run on this OS (a saved CoreML on Windows, DirectML on Linux...)
            // snaps onto the closest one that can, so the dropdown and the label always show a real option.
            // Auto resolves to the fastest engine this machine can run; DirectML never comes out of it,
            // because the V2 graphs fail on the DML provider.
            provider = (int)NormalizeProviderForOs((t3.streamdiffusion.Onnx.ExecutionProvider)provider);

            var triggerGenerate = TriggerGenerate.GetValue(context);
            var autoGenerate = AutoGenerate.GetValue(context);
            var debug = Debug.GetValue(context);
            var fastDecode = FastDecode.GetValue(context);
            var feedback = Math.Clamp(Feedback.GetValue(context), 0f, 1f);
            var matchInputAspect = MatchInputAspect.GetValue(context);

            if (MathUtils.WasTriggered(ResetStream.GetValue(context), ref _resetState))
            {
                ResetStream.SetTypedInputValue(false);
                _resetPending = true;
            }

            // Model names an export inside the model store; empty or unknown = the first valid export.
            // The store itself is managed in Settings > AI Models - the operator never takes a path.
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                var named = Path.Combine(T3.Core.Settings.AiModelSettings.FamilyDirectory(T3.Core.Settings.AiModelSettings.StreamDiffusionFamilyFolder), modelPath);
                if (StreamDiffusionV2Pipeline.ValidateModelDirectory(named).Length > 0)
                    modelPath = null;
                else
                    modelPath = named;
            }

            if (string.IsNullOrWhiteSpace(modelPath))
                modelPath = FindManagedModelFolder();

            _debugLogging = debug;
            _updateFrameCounter++;

            if (debug && _updateFrameCounter - _lastHeartbeatFrame >= 120)
            {
                _lastHeartbeatFrame = _updateFrameCounter;
                Log.Debug($"[StreamDiffusion] alive f={_updateFrameCounter} provider={provider} " +
                          $"auto={autoGenerate} trigger={triggerGenerate} pipeline={(_pipeline != null)} " +
                          $"pending={_hasPendingInput} inFlight={_requestInFlight} readback={_readbackPending} " +
                          $"init={_isInitializing} outputNull={Output.Value == null}", this);
            }

            _uiResizeMode = resizeMode;
            _uiRawExecutionProvider = ExecutionProvider.GetValue(context);
            _uiExecutionProvider = provider;

            var pipeline = _pipeline;
            if (pipeline != null)
            {
                pipeline.VerboseLogging = debug;
                pipeline.FastDecodeEnabled = fastDecode;
            }

            // Re-init trigger: inputs changed OR worker died. A failed initialization is only retried after the
            // inputs change - otherwise a missing model or unsupported provider would reload every frame.
            var inputsChanged = modelPath != _activeModelPath || deviceId != _activeDeviceId || provider != _activeProvider;
            var workerGone = _processingTask == null || _processingTask.IsCompleted;
            var failed = _failedInputs;
            var failedWithSameInputs = _initFailed && failed != null
                                       && failed.Path == modelPath && failed.Device == deviceId
                                       && failed.Provider == provider;

            if (!_isInitializing && !failedWithSameInputs && (inputsChanged || workerGone))
            {
                InitializePipelineWorker(modelPath, deviceId, provider);
            }

            pipeline = _pipeline;

            if (pipeline == null)
            {
                if (!_isInitializing)
                {
                    if (_outputTexture != null)
                    {
                        _outputTexture.Dispose();
                        _outputTexture = null;
                        _lastOutputSize = Int2.Zero;
                        _lastGenerationTime = 0f;
                        _prevRgba = null;
                    }
                    Output.Value = null;
                    OutputSize.Value = Int2.Zero;
                    GenerationTime.Value = 0f;
                }
                else
                {
                    Output.Value = _outputTexture;
                    OutputSize.Value = _lastOutputSize;
                    GenerationTime.Value = _lastGenerationTime;
                }
                return;
            }

            if (_isInitializing)
            {
                Output.Value = _outputTexture;
                OutputSize.Value = _lastOutputSize;
                GenerationTime.Value = _lastGenerationTime;
                return;
            }

            // V2 bakes its resolution into the exported DiT; the output size comes from the export
            if (!pipeline.TryGetFixedOutputSize(out var fixedWidth, out var fixedHeight))
            {
                fixedWidth = 256;
                fixedHeight = 256;
            }

            // Stream: continuously chase the latest input frame. Dirty-flag checks can't detect
            // in-place texture updates (GetValue already consumed the flag, and video sources like
            // PlayVideo rewrite the same texture), so with AutoGenerate on we keep a request in
            // flight with the newest frame; _requestInFlight rate-limits to GPU speed and TargetFps
            // paces how often new frames are started.
            var targetFps = TargetFps.GetValue(context);
            var nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            var paced = (targetFps <= 0f
                         || nowTicks - _lastEnqueueTicks >= (long)(System.Diagnostics.Stopwatch.Frequency / targetFps))
                        && nowTicks >= _cooldownUntilTicks;
            if (autoGenerate && paced && inputImage != null && !inputImage.IsDisposed)
            {
                _hasPendingInput = true;
            }

            var wantGenerate = false;
            if (autoGenerate)
            {
                var promptValid = !string.IsNullOrEmpty(prompt);
                var inputValid = inputImage != null && !inputImage.IsDisposed;
                wantGenerate = promptValid && inputValid;

                if (!wantGenerate && _debugLogging && _updateFrameCounter - _lastSkipLogFrame >= 60)
                {
                    _lastSkipLogFrame = _updateFrameCounter;
                    var reason = !promptValid ? "prompt empty" : "no input";
                    Log.Debug($"[StreamDiffusion] AutoGenerate skipped: {reason}", this);
                }
            }
            else
            {
                var wasTriggered = MathUtils.WasTriggered(triggerGenerate, ref _triggerState);
                if (wasTriggered)
                {
                    TriggerGenerate.SetTypedInputValue(false);
                    wantGenerate = true;
                }
            }

            if (wantGenerate)
                _hasPendingInput = true;

            // Start a new readback when there is an input image. The V2 model is a video model and
            // cannot generate from text alone; without an image the operator just waits.
            if (_hasPendingInput && !_requestInFlight && _inputQueue.IsEmpty && !_readbackPending)
            {
                if (inputImage != null && !inputImage.IsDisposed)
                {
                    BeginReadback(inputImage);
                }
                else if (_debugLogging && _updateFrameCounter - _lastSkipLogFrame >= 60)
                {
                    _lastSkipLogFrame = _updateFrameCounter;
                    Log.Debug("[StreamDiffusion] Waiting for an input image - the V2 model streams video, it cannot generate from text alone", this);
                }
            }

            // Complete deferred readback and enqueue
            if (_readbackPending)
            {
                var (data, w, h, format) = CompleteReadback();
                if (data != null)
                {
                    _hasPendingInput = false;
                    var resetPending = _resetPending;
                    _resetPending = false;
                    BuildAndEnqueueRequest(
                        prompt: prompt ?? string.Empty,
                        steps: steps,
                        strength: strength,
                        seed: seed,
                        width: fixedWidth,
                        height: fixedHeight,
                        resizeMode: resizeMode,
                        resetStream: resetPending,
                        matchInputAspect: matchInputAspect,
                        inputData: data,
                        inputWidth: w,
                        inputHeight: h,
                        inputFormat: format);
                }
            }

            // Drain results (newest wins; coalesce while render thread is busy). Results from a replaced worker are
            // dropped: its model may have different dimensions and its buffers belong to the old pipeline.
            var generation = Volatile.Read(ref _workerGeneration);
            GenerationResult? newest = null;
            while (_outputQueue.TryDequeue(out var r))
            {
                if (r != null && r.Generation == generation)
                    newest = r;
            }

            if (newest != null)
            {
                if (newest.Data != null)
                {
                    UploadRgbaTexture(ApplyFeedback(newest.Data, newest.Width, newest.Height, feedback),
                                      newest.Width, newest.Height);
                    ReturnOutputBuffer(newest.PooledBuffer);
                    _lastOutputSize = new Int2(newest.Width, newest.Height);
                    _lastGenerationTime = newest.Time;
                    _lastStatusMessage = null;

                    // Thermal stability: laptop GPUs dip their clocks at ~87 C under sustained load, and a frame
                    // produced inside that dip costs several times the usual step while heating the GPU further.
                    // Armed once per result and relative to the typical step, so a model whose normal step is
                    // slow is not held back permanently.
                    var resultTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (_typicalGenerationTime > 0f && newest.Time > _typicalGenerationTime * ThrottledStepFactor)
                    {
                        _cooldownUntilTicks = resultTicks + (long)(System.Diagnostics.Stopwatch.Frequency * ThrottleCooldownSec);
                    }
                    else
                    {
                        _typicalGenerationTime = _typicalGenerationTime > 0f
                                                     ? _typicalGenerationTime + (newest.Time - _typicalGenerationTime) * 0.1f
                                                     : newest.Time;
                    }
                }
                else if (newest.Error != null)
                {
                    // Generation errors must be visible in the log - they otherwise leave a
                    // blank output with no indication anything was attempted.
                    if (!string.Equals(_lastLoggedError, newest.Error, StringComparison.Ordinal))
                    {
                        _lastLoggedError = newest.Error;
                        Log.Warning($"[StreamDiffusion] Generation failed: {newest.Error}", this);
                    }
                    _lastStatusMessage = newest.Error;
                }
            }

            Output.Value = _outputTexture;
            OutputSize.Value = _lastOutputSize;
            GenerationTime.Value = _lastGenerationTime;

            // Stage telemetry from the pipeline's latest step; safe to read while the worker runs
            if (pipeline != null)
            {
                NoiseScale.Value = pipeline.EffectiveNoiseScale;
                EncodeMs.Value = (float)pipeline.LastEncodeMs;
                DenoiseMs.Value = (float)pipeline.LastDenoiseMs;
                DecodeMs.Value = (float)pipeline.LastDecodeMs;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _updating, 0);
        }
    }

    private void BuildAndEnqueueRequest(string prompt, int steps, float strength, int seed, int width, int height,
                                        int resizeMode, bool resetStream, bool matchInputAspect,
                                        byte[]? inputData, int inputWidth, int inputHeight, InputPixelFormat inputFormat)
    {
        // The V2 step count is the KV-cache batch size, so it is capped at 4
        var clampedSteps = Math.Clamp(steps, 1, 4);

        if (_debugLogging)
        {
            Log.Debug($"[StreamDiffusion] StreamDiffusion V2, steps={clampedSteps}", this);
        }

        _lastEnqueueTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        var request = new GenerationRequest(
            prompt, clampedSteps, strength, seed,
            width, height, resizeMode, resetStream, matchInputAspect,
            inputData, inputWidth, inputHeight, inputFormat);

        // The request consumes the pending flag. Without this, a txt2img trigger (or an AutoGenerate
        // parameter change) leaves it set and the render thread re-enqueues every frame, pinning the GPU
        // in back-to-back generations. The Stream path sets it again every frame by design.
        _hasPendingInput = false;

        // Publish "in flight" before the worker can see the request. Enqueueing first let the worker finish
        // (and store false) before this thread stored true, leaving the flag stuck and the op idle forever.
        _requestInFlight = true;
        _inputQueue.Enqueue(request);
        _workerWake.Set();
    }

    #region Pipeline worker lifecycle (DepthAnything port)

    /** Previous published frame for the Feedback blend (evaluation thread only, guarded by _updating). */
    private byte[]? _prevRgba;
    private int _prevWidth;
    private int _prevHeight;

    /** Temporal feedback ("Feed prev. output"): blends the new frame with the last published one.
     *  The stored previous frame is the BLENDED one, so trails decay exponentially. f=0 is a no-op
     *  and never allocates; with f>0 the blend happens in place in the cached previous-frame
     *  buffer (it is both history and published frame), so the steady state allocates nothing. */
    private byte[] ApplyFeedback(byte[] data, int width, int height, float feedback)
    {
        var length = width * height * 4;
        if (feedback <= 0f || data.Length != length)
        {
            _prevRgba = null;
            return data;
        }

        if (_prevRgba is not byte[] prev || _prevWidth != width || _prevHeight != height)
        {
            prev = (byte[])data.Clone();   // first frame / resolution change only
        }
        else
        {
            for (var i = 0; i < length; i++)
            {
                prev[i] = (byte)(data[i] * (1f - feedback) + prev[i] * feedback + 0.5f);
            }
        }

        _prevRgba = prev;
        _prevWidth = width;
        _prevHeight = height;
        return prev;
    }

    /** Model store scan result, cached briefly so an empty EnginePath does not rescan (and re-log) the store on every frame. */
    private static (string? Folder, long ScannedAtTicks) ManagedFolderCache;
    private const double ManagedModelScanIntervalMs = 5000;
    private static string? _lastLoggedManagedFolder;

    /** Export folder names in the model store, cached briefly for the Model dropdown. */
    private static string[]? _validExportNames;
    private static long _validExportScanTicks;

    private static string[] GetValidExportNames()
    {
        var now = Environment.TickCount64;
        if (_validExportNames != null && now - _validExportScanTicks < ManagedModelScanIntervalMs)
            return _validExportNames;

        var names = new List<string>();
        var familyDirectory = T3.Core.Settings.AiModelSettings.FamilyDirectory(T3.Core.Settings.AiModelSettings.StreamDiffusionFamilyFolder);
        if (Directory.Exists(familyDirectory))
        {
            try
            {
                foreach (var directory in Directory.GetDirectories(familyDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    if (StreamDiffusionV2Pipeline.ValidateModelDirectory(directory).Length == 0)
                        names.Add(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)));
                }
            }
            catch
            {
                // An unreadable store folder leaves the dropdown empty rather than failing the frame
            }
        }

        _validExportNames = names.ToArray();
        _validExportScanTicks = now;
        return _validExportNames;
    }

    /** The first folder in the model store that passes V2 validation, or null. */
    private static string? FindManagedModelFolder()
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks - ManagedFolderCache.ScannedAtTicks < ManagedModelScanIntervalMs)
            return ManagedFolderCache.Folder;

        var found = ScanManagedModelFolders();
        ManagedFolderCache = (found, nowTicks);
        return found;
    }

    private static string? ScanManagedModelFolders()
    {
        var familyDirectory = T3.Core.Settings.AiModelSettings.FamilyDirectory(T3.Core.Settings.AiModelSettings.StreamDiffusionFamilyFolder);
        if (!Directory.Exists(familyDirectory))
            return null;

        try
        {
            foreach (var directory in Directory.GetDirectories(familyDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var error = StreamDiffusionV2Pipeline.ValidateModelDirectory(directory);
                if (error.Length == 0)
                {
                    if (Interlocked.CompareExchange(ref _lastLoggedManagedFolder, directory, null) != directory)
                    {
                        Log.Debug($"[StreamDiffusion] No engine path set - using managed model folder '{directory}'");
                    }
                    return directory;
                }
            }
        }
        catch
        {
            // An unreadable store folder is not worth failing the update over
        }

        return null;
    }

    private void InitializePipelineWorker(string? modelPath, int deviceId, int provider)
    {
        var previousTask = _processingTask;
        StopWorker();

        var validationError = StreamDiffusionV2Pipeline.ValidateModelDirectory(modelPath);
        if (validationError.Length > 0)
        {
            _lastStatusMessage = validationError;
            _initFailed = true;
            _failedInputs = new FailureKey(modelPath, deviceId, provider);
            Log.Warning($"[StreamDiffusion] {validationError}", this);
            return;
        }

        _isInitializing = true;
        _initFailed = false;
        _activeModelPath = modelPath;
        _activeDeviceId = deviceId;
        _activeProvider = provider;
        _failedInputs = null;
        _hasPendingInput = true;

        var generation = Interlocked.Increment(ref _workerGeneration);
        _workerCts = new CancellationTokenSource();
        var token = _workerCts.Token;
        _processingTask = Task.Run(async () =>
        {
            // The previous generation releases its ONNX sessions only when its loop exits. Waiting here keeps the
            // render thread free while still preventing two sessions from holding GPU memory at the same time.
            if (previousTask != null)
            {
                try
                {
                    await previousTask.ConfigureAwait(false);
                }
                catch
                {
                    // A failing predecessor must not prevent this generation from starting
                }
            }

            if (token.IsCancellationRequested)
                return;

            WorkerLoop(modelPath!, deviceId, provider, token, generation);
        }, CancellationToken.None);
    }

    private void StopWorker()
    {
        try
        {
            _workerCts?.Cancel();
        }
        catch { }

        _workerWake.Set();

        // Never wait for the worker on the render thread: inference can take seconds. InitializePipelineWorker
        // chains the next worker after this task, so the old pipeline still releases its GPU memory first.

        while (_inputQueue.TryDequeue(out var req))
        {
            if (req.InputData != null)
                ReturnExtractBuffer(req.InputData);
        }

        while (_outputQueue.TryDequeue(out _)) { }

        lock (_stateLock)
        {
            _pipeline = null;
        }

        _requestInFlight = false;
        _hasPendingInput = false;
        _readbackPending = false;
    }

    private void WorkerLoop(string modelPath, int deviceId, int provider, CancellationToken token, int generation)
    {
        // Only the current worker touches the fingerprint fields from here on: the previous worker is
        // provably dead (this task awaited it), so the reset cannot be overwritten by a stale write.
        // Folding the generation into the fingerprint (see ComputeFingerprint) also keeps a swapped
        // model/device/provider from skipping the first request.
        _hasProcessedFingerprint = false;

        StreamDiffusionV2Pipeline? pipeline = null;
        var failedToInit = false;
        try
        {
            var streamPipeline = new StreamDiffusionV2Pipeline();
            pipeline = streamPipeline;
            var initialized = streamPipeline.Initialize(modelPath, deviceId, (t3.streamdiffusion.Onnx.ExecutionProvider)provider);

            pipeline.VerboseLogging = _debugLogging;

            if (!initialized)
            {
                failedToInit = true;

                // Initialization finished (with failure) - unblock the render thread immediately.
                // _isInitializing must not stay set while the worker keeps serving requests,
                // otherwise Evaluate() early-returns forever and no input ever reaches the pipeline.
                if (generation == Volatile.Read(ref _workerGeneration))
                {
                    _isInitializing = false;
                    _initFailed = true;
                    _failedInputs = new FailureKey(_activeModelPath, _activeDeviceId, _activeProvider);
                    _lastStatusMessage = pipeline?.InitializationError ?? "Failed to initialize ONNX pipeline. See log for details.";
                }
            }
            else
            {
                lock (_stateLock)
                {
                    _pipeline = pipeline;
                }

                // Initialization finished successfully - see note above.
                if (generation == Volatile.Read(ref _workerGeneration))
                    _isInitializing = false;

                _lastStatusMessage = null;
                Log.Info($"[StreamDiffusion] Pipeline ready ({pipeline.ProviderLabel}, StreamDiffusion V2)", this);
            }

            while (!token.IsCancellationRequested)
            {
                if (_inputQueue.TryDequeue(out var request))
                {
                    if (request != null && pipeline != null)
                    {
                        // Float render targets are converted here rather than on the render thread; the raw
                        // staging buffer is returned before the request reaches the pipeline.
                        request = ConvertRequestInput(request);
                        var result = ProcessGenerationRequest(pipeline, request, generation);
                        if (!token.IsCancellationRequested && result != null)
                            _outputQueue.Enqueue(result with { Generation = generation });
                    }

                    if (request?.InputData != null)
                        ReturnExtractBuffer(request.InputData);

                    _requestInFlight = false;
                }
                else
                {
                    _workerWake.WaitOne(15);
                }
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Log.Error($"[StreamDiffusion] Worker loop error: {ex.Message}", this);
                _outputQueue.Enqueue(new GenerationResult(null, 0, 0, 0, $"Worker error: {ex.Message}", generation));

                // An exception before a pipeline was published is an initialization failure. Without
                // latching it the retry gate sees "worker gone, same inputs" as a fresh attempt and
                // reconstructs every ONNX session on the next frame.
                if (pipeline == null)
                    failedToInit = true;
            }

            _requestInFlight = false;
        }
        finally
        {
            // Worker owns its pipeline - dispose unconditionally; a newer worker may have replaced the field
            pipeline?.Dispose();

            lock (_stateLock)
            {
                if (ReferenceEquals(_pipeline, pipeline))
                    _pipeline = null;
            }

            // Stale generations must not clobber their successor's init state
            if (generation == Volatile.Read(ref _workerGeneration))
            {
                _isInitializing = false;
                if (failedToInit)
                {
                    _initFailed = true;
                    _failedInputs = new FailureKey(_activeModelPath, _activeDeviceId, _activeProvider);
                    _lastStatusMessage = pipeline?.InitializationError ?? "Failed to initialize ONNX pipeline. See log for details.";
                }
            }

            if (_debugLogging)
                Log.Debug("[StreamDiffusion] Worker loop stopped", this);
        }
    }

    /// <summary>
    /// Converts raw float render-target rows to RGBA8. This runs on the worker: the render thread only copies the
    /// mapped rows out of the staging texture, so neither the per-pixel conversion nor its sRGB encode stalls a frame.
    /// </summary>
    private GenerationRequest ConvertRequestInput(GenerationRequest request)
    {
        if (request.InputData == null
            || request.InputFormat is InputPixelFormat.Rgba8 or InputPixelFormat.Bgra8
            || request.InputWidth <= 0 || request.InputHeight <= 0)
        {
            return request;
        }

        var halfFloats = request.InputFormat == InputPixelFormat.Rgba16F;
        var pixelSize = halfFloats ? 8 : 16;
        var requiredBytes = request.InputWidth * request.InputHeight * pixelSize;
        if (request.InputData.Length < requiredBytes)
            return request;

        var converted = RentExtractBuffer(request.InputWidth * request.InputHeight * 4);
        ConvertFloatToRgba8(request.InputData, converted, request.InputWidth, request.InputHeight, halfFloats);
        ReturnExtractBuffer(request.InputData);
        return request with { InputData = converted, InputFormat = InputPixelFormat.Rgba8 };
    }

    private GenerationResult? ProcessGenerationRequest(StreamDiffusionV2Pipeline pipeline, GenerationRequest request, int generation)
    {
        try
        {
            if (request.ResetStream)
                pipeline.ResetStream();

            pipeline.ConfigureStream(request.Steps);

            // Negative seeds randomize per call, so only fixed-seed requests are safely skippable there.
            // The stream consumes one new input frame per request regardless of seed: an unchanged frame
            // can reuse the previous result, which also stops a frozen source from keeping the GPU busy.
            var canSkipIdentical = request.Seed >= 0;
            if (canSkipIdentical)
            {
                // A prompt that was swapped in by the background encoder must produce one new frame even when
                // the input pixels did not change; the version is part of the fingerprint. The worker generation
                // and the FastDecode state are folded in as well, so a swapped model or decode-window change
                // always produces one fresh frame - without the render thread writing any worker-side state.
                var promptVersion = pipeline.PromptVersion;
                var fingerprintSalt = generation;
                if (pipeline.FastDecodeEnabled)
                    fingerprintSalt ^= unchecked((int)0x5A5A5A5A);
                if (request.ResetStream)
                    fingerprintSalt ^= unchecked((int)0x2B3C4D5E);

                var fingerprint = ComputeFingerprint(request, promptVersion, fingerprintSalt);
                if (_hasProcessedFingerprint && fingerprint == _lastProcessedFingerprint)
                    return null; // identical request - keep previous output, spare the GPU

                _lastProcessedFingerprint = fingerprint;
                _hasProcessedFingerprint = true;
            }

            if (request.InputData == null)
            {
                return new GenerationResult(null, request.Width, request.Height, 0, "No input frame reached the pipeline");
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var imageData = pipeline.StreamStep(
                request.Prompt, request.InputData, request.InputWidth, request.InputHeight,
                request.Strength, request.ResizeMode, request.Seed,
                request.InputFormat == InputPixelFormat.Bgra8, out var error);

            sw.Stop();

            if (imageData == null && error == null)
                error = "Pipeline returned null";

            var outWidth = request.Width;
            var outHeight = request.Height;
            byte[]? pooledBuffer = null;

            // MatchInputAspect: stretch the square baked output back to the input frame's aspect ratio at model
            // resolution (256x256 -> 455x256 for 16:9); the GPU sampler scales it further where it is used.
            // The target buffer is pooled: a fresh allocation per frame at 12+ fps made the GC stall the worker mid-step.
            if (imageData != null && request.MatchInputAspect && request.InputWidth > 0 && request.InputHeight > 0)
            {
                var inputAspect = (float)request.InputWidth / request.InputHeight;
                var shortSide = Math.Min(request.Width, request.Height);
                var targetWidth = inputAspect >= 1f ? Math.Max(1, (int)MathF.Round(shortSide * inputAspect)) : shortSide;
                var targetHeight = inputAspect >= 1f ? shortSide : Math.Max(1, (int)MathF.Round(shortSide / inputAspect));
                if (targetWidth != request.Width || targetHeight != request.Height)
                {
                    pooledBuffer = RentOutputBuffer(targetWidth * targetHeight * 4);
                    RgbaResize.ApplyInto(pooledBuffer, imageData, request.Width, request.Height, targetWidth, targetHeight);
                    imageData = pooledBuffer;
                    outWidth = targetWidth;
                    outHeight = targetHeight;
                }
            }

            return new GenerationResult(imageData, outWidth, outHeight, (float)sw.Elapsed.TotalSeconds, error, PooledBuffer: pooledBuffer);
        }
        catch (Exception ex)
        {
            Log.Error($"[StreamDiffusion] ProcessGenerationRequest failed: {ex.Message}", this);
            return new GenerationResult(null, request.Width, request.Height, 0, $"Error: {ex.Message}");
        }
    }

    /**
     * Typing into the prompt field changes it per keystroke, and every change starts a multi-second text encode that
     * blocks the next one. Only a prompt that stayed unchanged for <see cref="PromptSettleSec"/> is passed on.
     */
    private string SettlePrompt(string? prompt)
    {
        prompt ??= string.Empty;
        var nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!string.Equals(prompt, _typedPrompt, StringComparison.Ordinal))
        {
            _typedPrompt = prompt;
            _typedPromptChangedTicks = nowTicks;
        }

        if (_settledPrompt == null
            || nowTicks - _typedPromptChangedTicks >= (long)(System.Diagnostics.Stopwatch.Frequency * PromptSettleSec))
        {
            _settledPrompt = prompt;
        }

        return _settledPrompt;
    }

    private int ComputeFingerprint(GenerationRequest request, int promptVersion, int fingerprintSalt)
    {
        unchecked
        {
            var hash = (int)HashCode.Combine(
                request.Prompt, request.Steps, request.Strength, request.Seed,
                request.Width, request.Height);

            hash = (hash * 31) ^ fingerprintSalt;
            hash = (hash * 31) ^ request.ResizeMode;
            hash = (hash * 31) ^ request.InputWidth;
            hash = (hash * 31) ^ request.InputHeight;
            hash = (hash * 31) ^ request.InputFormat.GetHashCode();
            hash = (hash * 31) ^ promptVersion;

            if (request.InputData == null || request.InputWidth == 0 || request.InputHeight == 0)
                return hash;

            // Hash every word for model-sized frames and a bounded subset for large inputs. The previous
            // 16x16 sample could miss a small moving object and freeze a live feed on a false "identical".
            var pixelCount = request.InputWidth * request.InputHeight;
            var words = MemoryMarshal.Cast<byte, uint>(request.InputData.AsSpan(0, pixelCount * 4));
            var step = Math.Max(1, words.Length / 262144);
            for (var index = 0; index < words.Length; index += step)
                hash = hash * 31 + (int)words[index];

            hash = hash * 31 + (int)words[^1];
            return hash;
        }
    }

    #endregion

    #region Deferred readback (DepthAnything port)

    private void BeginReadback(Texture2D texture)
    {
        if (texture.IsDisposed)
        {
            _hasPendingInput = false;
            return;
        }

        var desc = texture.Description;

        switch (desc.Format)
        {
            case Format.R8G8B8A8_UNorm:
            case Format.R8G8B8A8_UNorm_SRgb:
                _readbackFormat = InputPixelFormat.Rgba8;
                break;
            case Format.B8G8R8A8_UNorm:
            case Format.B8G8R8A8_UNorm_SRgb:
                _readbackFormat = InputPixelFormat.Bgra8;
                break;
            case Format.R16G16B16A16_Float:
                _readbackFormat = InputPixelFormat.Rgba16F;
                break;
            case Format.R32G32B32A32_Float:
                _readbackFormat = InputPixelFormat.Rgba32F;
                break;
            default:
                if (!_loggedUnsupportedFormat)
                {
                    _loggedUnsupportedFormat = true;
                    Log.Warning($"[StreamDiffusion] Unsupported input format {desc.Format} - skipping frames. " +
                                "Connect an RGBA image or a float render target.", this);
                }
                _hasPendingInput = false;
                return;
        }

        var device = ResourceManager.Device;
        _readbackTexture = GetOrCreateStagingTexture(desc.Width, desc.Height, desc.Format);
        device.ImmediateContext.CopyResource(texture, _readbackTexture);

        _readbackPending = true;
        _readbackWaitFrames = 0;
    }

    private (byte[]? data, int width, int height, InputPixelFormat format) CompleteReadback()
    {
        var context = ResourceManager.Device.ImmediateContext;

        // TryMapForRead only succeeds once the copy is finished, so polling never stalls the render thread.
        var stagingTexture = _readbackTexture;
        var format = _readbackFormat;
        if (stagingTexture == null)
        {
            _readbackPending = false;
            return (null, 0, 0, format);
        }

        T3.Graphics.Compat.Texture2D nativeStaging = stagingTexture;
        if (nativeStaging.Native == null
            || !ResourceManager.Device.Backend.TryMapForRead(nativeStaging.Native, 0, out var mapped))
        {
            _readbackWaitFrames++;
            if (_readbackWaitFrames > ReadbackTimeoutFrames)
            {
                Log.Warning("[StreamDiffusion] Readback never completed - restarting readback", this);
                _readbackPending = false;
                _hasPendingInput = true;
            }
            return (null, 0, 0, format);
        }

        _readbackPending = false;
        var desc = stagingTexture.Description;
        int width = desc.Width;
        int height = desc.Height;

        try
        {
            if (mapped.SizeInBytes <= 0)
                return (null, 0, 0, format);

            // Float rows are copied out raw; the worker converts them to RGBA8 so no per-pixel work runs here
            var pixelSize = BytesPerPixel(format);
            var buffer = RentExtractBuffer(width * height * pixelSize);
            unsafe
            {
                var srcPtr = (byte*)mapped.Data;
                var rowPitch = mapped.RowPitch;
                var rowBytes = width * pixelSize;
                fixed (byte* dst = buffer)
                {
                    for (var y = 0; y < height; y++)
                    {
                        System.Buffer.MemoryCopy(srcPtr + (long)y * rowPitch, dst + (long)y * rowBytes, rowBytes, rowBytes);
                    }
                }
            }

            return (buffer, width, height, format);
        }
        finally
        {
            context.UnmapSubresource(stagingTexture, 0);
        }
    }

    private static int BytesPerPixel(InputPixelFormat format)
    {
        return format switch
        {
            InputPixelFormat.Rgba16F => 8,
            InputPixelFormat.Rgba32F => 16,
            _ => 4
        };
    }

    /// <summary>
    /// Converts linear RGBA render-target rows to encoded 8-bit RGBA. Float render targets hold linear color, while the
    /// image loaders and the diffusion models work in encoded sRGB, so the values have to be encoded on the way out.
    /// Runs on the worker thread.
    /// </summary>
    private static void ConvertFloatToRgba8(byte[] source, byte[] destination, int width, int height, bool halfFloats)
    {
        var pixelSize = halfFloats ? 8 : 16;
        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * width * pixelSize;
            var targetRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var pixel = source.AsSpan(sourceRow + x * pixelSize, pixelSize);
                var target = targetRow + x * 4;
                if (halfFloats)
                {
                    destination[target + 0] = EncodeSrgb((float)MemoryMarshal.Read<Half>(pixel));
                    destination[target + 1] = EncodeSrgb((float)MemoryMarshal.Read<Half>(pixel[2..]));
                    destination[target + 2] = EncodeSrgb((float)MemoryMarshal.Read<Half>(pixel[4..]));
                }
                else
                {
                    destination[target + 0] = EncodeSrgb(MemoryMarshal.Read<float>(pixel));
                    destination[target + 1] = EncodeSrgb(MemoryMarshal.Read<float>(pixel[4..]));
                    destination[target + 2] = EncodeSrgb(MemoryMarshal.Read<float>(pixel[8..]));
                }

                destination[target + 3] = 255;
            }
        }
    }

    private static byte EncodeSrgb(float linear)
    {
        // Also routes NaN and negative values to black
        if (!(linear > 0f))
            return 0;
        if (linear >= 1f)
            return byte.MaxValue;

        return SrgbEncodeTable[(int)(linear * (SrgbTableSize - 1) + 0.5f)];
    }

    private static byte[] CreateSrgbEncodeTable()
    {
        var table = new byte[SrgbTableSize];
        for (var index = 0; index < table.Length; index++)
        {
            var linear = index / (float)(SrgbTableSize - 1);
            var encoded = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
            table[index] = (byte)(encoded * 255f + 0.5f);
        }

        return table;
    }

    #endregion

    #region Persistent output texture (DepthAnything port)

    // Overload: upload RGBA texture directly from a GPU pointer (zero‑copy path).
    private unsafe void UploadRgbaTexture(IntPtr gpuPtr, int width, int height)
    {
        // Ensure the output texture exists with correct dimensions.
        if (_outputTexture == null || _outputTexture.Description.Width != width || _outputTexture.Description.Height != height)
        {
            _outputTexture?.Dispose();
            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = ResourceOptionFlags.None
            };
            _outputTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, desc));
        }

        // Create a DataBox that points to the GPU memory.
        var dataBox = new DataBox(gpuPtr, width * 4, 0);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, _outputTexture);
    }

    // Compatibility wrapper: upload from managed byte[] by pinning and delegating to the pointer overload.
    private void UploadRgbaTexture(byte[] rgbaData, int width, int height)
    {
        unsafe
        {
            fixed (byte* ptr = rgbaData)
            {
                UploadRgbaTexture((IntPtr)ptr, width, height);
            }
        }
    }

    #endregion

    #region Staging + buffer pool

    private Texture2D GetOrCreateStagingTexture(int width, int height, Format format)
    {
        // One staging texture is enough: readbacks are single-in-flight, and a new input size replaces the
        // buffer instead of keeping every size the session has ever seen alive.
        if (_stagingTexture is { IsDisposed: false }
            && _stagingTexture.Description.Width == width
            && _stagingTexture.Description.Height == height
            && _stagingTexture.Description.Format == format)
        {
            return _stagingTexture;
        }

        _stagingTexture?.Dispose();
        _stagingTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        }));

        return _stagingTexture;
    }

    /** Output buffers for the MatchInputAspect resize, pooled because a fresh multi-megabyte
     *  allocation per frame starves the GC and stalls the worker mid-inference. Returned from the
     *  render thread only after the upload consumed the buffer. */
    private readonly Stack<byte[]> _outputBufferPool = new();

    private byte[] RentOutputBuffer(int minimumSize)
    {
        lock (_poolLock)
        {
            while (_outputBufferPool.Count > 0)
            {
                var buffer = _outputBufferPool.Pop();
                if (buffer.Length >= minimumSize)
                    return buffer;
            }
        }

        return new byte[minimumSize];
    }

    private void ReturnOutputBuffer(byte[]? buffer)
    {
        if (buffer == null)
            return;

        lock (_poolLock)
        {
            if (_outputBufferPool.Count < 2)
                _outputBufferPool.Push(buffer);
        }
    }

    private byte[] RentExtractBuffer(int minimumSize)
    {
        lock (_poolLock)
        {
            while (_extractBufferPool.Count > 0)
            {
                var buffer = _extractBufferPool.Pop();
                if (buffer.Length >= minimumSize)
                    return buffer;
            }
        }

        return new byte[minimumSize];
    }

    private void ReturnExtractBuffer(byte[] buffer)
    {
        lock (_poolLock)
        {
            if (_extractBufferPool.Count < 4)
                _extractBufferPool.Push(buffer);
        }
    }

    #endregion

    #region IStatusProvider

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        if (_isInitializing)
            return IStatusProvider.StatusLevel.Warning;

        var pipeline = _pipeline;
        if (pipeline == null || _initFailed)
            return IStatusProvider.StatusLevel.Warning;

        try
        {
            // A pipeline whose provider fallback failed is still published but cannot stream; the status text
            // already says "Initialization failed", so the light must not stay green.
            if (!pipeline.IsInitialized)
                return IStatusProvider.StatusLevel.Warning;
        }
        catch (ObjectDisposedException)
        {
            return IStatusProvider.StatusLevel.Warning;
        }

        return string.IsNullOrEmpty(_lastStatusMessage)
            ? IStatusProvider.StatusLevel.Success
            : IStatusProvider.StatusLevel.Warning;
    }

    public string? GetStatusMessage()
    {
        if (_isInitializing)
        {
            return _activeProvider == (int)t3.streamdiffusion.Onnx.ExecutionProvider.TensorRt
                       ? "Loading model... (TensorRT builds its engines on first use - this can take 10+ minutes; CUDA starts instantly)"
                       : "Loading model...";
        }

        var pipeline = _pipeline;
        if (pipeline == null)
            return _lastStatusMessage ?? "Not initialized";

        try
        {
            if (!pipeline.IsInitialized)
                return "Initialization failed";

            if (!string.IsNullOrEmpty(_lastStatusMessage))
                return _lastStatusMessage;

            var detail = string.Empty;
            if (pipeline is StreamDiffusionV2Pipeline v2)
            {
                var total = v2.LastEncodeMs + v2.LastDenoiseMs + v2.LastDecodeMs;
                if (total > 0)
                    detail = $", {total:F0} ms (enc {v2.LastEncodeMs:F0} / dit {v2.LastDenoiseMs:F0} / dec {v2.LastDecodeMs:F0})";

                if (v2.IsPromptPending)
                    detail += ", encoding new prompt";
            }

            return $"Ready ({pipeline.ProviderLabel}{detail})";
        }
        catch (ObjectDisposedException)
        {
            return "Not initialized";
        }
    }

    #endregion

    #region ICustomDropdownHolder

    public string GetValueForInput(Guid inputId)
    {
        if (inputId == ResizeMode.Id)
        {
            var mode = _uiResizeMode;
            return mode >= 0 && mode < ResizeModeNames.Length ? ResizeModeNames[mode] : "Unknown";
        }

        if (inputId == ExecutionProvider.Id)
        {
            var raw = (t3.streamdiffusion.Onnx.ExecutionProvider)_uiRawExecutionProvider;
            if (raw == t3.streamdiffusion.Onnx.ExecutionProvider.Auto)
            {
                var resolved = (t3.streamdiffusion.Onnx.ExecutionProvider)_uiExecutionProvider;
                var resolvedName = ExecutionProviderOptions.FirstOrDefault(o => o.Provider == resolved).Name ?? "CPU";
                return $"Auto ({resolvedName})";
            }

            var provider = (t3.streamdiffusion.Onnx.ExecutionProvider)_uiExecutionProvider;
            return ExecutionProviderOptions.FirstOrDefault(o => o.Provider == provider).Name ?? "CPU";
        }

        if (inputId == Model.Id)
        {
            var value = Model.Value;
            if (string.IsNullOrEmpty(value))
            {
                var auto = FindManagedModelFolder();
                return auto == null ? "No export in model store" : Path.GetFileName(auto.TrimEnd(Path.DirectorySeparatorChar)) + " (auto)";
            }

            return value;
        }

        if (inputId == CudaDevice.Id)
        {
            var deviceId = CudaDevice.Value;
            var names = GetDeviceNames();
            return deviceId >= 0 && deviceId < names.Length ? $"{deviceId}: {names[deviceId]}" : PlaceholderTextDevice;
        }

        return "";
    }

    public IEnumerable<string> GetOptionsForInput(Guid inputId)
    {
        if (inputId == ResizeMode.Id)
        {
            foreach (var mode in ResizeModeNames)
                yield return mode;
            yield break;
        }

        if (inputId == ExecutionProvider.Id)
        {
            foreach (var option in ExecutionProviderOptions)
                yield return option.Name;
            yield break;
        }

        if (inputId == Model.Id)
        {
            yield return AutoModelOption;
            foreach (var name in GetValidExportNames())
                yield return name;
            yield break;
        }

        if (inputId == CudaDevice.Id)
        {
            var names = GetDeviceNames();
            for (var i = 0; i < names.Length; i++)
                yield return $"{i}: {names[i]}";
            yield break;
        }

        yield return "undefined";
    }

    public void HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected))
            return;

        if (inputId == ResizeMode.Id)
        {
            var index = Array.IndexOf(ResizeModeNames, selected);
            if (index >= 0)
                ResizeMode.SetTypedInputValue(index);
            return;
        }

        if (inputId == Model.Id)
        {
            var model = selected == AutoModelOption ? string.Empty : selected;
            Model.SetTypedInputValue(model);
            return;
        }

        if (inputId == CudaDevice.Id && selected != PlaceholderTextDevice)
        {
            var separatorIndex = selected.IndexOf(':');
            if (separatorIndex > 0 && int.TryParse(selected.AsSpan(0, separatorIndex), out var deviceId))
                CudaDevice.SetTypedInputValue(deviceId);
        }

        if (inputId == ExecutionProvider.Id)
        {
            var index = Array.FindIndex(ExecutionProviderOptions, o => o.Name == selected);
            if (index >= 0)
                ExecutionProvider.SetTypedInputValue((int)ExecutionProviderOptions[index].Provider);
        }
    }

    #endregion

    private static string[]? _cachedDeviceNames;
    private static readonly object DeviceNameCacheLock = new();

    private static string[] GetDeviceNames()
    {
        if (_cachedDeviceNames != null)
            return _cachedDeviceNames;

        // Adapter enumeration can run from any instance's dropdown on any thread, so the lazy cache is built under a lock
        lock (DeviceNameCacheLock)
        {
            if (_cachedDeviceNames != null)
                return _cachedDeviceNames;

            var names = new List<string>();
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    AddDxgiAdapterNames(names);
                }
            }
            catch
            {
                // Adapter names are only labels for the device dropdown
            }

            _cachedDeviceNames = names.Count > 0 ? names.ToArray() : ["Default"];
        }

        return _cachedDeviceNames;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddDxgiAdapterNames(List<string> names)
    {
        using var factory = new SharpDX.DXGI.Factory1();
        foreach (var adapter in factory.Adapters)
        {
            names.Add(adapter.Description.Description.Trim());
            adapter.Dispose();
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        try
        {
            _workerCts?.Cancel();
        }
        catch { }

        _workerWake.Set();

        try
        {
            _processingTask?.Wait(0);
        }
        catch { }

        while (_inputQueue.TryDequeue(out var req))
        {
            if (req.InputData != null)
                ReturnExtractBuffer(req.InputData);
        }

        while (_outputQueue.TryDequeue(out _)) { }

        _outputTexture?.Dispose();
        _outputTexture = null;


        _stagingTexture?.Dispose();
        _stagingTexture = null;

        lock (_poolLock)
        {
            _extractBufferPool.Clear();
        }

        // Do not dispose _workerWake - an exiting worker may still wait on it.
        // Do not dispose _pipeline - the worker owns it.

        base.Dispose(isDisposing);
    }
}

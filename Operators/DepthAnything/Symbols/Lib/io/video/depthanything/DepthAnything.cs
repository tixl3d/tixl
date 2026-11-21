using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lib.io.video.depthanything.Utils;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using T3.Core.Logging;
using T3.Core.Utils;
using T3.Graphics.Compat;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Operator.Interfaces;
using T3.Core.Rendering;

#pragma warning disable CS8981 // Disable warning about type name 'depthanything' being lowercase only

namespace Lib.io.video.depthanything;

internal sealed class DepthEstimationRequest
{
    public float[]? InputData;
    public byte[]? InputBytes;
    public int Width;
    public int Height;
    public int SourceWidth;
    public int SourceHeight;
    /** Black rows above and below the image in a letterboxed input; the depth planes are cropped back to the image rows. */
    public int PadRows;
    public long CaptureTicks;
    public int Generation;
}

internal sealed class DepthEstimationResult
{
    public float[]? DepthData;
    public float[]? ConfidenceData;
    public float[]? SkyData;
    public byte[]? ColorData;
    public int Width;
    public int Height;
    public float Min;
    public float Max;
    public float VerticalFovDegrees;
    public bool HasCameraPose;
    /** False while the pose tracker has lost the view (predicted or frozen pose); scans must not place new points then. */
    public bool PoseTrusted = true;
    public Vector3 CameraPosition;
    public Vector3 CameraForward;
    public Vector3 CameraUp;
    public float InferenceMs;
    /** CPU work after inference on the worker (copies, smoothing, pose tracking, range, colorize); part of the latency. */
    public float PostMs;
    public float PoseMs;
    public long CaptureTicks;
    public DepthAnything.DepthOutputFormat ColorFormat;
    public bool ColorEnhance;
    public bool ColorInvert;
    public bool FarIsLarge;
    public int Generation;
    public string? Error;
    public string PoseSource = "";
}

[Guid("c8f7d6e5-4b3a-2c1d-9f8e-7a6b5c4d3e2f")]
[ExportDependencies("Microsoft.ML.OnnxRuntime.dll", "onnxruntime.dll", "libonnxruntime*")]
public class DepthAnything : Instance<DepthAnything>, ICustomDropdownHolder, ICamera, ICameraPropertiesProvider
{
    #region Outputs

    [Output(Guid = "d9e8f7a6-5c4b-3d2e-0f9e-8b7a6c5d4e3f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> OutputTexture = new();

    [Output(Guid = "e0f9e8b7-6d5c-4e3f-1a0f-9c8b7a6d5e4f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> DepthTexture = new();

    [Output(Guid = "f1a0f9c8-7e6d-5f4e-2b1a-0d9c8b7a6e5f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> NormalizedDepthTexture = new();

    [Output(Guid = "3b7e1c42-8a5d-4f60-9d21-6c0e5a7b8f13", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> ConfidenceTexture = new();

    [Output(Guid = "5d9a2e74-1b3c-4a86-b7f0-2e8c4d6a9b35", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> SkyTexture = new();

    [Output(Guid = "a2b1c0d9-8f7e-6d5c-3c2b-1e0d9c8b7f6a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> UpdateCount = new();

    [Output(Guid = "b3c2d1e0-0f9f-8e7d-4d3c-2f1e0d9c8a7b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> MinDepth = new();

    [Output(Guid = "c4d3e2f1-1a0b-9f8e-5e4d-3f2e1d0b9a8c", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> MaxDepth = new();

    [Output(Guid = "7f1c4b96-3d5e-4c08-a9b2-4a0e6c8d1f57", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> VerticalFov = new();

    [Output(Guid = "91e3d6b8-5f7a-4e2a-8c14-6c2a8e0f3b79", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> InferenceMs = new();

    [Output(Guid = "b3a5f8da-7c9b-4a4c-9e36-8e4c0a2b5d9b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> LatencyMs = new();

    [Output(Guid = "d5e6f7a2-1b2c-0d9e-8f7a-6b5c4d3e2f1a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Status = new();

    /// <summary>
    /// The camera estimated by the model as an <see cref="ICamera"/>. The model outputs
    /// world-to-camera extrinsics and a 3x3 intrinsics matrix; the pose is published here
    /// in DA3's image coordinate frame (camera +Z forward, camera -Y up). Connect it to
    /// camera-reference inputs such as ApplyCamTransform or SortPoints.
    /// </summary>
    [Output(Guid = "5a3334ec-78c3-4001-8018-3d8aae7f870f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Object> CamReference = new();

    /** False while the image tracker of a pan/tilt camera has lost the view; DepthScan3D skips captures then. */
    public bool PoseTrusted { get; private set; } = true;

    #endregion

    /** What the render thread needs to know about the loaded model to prepare input for it. */
    private sealed class ModelInfo
    {
        public int DeclaredWidth;
        public int DeclaredHeight;
        public bool IsMetric;

        /** uint8 NHWC input that the graph normalizes itself; otherwise float32 NCHW. */
        public bool InputIsBytes;

        /** 2 for the multi-view export, which takes the previous and the current frame as one [1,2,3,H,W] input and predicts their relative pose. */
        public int ViewCount = 1;
    }

    private readonly record struct ModelFile(string Name, bool IsMetric);

    // Everything a worker generation touches exclusively; isolates an exiting
    // worker from its replacement during model/provider switches
    private sealed class WorkerContext
    {
        public InferenceSession? Session;
        public RunOptions? RunOptions;
        public string[] InputNames = ["pixel_values"];
        public string[] OutputNames = ["predicted_depth"];
        public readonly OrtValue?[] InputValues = new OrtValue?[1];
        public int DepthIndex;
        public int ConfidenceIndex = -1;
        public int SkyIndex = -1;
        public int IntrinsicsIndex = -1;
        public int ExtrinsicsIndex = -1;
        public bool LoggedFirstInference;
        public float[]? PreviousDepth;
        public int PreviousWidth;
        public int PreviousHeight;
        public bool HasRange;
        public float SmoothedMin;
        public float SmoothedMax;

        public readonly DepthPoseTracker PoseTracker = new();
        public byte[]? GrayFrame;

        // Multi-view model: the previous input frame, the reusable pair tensor and the pose chained over all pairs so far
        public float[]? PreviousInput;
        public float[]? PairInput;
        public int PreviousInputLength;
        public bool HasPreviousView;
        public readonly float[] WorldRotation = [1, 0, 0, 0, 1, 0, 0, 0, 1];
        public Vector3 WorldPosition;
        public float WorldScale = 1f;
        // N-view window: the previous keyframes, the reusable window tensor and the chain that places each window
        public float[][]? Ring;
        public float[]? WindowInput;
        public int RingViewLength;
        public int ChunkShift;
        public DepthChunkChain? Chain;
        public float[]? ChunkDepth;
        public float[]? ChunkConfidence;
        public float[]? ChunkExtrinsics;
        public float[]? ChunkIntrinsics;

        public readonly float[] KeyRotation = [1, 0, 0, 0, 1, 0, 0, 0, 1];
        public Vector3 KeyPosition;
        public float KeyWorldDepth;
        public readonly float[] LastRotation = [1, 0, 0, 0, 1, 0, 0, 0, 1];
        public Vector3 LastPosition;
        public int JumpCount;
        public readonly float[] MedianScratch = new float[MedianSamples];

        // extrinsics-aliveness: the community exports publish an identity matrix every frame;
        // when the model's own pose output actually moves we prefer it over depth tracking
        public Vector3 LastExtPosition;
        public Vector3 LastExtForward;
        public bool HasLastExt;
        public int InertFrames;
    }

    public DepthAnything()
    {
        OutputTexture.UpdateAction = Update;
        DepthTexture.UpdateAction = Update;
        NormalizedDepthTexture.UpdateAction = Update;
        ConfidenceTexture.UpdateAction = Update;
        SkyTexture.UpdateAction = Update;
        UpdateCount.UpdateAction = Update;
        MinDepth.UpdateAction = Update;
        MaxDepth.UpdateAction = Update;
        VerticalFov.UpdateAction = Update;
        InferenceMs.UpdateAction = Update;
        LatencyMs.UpdateAction = Update;
        Status.UpdateAction = Update;
        CamReference.UpdateAction = Update;
        CamReference.Value = this;
    }

    private void Update(EvaluationContext context)
    {
        var pendingStatus = _pendingStatus;
        if (pendingStatus != null)
        {
            Status.Value = pendingStatus;
            _pendingStatus = null;
        }

        var inputDirty = InputTexture.DirtyFlag.IsDirty;
        var inputTexture = InputTexture.GetValue(context);
        var enabled = Enabled.GetValue(context);
        var debug = Debug.GetValue(context);
        var modelSize = NormalizeModelSize((ModelSize)ModelSizeParam.GetValue(context));
        var provider = (ExecutionProvider)ExecutionProviderParam.GetValue(context);
        _uiRawExecutionProvider = (int)provider;

        // A provider that cannot run on this OS or with the shared ORT 1.30 build (saved DirectML,
        // a CoreML on Windows/Linux...) snaps onto the closest one that can, so the dropdown and
        // the label always show a real option.
        provider = NormalizeProviderForOs(provider);
        _requestedInputSize = ((Resolution)InputResolution.GetValue(context)) switch
        {
            Resolution.R196 => 196,
            Resolution.R280 => 280,
            Resolution.R392 => 392,
            Resolution.R504 => 504,
            Resolution.R518 => 518,
            _ => 0
        };
        var outputFormat = (DepthOutputFormat)OutputFormat.GetValue(context);
        var normalization = (DepthNormalization)Normalization.GetValue(context);
        var skyHandling = (SkyHandling)SkyHandlingParam.GetValue(context);
        var enhanceContrast = EnhanceContrast.GetValue(context);
        var invertDepth = InvertDepth.GetValue(context);
        var matchRes = MatchInputAspect.GetValue(context);
        _matchInputAspect = matchRes;
        _workerOutputFormat = outputFormat;
        _workerEnhanceContrast = enhanceContrast;
        _workerInvertDepth = invertDepth;
        _workerPercentileNormalization = normalization == DepthNormalization.Percentile;
        _workerStabilizeSky = skyHandling == SkyHandling.Stabilize;
        _workerSmoothing = Math.Clamp(Smoothing.GetValue(context), 0f, MaxSmoothing);
        _workerFieldOfViewDegrees = Math.Clamp(FieldOfView.GetValue(context), 1f, 179f);

        // Ego-motion tracking costs tens of ms per frame on the worker; only pay it while something pulls the camera
        // Ego-motion tracking costs tens of ms per frame, so the depth tracker only runs while something pulls the camera. The
        // image-based rotation tracker must follow the video continuously: a scan that reads the pose only while it captures would
        // otherwise find it reset at every stop
        _workerTrackPose = CamReference.DirtyFlag.FramesSinceLastUpdate < PoseConsumerTimeoutFrames
                           || (DepthCameraMotion)CameraMotion.GetValue(context) == DepthCameraMotion.RotationOnly;
        _workerCameraMotion = (DepthCameraMotion)CameraMotion.GetValue(context);

        // Normalization and sky handling are applied by the worker when producing a result.
        // With a static input nothing else would re-run inference, so latch a recapture when
        // either changes - a live video input re-runs inference anyway.
        if (normalization != _lastNormalization || skyHandling != _lastSkyHandling)
        {
            _hasPendingInput = true;
        }

        _lastNormalization = normalization;
        _lastSkyHandling = skyHandling;

        // Store values for UI dropdowns
        _uiOutputFormat = (int)outputFormat;
        _uiNormalization = (int)normalization;
        _uiSkyHandling = (int)skyHandling;
        _uiModelSize = (int)modelSize;
        _uiInputResolution = (int)InputResolution.GetValue(context);
        _uiExecutionProvider = (int)provider;

        if (!enabled || inputTexture == null || inputTexture.IsDisposed)
        {
            OutputTexture.Value = inputTexture;
            Status.Value = "Disabled";
            PauseProcessing();
            ClearOutputs();
            return;
        }

        OutputTexture.Value = inputTexture;

        if (inputTexture != null && !inputTexture.IsDisposed)
        {
            var inputDescription = inputTexture.Description;
            if (inputDescription.Height > 0)
                _requestedInputAspectKey = AspectKey((float)inputDescription.Width / inputDescription.Height);
        }

        var inputSizeChanged = _requestedInputSize != _activeInputSize || _requestedInputAspectKey != _activeInputAspectKey;
        if (_initFailed && (modelSize != _activeModelSize || provider != _activeProvider || inputSizeChanged))
        {
            _initFailed = false;
        }

        if (!_initFailed && (_processingTask == null || _processingTask.IsCompleted || modelSize != _activeModelSize || provider != _activeProvider
                             || inputSizeChanged))
        {
            InitializeWorker(debug, modelSize, provider);
        }
        else if (_initFailed
                 && _initFailureMessage is { } failureMessage
                 && failureMessage.StartsWith("Model not found", StringComparison.Ordinal)
                 && modelSize == _activeModelSize && provider == _activeProvider
                 && Stopwatch.GetTimestamp() - _lastInitAttemptTicks > ModelNotFoundRetryInterval.Ticks)
        {
            // The missing model may have just been downloaded into the store - retry without
            // waiting for a settings change so a live graph recovers on its own
            InitializeWorker(debug, modelSize, provider);
        }

        var modelInfo = _modelInfo;
        if (modelInfo == null)
        {
            if (!_isInitializing)
            {
                Status.Value = _initFailureMessage ?? "Failed to initialize ONNX Runtime";
                ClearOutputs();
            }

            return;
        }

        // Only infer when the input texture actually changed; changes that arrive
        // while the worker is busy are latched and picked up when it frees
        if (inputDirty || !ReferenceEquals(inputTexture, _lastInputTexture))
        {
            _hasPendingInput = true;
        }

        if (_hasPendingInput && !_requestInFlight && !_preprocessor.IsPending && _inputQueue.IsEmpty)
        {
            BeginCapture(inputTexture, modelInfo, modelSize);
        }

        if (_preprocessor.IsPending)
        {
            CompleteCapture(inputTexture);
        }

        var generation = Volatile.Read(ref _workerGeneration);
        while (_outputQueue.TryDequeue(out var result))
        {
            if (result == null)
                continue;

            // Results from a replaced worker may have a different size and belong to a disposed model
            if (result.Generation != generation)
            {
                ReturnResultBuffers(result);
                continue;
            }

            if (result.Error != null)
            {
                Status.Value = "Error: " + result.Error;
                ReturnResultBuffers(result);
            }
            else if (result.DepthData != null)
            {
                ReturnResultBuffers(_currentResult);
                _currentResult = result;
                _updateCount++;
                InferenceMs.Value = result.InferenceMs;
                LatencyMs.Value = (float)((Stopwatch.GetTimestamp() - result.CaptureTicks) * 1000.0 / Stopwatch.Frequency);
                VerticalFov.Value = result.VerticalFovDegrees;
                var poseNote = string.IsNullOrEmpty(result.PoseSource) ? "" : $" - {result.PoseSource}";
                Status.Value = $"Processing {_updateCount}{poseNote} - infer {result.InferenceMs:0.0} ms, post {result.PostMs:0.0} ms (pose {result.PoseMs:0.0})";
            }
            else
            {
                ReturnResultBuffers(result);
            }
        }

        if (_currentResult != null && _currentResult.DepthData != null)
        {
            // Regenerating the textures is a full-res upload -
            // skip it while neither the data nor the formatting inputs changed
            if (_currentResult != _appliedResult || outputFormat != _appliedFormat || enhanceContrast != _appliedEnhance || invertDepth != _appliedInvert || matchRes != _appliedMatchRes)
            {
                if (_currentResult == _appliedResult && debug)
                {
                    Log.Debug($"[DepthAnything] Reapplying outputs - format={outputFormat}, enhance={enhanceContrast}, invert={invertDepth}", this);
                }

                ApplyResult(_currentResult, outputFormat, enhanceContrast, invertDepth);
                _appliedResult = _currentResult;
                _appliedFormat = outputFormat;
                _appliedEnhance = enhanceContrast;
                _appliedInvert = invertDepth;
                _appliedMatchRes = matchRes;
            }

            UpdateCount.Value = _updateCount;
            UpdateCameraReference(context, _currentResult, debug);
        }
        else
        {
            ClearOutputs();
        }
    }

    private void ClearOutputs()
    {
        ReturnResultBuffers(_currentResult);
        _currentResult = null;
        _appliedResult = null;
        DepthTexture.Value = null;
        NormalizedDepthTexture.Value = null;
        ConfidenceTexture.Value = null;
        SkyTexture.Value = null;
        MinDepth.Value = 0.0f;
        MaxDepth.Value = 0.0f;
        VerticalFov.Value = 0.0f;
        ResetCamera();
        if (string.IsNullOrEmpty(Status.Value))
            Status.Value = "No data";
    }

    /// <summary>
    /// Publishes the camera estimated by the model. The model outputs world-to-camera
    /// extrinsics (3x4 or 4x4); the camera position is -R^T t, its forward axis is R's
    /// third row and its image-up axis is -R's second row (DA3 uses an OpenCV-style
    /// y-down camera). Without an extrinsics output we fall back to the reference pose
    /// at the origin looking along +Z, which is what a single image predicts anyway.
    /// </summary>
    private void UpdateCameraReference(EvaluationContext context, DepthEstimationResult? result, bool debug)
    {
        var hasPose = result is { HasCameraPose: true };
        var position = hasPose ? result!.CameraPosition : Vector3.Zero;
        var forward = hasPose ? result!.CameraForward : Vector3.UnitZ;
        var up = hasPose ? result!.CameraUp : Vector3.UnitY;

        var fov = result?.VerticalFovDegrees ?? 0;
        if (fov <= 0)
            fov = _workerFieldOfViewDegrees;
        if (fov <= 0)
            fov = 50;

        var aspect = result is { Height: > 0 } ? (float)result.Width / result.Height : 1f;

        LastObjectToWorld = context.ObjectToWorld;
        PoseTrusted = result?.PoseTrusted ?? true;
        SetCameraPose(position, forward, up, fov, aspect);

        // One line per new result, so the pose trajectory shows in the log while debugging
        if (debug && result != null && !ReferenceEquals(result, _loggedPoseResult))
        {
            _loggedPoseResult = result;
            Log.Debug(hasPose
                          ? $"[DepthAnything] CamReference pose pos={position} target={position + forward} up={up} fov={fov:F1} source={result.PoseSource}"
                          : "[DepthAnything] CamReference: model has no extrinsics output; using the reference pose", this);
        }
    }

    private void ResetCamera()
    {
        SetCameraPose(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, _workerFieldOfViewDegrees > 0 ? _workerFieldOfViewDegrees : 50, 1f);
    }

    private void SetCameraPose(Vector3 position, Vector3 forward, Vector3 up, float fovDegrees, float aspect)
    {
        if (forward.LengthSquared() < 1e-8f)
            forward = Vector3.UnitZ;
        forward = Vector3.Normalize(forward);

        up -= forward * Vector3.Dot(up, forward);
        if (up.LengthSquared() < 1e-8f)
            up = MathF.Abs(forward.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        up = Vector3.Normalize(up);

        _cameraDefinition = new CameraDefinition
                                {
                                    NearFarClip = new Vector2(0.01f, 1000f),
                                    LensShift = Vector2.Zero,
                                    PositionOffset = Vector3.Zero,
                                    Position = position,
                                    Target = position + forward,
                                    Up = up,
                                    AspectRatio = MathF.Max(aspect, 0.0001f),
                                    FieldOfView = Math.Clamp(fovDegrees, 1f, 179f) * MathF.PI / 180f,
                                    Roll = 0,
                                    RotationOffset = Vector3.Zero,
                                    OffsetAffectsTarget = false
                                };
        _cameraDefinition.BuildProjectionMatrices(out var camToClipSpace, out var worldToCamera);

        CameraPosition = position;
        CameraTarget = position + forward;
        CameraRoll = 0;
        CameraToClipSpace = camToClipSpace;
        WorldToCamera = worldToCamera;
        CamReference.Value = this;
    }

    private static CameraPose ReadCameraPose(WorkerContext workerContext, IReadOnlyList<OrtValue> outputs)
    {
        if (workerContext.ExtrinsicsIndex < 0 || workerContext.ExtrinsicsIndex >= outputs.Count)
            return default;

        var value = outputs[workerContext.ExtrinsicsIndex];
        var info = value.GetTensorTypeAndShape();
        if (info.ElementDataType != TensorElementType.Float || info.Shape.Length < 2)
            return default;

        var rows = (int)info.Shape[^2];
        var columns = (int)info.Shape[^1];
        if (rows < 3 || columns < 4)
            return default;

        var data = value.GetTensorDataAsSpan<float>();
        if (data.Length < rows * columns)
            return default;

        // The first view's 3x4 block is the top of the tensor; a 4x4 export has the
        // same top three rows, so both layouts read the same values.
        float r11 = data[0], r12 = data[1], r13 = data[2], tx = data[3];
        float r21 = data[columns], r22 = data[columns + 1], r23 = data[columns + 2], ty = data[columns + 3];
        float r31 = data[2 * columns], r32 = data[2 * columns + 1], r33 = data[2 * columns + 2], tz = data[2 * columns + 3];

        // DA3 extrinsics are camera-to-world: position is the camera center, forward and up
        // are the OpenCV camera axes (z forward, y down) as world-space columns
        var position = new Vector3(tx, ty, tz);
        var forward = new Vector3(r13, r23, r33);
        var up = new Vector3(-r12, -r22, -r32);

        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
            || !float.IsFinite(forward.X) || !float.IsFinite(forward.Y) || !float.IsFinite(forward.Z)
            || !float.IsFinite(up.X) || !float.IsFinite(up.Y) || !float.IsFinite(up.Z))
            return default;

        return new CameraPose(true, position, forward, up);
    }

    private readonly record struct CameraPose(bool HasPose, Vector3 Position, Vector3 Forward, Vector3 Up);

    private const float KeyframeMinFrameDifference = 0.045f;
    private const float PoseJumpDegrees = 7f;
    private const int PoseJumpPatience = 3;
    private const float KeyframeRotationDegrees = 8f;
    private const float KeyframeTranslationOverDepth = 0.15f;

    /** Converts the model's OpenCV camera frame (y down, +Z forward) to TiXL's world (y up, -Z forward). */
    private static readonly Quaternion OpenCvToWorld = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);

    /**
     * The last views-1 keyframes plus the current frame as one [1,N,3,H,W] window. The ring starts as copies of the first
     * frame, so the first windows are identical views (identity poses) and the real ones follow as the camera moves. A frame
     * that barely differs from the newest keyframe does not enter the ring (shift 0).
     */
    private static OrtValue CreateChunkInput(WorkerContext workerContext, DepthEstimationRequest request, int views)
    {
        var viewLength = 3 * request.Height * request.Width;
        var current = request.InputData!;
        if (workerContext.WindowInput == null || workerContext.WindowInput.Length != views * viewLength || workerContext.RingViewLength != viewLength)
        {
            workerContext.WindowInput = new float[views * viewLength];
            workerContext.Ring = new float[views - 1][];
            for (var i = 0; i < views - 1; i++)
            {
                workerContext.Ring[i] = new float[viewLength];
                Array.Copy(current, workerContext.Ring[i], viewLength);
            }

            workerContext.RingViewLength = viewLength;
            workerContext.Chain = null;
        }

        var ring = workerContext.Ring!;
        workerContext.ChunkShift = MeanAbsoluteDifference(ring[views - 2], current, viewLength) >= KeyframeMinFrameDifference ? 1 : 0;
        for (var i = 0; i < views - 1; i++)
            Array.Copy(ring[i], 0, workerContext.WindowInput, i * viewLength, viewLength);

        Array.Copy(current, 0, workerContext.WindowInput, (views - 1) * viewLength, viewLength);
        return OrtValue.CreateTensorValueFromMemory(workerContext.WindowInput, [1, views, 3, request.Height, request.Width]);
    }

    /** Places the window in the world frame and publishes the newest frame's pose and depth scale. */
    private static void AccumulateChunkPose(WorkerContext workerContext, IReadOnlyList<OrtValue> outputs, DepthEstimationRequest request, int views)
    {
        if (workerContext.ExtrinsicsIndex < 0 || workerContext.IntrinsicsIndex < 0 || workerContext.Ring == null || request.Width != request.Height)
            return;

        var size = request.Height;
        var plane = size * size;
        var depth = outputs[workerContext.DepthIndex].GetTensorDataAsSpan<float>();
        var extrinsics = outputs[workerContext.ExtrinsicsIndex].GetTensorDataAsSpan<float>();
        var intrinsics = outputs[workerContext.IntrinsicsIndex].GetTensorDataAsSpan<float>();
        if (depth.Length < views * plane || extrinsics.Length < views * 12 || intrinsics.Length < views * 9)
            return;

        if (workerContext.ChunkDepth == null || workerContext.ChunkDepth.Length != views * plane)
        {
            workerContext.ChunkDepth = new float[views * plane];
            workerContext.ChunkConfidence = new float[views * plane];
            workerContext.ChunkExtrinsics = new float[views * 12];
            workerContext.ChunkIntrinsics = new float[views * 9];
        }

        depth.Slice(0, views * plane).CopyTo(workerContext.ChunkDepth);
        extrinsics.Slice(0, views * 12).CopyTo(workerContext.ChunkExtrinsics!);
        intrinsics.Slice(0, views * 9).CopyTo(workerContext.ChunkIntrinsics!);
        if (workerContext.ConfidenceIndex >= 0 && outputs[workerContext.ConfidenceIndex].GetTensorDataAsSpan<float>() is { Length: > 0 } confidence && confidence.Length >= views * plane)
            confidence.Slice(0, views * plane).CopyTo(workerContext.ChunkConfidence!);
        else
            Array.Fill(workerContext.ChunkConfidence!, 1f);

        var chunk = new DepthChunk(views, size, workerContext.ChunkDepth, workerContext.ChunkConfidence!, workerContext.ChunkExtrinsics!, workerContext.ChunkIntrinsics!);
        workerContext.Chain ??= new DepthChunkChain();
        // The ring is the key window's last views-1 frames, so a window is always placed one frame past the key window
        var placed = workerContext.Chain.Add(chunk, 1, workerContext.ChunkShift == 1);
        if (placed == null || placed.Count == 0)
        {
            // The shared frames could not be fitted: start a fresh world instead of publishing a guess
            workerContext.Chain = null;
            return;
        }

        var frame = placed[^1];
        var m = frame.Rotation;
        var world = workerContext.WorldRotation;
        world[0] = m.M11; world[1] = m.M21; world[2] = m.M31;
        world[3] = m.M12; world[4] = m.M22; world[5] = m.M32;
        world[6] = m.M13; world[7] = m.M23; world[8] = m.M33;
        workerContext.WorldPosition = frame.Position;
        workerContext.WorldScale = frame.DepthScale;

        if (workerContext.ChunkShift == 1)
        {
            var ring = workerContext.Ring;
            var recycled = ring[0];
            for (var i = 0; i < ring.Length - 1; i++)
                ring[i] = ring[i + 1];

            Array.Copy(request.InputData!, recycled, workerContext.RingViewLength);
            ring[^1] = recycled;
        }
    }

    /** The frame the model saw as 8-bit grayscale (null when its size differs from the output grid), for image-based rotation tracking. */
    private static byte[]? TryGetGray(WorkerContext workerContext, DepthEstimationRequest request, int width, int height)
    {
        var pad = request.PadRows;
        if (request.Width != width || request.Height - 2 * pad != height)
            return null;

        var count = width * height;
        if (workerContext.GrayFrame == null || workerContext.GrayFrame.Length != count)
            workerContext.GrayFrame = new byte[count];

        var gray = workerContext.GrayFrame;
        if (request.InputBytes is { } bytes && bytes.Length >= count * 3)
        {
            for (var i = 0; i < count; i++)
                gray[i] = (byte)(0.299f * bytes[i * 3] + 0.587f * bytes[i * 3 + 1] + 0.114f * bytes[i * 3 + 2]);

            return gray;
        }

        if (request.InputData is { } data && data.Length >= request.Width * request.Height * 3)
        {
            // Planar ImageNet-normalized RGB: undo the normalization
            var plane = request.Width * request.Height;
            var first = pad * request.Width;
            for (var i = 0; i < count; i++)
            {
                var r = data[first + i] * 0.229f + 0.485f;
                var g = data[plane + first + i] * 0.224f + 0.456f;
                var b = data[2 * plane + first + i] * 0.225f + 0.406f;
                gray[i] = (byte)Math.Clamp((int)((0.299f * r + 0.587f * g + 0.114f * b) * 255f + 0.5f), 0, 255);
            }

            return gray;
        }

        return null;
    }

    /** Keyframe and current frame as one [1,2,3,H,W] tensor; the first frame pairs with itself, which anchors the world pose at zero motion. */
    private static OrtValue CreatePairInput(WorkerContext workerContext, DepthEstimationRequest request)
    {
        var viewLength = 3 * request.Height * request.Width;
        if (workerContext.PairInput == null || workerContext.PairInput.Length != 2 * viewLength)
            workerContext.PairInput = new float[2 * viewLength];

        if (workerContext.PreviousInput == null || workerContext.PreviousInputLength != viewLength)
        {
            workerContext.PreviousInput = new float[viewLength];
            workerContext.PreviousInputLength = viewLength;
            workerContext.HasPreviousView = false;
        }

        var current = request.InputData!;
        var previous = workerContext.HasPreviousView ? workerContext.PreviousInput : current;
        Array.Copy(previous, 0, workerContext.PairInput, 0, viewLength);
        Array.Copy(current, 0, workerContext.PairInput, viewLength, viewLength);
        return OrtValue.CreateTensorValueFromMemory(workerContext.PairInput, [1, 2, 3, request.Height, request.Width]);
    }

    /**
     * The pair's last extrinsics map the keyframe's camera frame to the current one, in the pair's own
     * depth units. Pairing against a keyframe instead of the previous frame stops per-pair noise from
     * integrating while the camera is still. Each pair has its own arbitrary scale, so the keyframe's depth
     * in this pair is compared with its world-unit depth to carry one world scale across the sequence.
     * Returns true when the current frame should become the new keyframe.
     */
    private static bool AccumulateViewPose(WorkerContext workerContext, IReadOnlyList<OrtValue> outputs, int count, float[] currentInput)
    {
        // Near-identical frames have no baseline, so the model's relative pose is noise: hold the keyframe pose
        if (workerContext.HasPreviousView && workerContext.PreviousInput != null)
        {
            var frameDifference = MeanAbsoluteDifference(workerContext.PreviousInput, currentInput, workerContext.PreviousInputLength);
            if (frameDifference < KeyframeMinFrameDifference)
            {
                Array.Copy(workerContext.KeyRotation, workerContext.WorldRotation, 9);
                workerContext.WorldPosition = workerContext.KeyPosition;
                Array.Copy(workerContext.WorldRotation, workerContext.LastRotation, 9);
                workerContext.LastPosition = workerContext.WorldPosition;
                workerContext.JumpCount = 0;
                return false;
            }
        }

        if (workerContext.ExtrinsicsIndex < 0)
            return false;

        var extrinsicsValue = outputs[workerContext.ExtrinsicsIndex];
        var info = extrinsicsValue.GetTensorTypeAndShape();
        if (info.ElementDataType != TensorElementType.Float || info.Shape.Length < 2)
            return false;

        var rows = (int)info.Shape[^2];
        var columns = (int)info.Shape[^1];
        var extrinsics = extrinsicsValue.GetTensorDataAsSpan<float>();
        var depth = outputs[workerContext.DepthIndex].GetTensorDataAsSpan<float>();
        if (rows < 3 || columns < 4 || extrinsics.Length < 2 * rows * columns || depth.Length < 2 * count)
            return false;

        var currentDepth = MedianDepth(workerContext, depth.Slice(depth.Length - count, count));
        var previousDepth = MedianDepth(workerContext, depth.Slice(depth.Length - 2 * count, count));
        if (!(currentDepth > 0f) || !(previousDepth > 0f))
            return false;

        var scale = 1f;
        if (workerContext.HasPreviousView && workerContext.KeyWorldDepth > 0f)
            scale = Math.Clamp(workerContext.KeyWorldDepth / previousDepth, 0.25f, 4f);

        // Row-major camera-to-world [R|t] of the current view in the previous view's frame (DA3 predicts c2w):
        // the camera center is t and its orientation chains as world * R
        var offset = extrinsics.Length - rows * columns;
        Span<float> r = stackalloc float[9];
        Span<float> t = stackalloc float[3];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
                r[row * 3 + column] = extrinsics[offset + row * columns + column];
            t[row] = extrinsics[offset + row * columns + 3];
        }

        // Measured against a PTZ camera, chaining the pair's matrix as read turns the pose opposite to the real
        // camera on both axes (pan left turns right, tilt up looks down), so chain its inverse
        var tx = -(r[0] * t[0] + r[3] * t[1] + r[6] * t[2]);
        var ty = -(r[1] * t[0] + r[4] * t[1] + r[7] * t[2]);
        var tz = -(r[2] * t[0] + r[5] * t[1] + r[8] * t[2]);
        (r[1], r[3]) = (r[3], r[1]);
        (r[2], r[6]) = (r[6], r[2]);
        (r[5], r[7]) = (r[7], r[5]);
        t[0] = tx;
        t[1] = ty;
        t[2] = tz;

        var motion = new Vector3(t[0], t[1], t[2]) * scale;
        if (!float.IsFinite(motion.X) || !float.IsFinite(motion.Y) || !float.IsFinite(motion.Z))
            return false;

        var key = workerContext.KeyRotation;
        workerContext.WorldPosition = workerContext.KeyPosition
                                      + new Vector3(key[0] * motion.X + key[1] * motion.Y + key[2] * motion.Z,
                                                    key[3] * motion.X + key[4] * motion.Y + key[5] * motion.Z,
                                                    key[6] * motion.X + key[7] * motion.Y + key[8] * motion.Z);

        // world = keyframe * R
        var world = workerContext.WorldRotation;
        Span<float> nextWorld = stackalloc float[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
                nextWorld[row * 3 + column] = key[row * 3] * r[column] + key[row * 3 + 1] * r[3 + column] + key[row * 3 + 2] * r[6 + column];
        }

        // One noisy pair must not throw the pose across the room: a jump is held back unless it persists
        var last = workerContext.LastRotation;
        var jumpTrace = 0f;
        for (int i = 0; i < 9; i++)
            jumpTrace += last[i] * nextWorld[i];

        var jumpDegrees = MathF.Acos(Math.Clamp((jumpTrace - 1f) * 0.5f, -1f, 1f)) * 180f / MathF.PI;
        if (jumpDegrees > PoseJumpDegrees && workerContext.HasPreviousView && workerContext.JumpCount < PoseJumpPatience)
        {
            workerContext.JumpCount++;
            Array.Copy(last, world, 9);
            workerContext.WorldPosition = workerContext.LastPosition;
            return false;
        }

        workerContext.JumpCount = 0;
        nextWorld.CopyTo(world);
        Array.Copy(world, last, 9);
        workerContext.LastPosition = workerContext.WorldPosition;

        workerContext.WorldScale = scale;
        var worldDepth = currentDepth * scale;
        var trace = r[0] + r[4] + r[8];
        var rotationDegrees = MathF.Acos(Math.Clamp((trace - 1f) * 0.5f, -1f, 1f)) * 180f / MathF.PI;
        var promote = !workerContext.HasPreviousView
                      || rotationDegrees > KeyframeRotationDegrees
                      || new Vector3(t[0], t[1], t[2]).Length() > KeyframeTranslationOverDepth * currentDepth;
        if (promote)
        {
            Array.Copy(world, key, 9);
            workerContext.KeyPosition = workerContext.WorldPosition;
            workerContext.KeyWorldDepth = worldDepth;
        }

        return promote;
    }

    private static float MeanAbsoluteDifference(float[] a, float[] b, int length)
    {
        const int step = 7;
        var sum = 0f;
        var n = 0;
        for (int i = 0; i < length; i += step, n++)
            sum += MathF.Abs(a[i] - b[i]);

        return n == 0 ? 0f : sum / n;
    }

    /** Median of a strided sample of the finite positive depths; 0 when there are none. */
    private static float MedianDepth(WorkerContext workerContext, ReadOnlySpan<float> plane)
    {
        var scratch = workerContext.MedianScratch;
        var step = Math.Max(1, plane.Length / scratch.Length);
        var n = 0;
        for (int i = 0; i < plane.Length && n < scratch.Length; i += step)
        {
            var value = plane[i];
            if (float.IsFinite(value) && value > 0f)
                scratch[n++] = value;
        }

        if (n == 0)
            return 0f;

        Array.Sort(scratch, 0, n);
        return scratch[n / 2];
    }

    private static float DegreesBetween(Vector3 a, Vector3 b)
    {
        var length = a.Length() * b.Length();
        if (length < 1e-8f)
            return 0f;
        var dot = Vector3.Dot(a, b) / length;
        return (float)(Math.Acos(Math.Clamp(dot, -1f, 1f)) * 180.0 / Math.PI);
    }

    private void ReturnResultBuffers(DepthEstimationResult? result)
    {
        if (result == null)
            return;

        ReturnDepthBuffer(result.DepthData);
        ReturnDepthBuffer(result.ConfidenceData);
        ReturnDepthBuffer(result.SkyData);
        ReturnBufferToPool(result.ColorData);
        result.DepthData = null;
        result.ConfidenceData = null;
        result.SkyData = null;
        result.ColorData = null;
    }

    #region Capture

    private void BeginCapture(Texture2D inputTexture, ModelInfo modelInfo, ModelSize modelSize)
    {
        var description = inputTexture.Description;
        GetInputDimensions(modelInfo, description.Width, description.Height, modelSize, _requestedInputSize, out var width, out var height);

        if (modelInfo.InputIsBytes)
        {
            if (_inputBytes == null || _inputBytes.Length != 3 * width * height)
            {
                _inputBytes = new byte[3 * width * height];
            }
        }
        else if (_inputData == null || _inputData.Length != 3 * width * height)
        {
            _inputData = new float[3 * width * height];
        }

        var layout = modelInfo.InputIsBytes ? DepthInputPreprocessor.InputLayout.PackedBytes : DepthInputPreprocessor.InputLayout.PlanarFloat;
        var padRows = GetLetterboxRows(modelInfo, width, height, description.Width, description.Height);
        if (!_preprocessor.TryBegin(inputTexture, width, height, layout, padRows))
        {
            if (!_loggedCaptureFailure)
            {
                _loggedCaptureFailure = true;
                Log.Warning($"[DepthAnything] Can't prepare the input texture ({description.Format}): {_preprocessor.Error}", this);
            }

            _hasPendingInput = false;
            return;
        }

        _loggedCaptureFailure = false;
        _captureWidth = width;
        _captureHeight = height;
        _captureSourceWidth = description.Width;
        _captureSourceHeight = description.Height;
        _capturePadRows = padRows;
        _captureTicks = Stopwatch.GetTimestamp();
        _captureIsBytes = modelInfo.InputIsBytes;
    }

    private void CompleteCapture(Texture2D inputTexture)
    {
        var inputData = _captureIsBytes ? null : _inputData;
        var inputBytes = _captureIsBytes ? _inputBytes : null;
        if (inputData == null && inputBytes == null)
        {
            _preprocessor.Cancel();
            return;
        }

        var state = inputBytes != null ? _preprocessor.TryComplete(inputBytes) : _preprocessor.TryComplete(inputData!);
        switch (state)
        {
            case DepthInputPreprocessor.ReadbackState.Ready:
                _requestInFlight = true;
                _hasPendingInput = false;
                _lastInputTexture = inputTexture;
                _inputQueue.Enqueue(new DepthEstimationRequest
                                        {
                                            InputData = inputData,
                                            InputBytes = inputBytes,
                                            Width = _captureWidth,
                                            Height = _captureHeight,
                                            SourceWidth = _captureSourceWidth,
                                            SourceHeight = _captureSourceHeight,
                                            PadRows = _capturePadRows,
                                            CaptureTicks = _captureTicks,
                                            Generation = Volatile.Read(ref _workerGeneration)
                                        });
                _workerWake.Set();
                break;

            case DepthInputPreprocessor.ReadbackState.TimedOut:
                Log.Warning("[DepthAnything] Readback never completed - restarting readback", this);
                _hasPendingInput = true;
                break;
        }
    }

    /**
     * The metric model has a fixed square input and reads depth from the apparent size of things, so squeezing a 16:9 frame into it
     * skews distances differently along each axis. Its input is letterboxed instead: this is the black rows above and below the image.
     */
    private static int GetLetterboxRows(ModelInfo modelInfo, int width, int height, int sourceWidth, int sourceHeight)
    {
        if (!modelInfo.IsMetric || modelInfo.InputIsBytes || modelInfo.ViewCount != 1 || width != height || sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth <= sourceHeight)
            return 0;

        var contentHeight = (int)MathF.Round(width * (float)sourceHeight / sourceWidth / 2f) * 2;
        return Math.Clamp((height - contentHeight) / 2, 0, height / 4);
    }

    /** Drops the letterbox rows from a [height x width] plane in place and returns the image rows' height. */
    private static int CropLetterbox(float[]? plane, int width, int height, int padRows)
    {
        if (plane == null || padRows <= 0)
            return height;

        var rows = height - 2 * padRows;
        System.Buffer.BlockCopy(plane, padRows * width * sizeof(float), plane, 0, rows * width * sizeof(float));
        return rows;
    }

    /** Dynamic-size models take the image's aspect with its long side at the requested size; fixed-size exports dictate their own. */
    private static void GetInputDimensions(ModelInfo modelInfo, int sourceWidth, int sourceHeight, ModelSize modelSize, int requestedSize,
                                           out int width, out int height)
    {
        if (modelInfo.DeclaredWidth > 0 && modelInfo.DeclaredHeight > 0)
        {
            width = modelInfo.DeclaredWidth;
            height = modelInfo.DeclaredHeight;
            return;
        }

        var targetSize = GetEffectiveInputSize(requestedSize, modelSize);
        var aspectRatio = (float)sourceWidth / sourceHeight;
        if (aspectRatio > 1.0f)
        {
            width = targetSize;
            height = Math.Max(PatchSize, (int)(targetSize / aspectRatio) / PatchSize * PatchSize);
        }
        else
        {
            height = targetSize;
            width = Math.Max(PatchSize, (int)(targetSize * aspectRatio) / PatchSize * PatchSize);
        }
    }

    private static int GetEffectiveInputSize(int requestedSize, ModelSize modelSize)
    {
        if (requestedSize > 0)
        {
            return requestedSize;
        }

        return modelSize switch
        {
            ModelSize.Da3Small or ModelSize.Da3Base or ModelSize.Da3Large or ModelSize.Da3MonoLarge
                or ModelSize.Da3MetricLarge or ModelSize.Da3Giant or ModelSize.Da3NestedGiant or ModelSize.Da3BaseTracked => 504,
            ModelSize.Da3BaseStream => 392,
            _ => DefaultInputSize
        };
    }

    #endregion

    #region ONNX Runtime Integration

    // The ViT backbones need a size divisible by the patch size
    private const int PatchSize = 14;
    private const int DefaultInputSize = 518;

    /** Reference camera focal length, in pixels, the metric model predicts depth for. */
    private const float MetricReferenceFocalPx = 300f;
    private const float MaxSmoothing = 0.95f;

    /** Depth samples per view used to relate the depth scale of consecutive view pairs. */
    private const int MedianSamples = 1024;

    // Published by the worker once its session is warm; null while loading or after a failure
    private volatile ModelInfo? _modelInfo;

    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<DepthEstimationRequest> _inputQueue = new();
    private readonly ConcurrentQueue<DepthEstimationResult?> _outputQueue = new();
    private DepthEstimationResult? _currentResult;
    private readonly object _workerLock = new object();
    private ModelSize _activeModelSize;
    private static readonly TimeSpan PreviousWorkerExitTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ModelLoadSettleTime = TimeSpan.FromMilliseconds(750);
    private int _activeInputSize;
    private int _requestedInputAspectKey;
    private int _activeInputAspectKey;
    private int _updateCount;

    private volatile bool _isInitializing;
    private volatile bool _initFailed;
    private volatile string? _initFailureMessage;
    private volatile bool _requestInFlight;
    private volatile string _activeProviderLabel = "CPU";

    // Render-thread only
    private bool _hasPendingInput;
    private Texture2D? _lastInputTexture;
    private ExecutionProvider _activeProvider;
    private bool _appliedMatchRes;
    private DepthNormalization _lastNormalization;
    private SkyHandling _lastSkyHandling;

    /** How often a "Model not found" failure re-checks the model store for an appeared file. */
    private static readonly TimeSpan ModelNotFoundRetryInterval = TimeSpan.FromSeconds(2);

    private long _lastInitAttemptTicks;

    // Volatile settings the worker reads when producing a result
    private volatile bool _matchInputAspect;
    private int _capturePadRows;
    private volatile DepthOutputFormat _workerOutputFormat;
    private volatile bool _workerEnhanceContrast;
    private volatile bool _workerInvertDepth;
    private volatile bool _workerPercentileNormalization;
    private volatile bool _workerStabilizeSky;
    private volatile float _workerSmoothing;
    private volatile float _workerFieldOfViewDegrees;
    private volatile bool _workerTrackPose;
    private volatile DepthCameraMotion _workerCameraMotion;
    private const int PoseConsumerTimeoutFrames = 30;

    // Requested inference size from the Resolution input, 0 = auto
    private volatile int _requestedInputSize;

    // Wakes the worker instead of polling; never disposed - an exiting worker may still wait on it
    private readonly AutoResetEvent _workerWake = new(false);
    private int _workerGeneration;
    private volatile string? _pendingStatus;

    // Render-thread side of the GPU input preparation. Only one capture is ever in flight, so the tensor
    // array is free to be rewritten whenever no request is.
    private readonly DepthInputPreprocessor _preprocessor = new();
    private float[]? _inputData;
    private byte[]? _inputBytes;
    private int _captureWidth;
    private int _captureHeight;
    private int _captureSourceWidth;
    private int _captureSourceHeight;
    private long _captureTicks;
    private bool _captureIsBytes;
    private bool _loggedCaptureFailure;

    // Owned by the render thread - outputs only regenerate when these change
    private DepthEstimationResult? _appliedResult;
    private DepthOutputFormat _appliedFormat;
    private bool _appliedEnhance;
    private bool _appliedInvert;
    private byte[]? _normalizedBuffer;

    private SizeClassBufferPool? _bufferPool;

    // Stored values for UI dropdowns (updated each frame, read by UI thread)
    private int _uiOutputFormat;
    private int _uiNormalization;
    private int _uiSkyHandling;
    private int _uiModelSize;
    private int _uiInputResolution;
    private int _uiExecutionProvider;

    /** The provider input as saved (before the Auto resolution), so the dropdown can show what Auto picked. */
    private int _uiRawExecutionProvider;

    private void InitializeWorker(bool debug, ModelSize modelSize, ExecutionProvider provider)
    {
        _lastInitAttemptTicks = Stopwatch.GetTimestamp();
        var previousTask = _processingTask;
        StopWorker(debug);
        _activeModelSize = modelSize;
        _activeProvider = provider;
        _activeInputSize = _requestedInputSize;
        _activeInputAspectKey = _requestedInputAspectKey;
        _initFailed = false;
        _initFailureMessage = null;
        _requestInFlight = false;
        _hasPendingInput = true;
        _loggedPoseResult = null;

        var modelFile = ResolveModelFile(modelSize, debug, out var modelPath);
        if (modelPath == null)
        {
            _initFailed = true;
            return;
        }

        _isInitializing = true;
        Status.Value = provider == ExecutionProvider.TensorRt
                           ? "Loading model... (TensorRT builds its engine on first use - this can take a few minutes; CUDA starts instantly)"
                           : "Loading model...";

        var generation = Interlocked.Increment(ref _workerGeneration);
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;
        _bufferPool = new SizeClassBufferPool();
        _processingTask = Task.Run(async () =>
        {
            // The previous worker releases its session only when its loop exits. Waiting here keeps the render
            // thread free while preventing two models from holding GPU memory at the same time - but only briefly:
            // a predecessor stuck in a TensorRT engine build would otherwise block a provider switch for minutes.
            if (previousTask != null)
            {
                try
                {
                    await Task.WhenAny(previousTask, Task.Delay(PreviousWorkerExitTimeout, token)).ConfigureAwait(false);
                }
                catch
                {
                    // A failing predecessor must not prevent this generation from starting
                }
            }

            if (token.IsCancellationRequested)
                return;

            // Stepping through the model dropdown re-initializes per entry; a TensorRT engine build cannot be
            // cancelled once started, so a choice must survive briefly before its session is created.
            try
            {
                await Task.Delay(ModelLoadSettleTime, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            WorkerLoop(modelPath, modelFile, modelSize, provider, token, generation, debug);
        }, CancellationToken.None);
    }

    private void PublishStatus(string status)
    {
        _pendingStatus = status;
    }

    /**
     * Candidate file names per model, in order of preference. Every model is DA3 now; the V2-era
     * enum values are normalized onto their DA3 equivalents before this runs (see NormalizeModelSize).
     */
    private static string[] GetModelCandidates(ModelSize modelSize)
    {
        return modelSize switch
        {
            ModelSize.Da3Small => ["da3-small-fp16.onnx", "da3-small.onnx", "da3-small-int8.onnx"],
            ModelSize.Da3Base => ["da3-base-fp16.onnx", "da3-base.onnx"],
            ModelSize.Da3Large => ["da3-large-fp16.onnx", "da3-large.onnx"],
            ModelSize.Da3MonoLarge => ["da3-mono-large-fp32.onnx", "da3-mono-large-fp16.onnx", "da3-mono-large-w8a16.onnx", "da3-mono-large-int8.onnx"],
            ModelSize.Da3MetricLarge => ["da3-metric-large-fp16.onnx"],
            ModelSize.Da3Giant => ["da3-giant-fp16.onnx", "da3-giant.onnx"],
            ModelSize.Da3NestedGiant => ["da3-nested-giant-fp16.onnx", "da3-nested-giant.onnx"],
            ModelSize.Da3BaseTracked => ["da3-base-mv2.onnx"],
            ModelSize.Da3BaseStream => ["da3-base-mv6-392.onnx"],
            _ => ["da3-small-fp16.onnx", "da3-small.onnx", "da3-small-int8.onnx"]
        };
    }

    /** Legacy V2-era ModelSize values route onto their DA3 equivalents; only DA3 models remain. */
    private static ModelSize NormalizeModelSize(ModelSize modelSize) => modelSize switch
    {
        ModelSize.Small or ModelSize.SmallFp32 or ModelSize.SmallInt8 => ModelSize.Da3Small,
        ModelSize.Base => ModelSize.Da3Base,
        ModelSize.Large => ModelSize.Da3MonoLarge,
        _ => modelSize
    };

    /**
     * Prepends the fixed-size exports for a requested input size ("da3-small-280-fp16.onnx" before "da3-small-fp16.onnx"):
     * fixed-size exports ignore the requested size, so a smaller input needs its own file.
     */
    private static string[] WithSizeVariants(string[] candidates, int inputSize, int aspectKey)
    {
        var aspect = aspectKey / AspectKeyScale;
        var matchAspect = aspect > 0 && MathF.Abs(aspect - 1f) > 0.02f;
        if (inputSize <= 0 && !matchAspect)
            return candidates;

        // An export at the source's aspect ratio sees the frame undistorted: "da3-small-392x224-fp16.onnx" for 16:9
        var longSide = inputSize > 0 ? inputSize : DefaultInputSize;
        var result = new List<string>(candidates.Length * 3);
        if (matchAspect)
        {
            var shortSide = Math.Max(PatchSize, (int)MathF.Round(longSide / MathF.Max(aspect, 1f / aspect) / PatchSize) * PatchSize);
            var size = aspect > 1f ? $"{longSide}x{shortSide}" : $"{shortSide}x{longSide}";
            foreach (var candidate in candidates)
                result.Add(InsertSize(candidate, size));
        }

        if (inputSize > 0)
        {
            foreach (var candidate in candidates)
                result.Add(InsertSize(candidate, inputSize.ToString()));
        }

        result.AddRange(candidates);
        return result.ToArray();
    }

    /** "da3-small-fp16.onnx" + "392x224" -> "da3-small-392x224-fp16.onnx" */
    private static string InsertSize(string candidate, string size)
    {
        var stem = candidate[..^".onnx".Length];
        var precisionIndex = stem.LastIndexOf("-fp", StringComparison.Ordinal);
        if (precisionIndex < 0)
            precisionIndex = stem.LastIndexOf("-int8", StringComparison.Ordinal);

        return precisionIndex < 0
                   ? $"{stem}-{size}.onnx"
                   : $"{stem[..precisionIndex]}-{size}{stem[precisionIndex..]}.onnx";
    }

    /** Aspect ratios compare as integers so float jitter in texture sizes cannot trigger a reload. */
    private const float AspectKeyScale = 100f;

    private static int AspectKey(float aspect) => (int)MathF.Round(aspect * AspectKeyScale);

    private ModelFile ResolveModelFile(ModelSize modelSize, bool debug, out string? modelPath)
    {
        var plainCandidates = GetModelCandidates(modelSize);
        var candidates = WithSizeVariants(plainCandidates, _activeInputSize, _activeInputAspectKey);
        foreach (var candidate in candidates)
        {
            var path = FindModelPath(candidate, debug);
            if (path == null)
                continue;

            modelPath = path;
            return new ModelFile(candidate, candidate.Contains("metric", StringComparison.Ordinal));
        }

        // Name the file to install, not one of the optional size-matched exports tried first
        var status = $"Model not found: {plainCandidates[0]}";
        if (debug) Log.Error($"[DepthAnything] {status}. Place it in the model store (Settings > AI Models) or the package's Assets/ folder.", this);
        Status.Value = status;
        // Update would otherwise overwrite this with a generic init-failure message on the same frame
        _initFailureMessage = status;
        modelPath = null;
        return default;
    }

    private string? FindModelPath(string modelName, bool debug)
    {
        // The model store (AI settings) first: that is where the settings window downloads models
        if (T3.Core.Settings.AiModelSettings.TryGetStoredFile(T3.Core.Settings.AiModelSettings.DepthAnythingFamilyFolder,
                                                             modelName, out var storedPath))
        {
            return storedPath;
        }

        // Try AssetRegistry first
        try
        {
            if (AssetRegistry.TryResolveAddress($"DepthAnything:{modelName}", this, out var resolvedPath, out _, logWarnings: false))
            {
                return resolvedPath;
            }
        }
        catch { }

        var asmDir = System.IO.Path.GetDirectoryName(typeof(DepthAnything).Assembly.Location) ?? "";
        var possiblePaths = new[]
        {
            System.IO.Path.Combine(asmDir, "Assets", modelName),
            System.IO.Path.Combine(asmDir, "..", "Assets", modelName),
            System.IO.Path.Combine(asmDir, "..", "..", "Assets", modelName),
            System.IO.Path.Combine(asmDir, "..", "..", "..", "Assets", modelName),
            System.IO.Path.Combine(asmDir, modelName),
            $"Assets/{modelName}",
            $"./Assets/{modelName}",
            $"../Assets/{modelName}"
        };

        foreach (var path in possiblePaths)
        {
            if (System.IO.File.Exists(path))
            {
                var fullPath = System.IO.Path.GetFullPath(path);
                if (debug) Log.Debug($"[DepthAnything] Found model at: {fullPath}", this);
                return fullPath;
            }
        }

        return null;
    }

    private void WorkerLoop(string modelPath, ModelFile modelFile, ModelSize modelSize, ExecutionProvider provider, CancellationToken token, int generation, bool debug)
    {
        if (debug) Log.Debug("[DepthAnything] Worker loop started", this);

        var workerContext = new WorkerContext();
        var failedToInit = false;
        try
        {
            var modelInfo = InitializeSession(workerContext, modelFile, modelPath, provider, token, debug);

            // Session creation can't be cancelled (a TensorRT engine build runs for minutes); a successor may
            // already be running, so a cancelled worker only disposes its session and must not publish anything.
            if (token.IsCancellationRequested)
                return;

            if (modelInfo == null || workerContext.Session == null)
            {
                failedToInit = true;
            }
            else
            {
                WarmUpSession(workerContext, modelInfo, modelSize, token, debug);
                if (token.IsCancellationRequested)
                    return;

                // Published only after warm-up so request acceptance coincides with a usable session
                _modelInfo = modelInfo;

                var size = modelInfo.DeclaredWidth > 0 ? $"{modelInfo.DeclaredWidth}x{modelInfo.DeclaredHeight}" : "dynamic size";
                PublishStatus($"Ready ({_activeProviderLabel}, {modelFile.Name}, {size})");
                Log.Info($"[DepthAnything] Ready ({_activeProviderLabel}) - {modelFile.Name}, input '{workerContext.InputNames[0]}' " +
                         $"[{string.Join(", ", workerContext.Session.InputMetadata[workerContext.InputNames[0]].Dimensions)}], outputs {string.Join(", ", workerContext.OutputNames)}", this);
            }

            while (workerContext.Session != null && !token.IsCancellationRequested)
            {
                if (_inputQueue.TryDequeue(out var request))
                {
                    // A request captured for a replaced model carries the old input layout; it must not reach
                    // the new session (the render thread's in-flight flag belongs to the new generation).
                    if (request.Generation != generation)
                    {
                        if (debug)
                            Log.Debug($"[DepthAnything] Dropping stale request (generation {request.Generation}, worker {generation})", this);
                        continue;
                    }

                    if ((request.InputData != null || request.InputBytes != null) && workerContext.Session != null)
                    {
                        var result = ProcessFrame(workerContext, request, modelInfo ?? new ModelInfo(), debug);
                        if (result != null)
                            result.Generation = generation;

                        if (token.IsCancellationRequested)
                        {
                            // A cancelled generation must not feed results into its successor
                            ReturnResultBuffers(result);
                        }
                        else
                        {
                            _outputQueue.Enqueue(result);
                        }
                    }

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
                if (debug) Log.Error($"[DepthAnything] Worker loop error: {ex.Message}", this);
                _outputQueue.Enqueue(new DepthEstimationResult { Error = ex.Message, Generation = generation });
            }

            _requestInFlight = false;
        }
        finally
        {
            // Owns its session - dispose unconditionally; a newer worker may have replaced the model info
            workerContext.RunOptions?.Dispose();
            workerContext.Session?.Dispose();

            // Stale generations must not clobber their successor's init state
            if (generation == Volatile.Read(ref _workerGeneration))
            {
                _modelInfo = null;
                _isInitializing = false;
                if (failedToInit)
                    _initFailed = true;
            }

            if (debug) Log.Debug("[DepthAnything] Worker loop stopped", this);
        }
    }

    private ModelInfo? InitializeSession(WorkerContext workerContext, ModelFile modelFile, string modelPath, ExecutionProvider provider, CancellationToken token, bool debug)
    {
        InferenceSession session;
        try
        {
            if (token.IsCancellationRequested)
            {
                return null;
            }

            session = CreateSession(modelPath, provider);
        }
        catch (Exception ex)
        {
            _initFailureMessage = $"Initialization error: {ex.Message}";
            Log.Warning($"[DepthAnything] {_initFailureMessage}", this);
            return null;
        }

        workerContext.Session = session;
        workerContext.RunOptions = new RunOptions();

        var inputName = FirstMetadataKey(session.InputMetadata) ?? "pixel_values";
        workerContext.InputNames = [inputName];

        var inputMetadata = session.InputMetadata[inputName];
        var dimensions = inputMetadata.Dimensions;
        var isFloatPlanar = inputMetadata.ElementType == typeof(float) && dimensions.Length == 4 && dimensions[1] == 3;
        var isBytePacked = inputMetadata.ElementType == typeof(byte) && dimensions.Length == 4 && dimensions[3] == 3;
        var isFloatPair = inputMetadata.ElementType == typeof(float) && dimensions.Length == 5 && dimensions[1] >= 2 && dimensions[2] == 3;
        if (!isFloatPlanar && !isBytePacked && !isFloatPair)
        {
            _initFailureMessage = "Unsupported model input: expected float32 NCHW, float32 [1,N,3,H,W] views (N >= 2) or uint8 NHWC RGB";
            Log.Warning($"[DepthAnything] Unsupported input for {modelFile.Name}: {inputMetadata.ElementType.Name} [{string.Join(", ", dimensions)}]", this);

            // Leaving the session assigned would keep the worker loop alive forever while _modelInfo
            // stays null: the operator would report "Loading model..." and never fail or retry.
            session.Dispose();
            workerContext.Session = null;
            workerContext.RunOptions = null;
            return null;
        }

        var outputNames = new List<string>(4);
        workerContext.DepthIndex = AddOutput(outputNames, session.OutputMetadata, ["depth", "predicted_depth"], true);
        workerContext.ConfidenceIndex = AddOutput(outputNames, session.OutputMetadata, ["depth_conf", "conf", "confidence"], false);
        workerContext.SkyIndex = AddOutput(outputNames, session.OutputMetadata, ["sky"], false);
        workerContext.IntrinsicsIndex = AddOutput(outputNames, session.OutputMetadata, ["intrinsics"], false);
        workerContext.ExtrinsicsIndex = AddOutput(outputNames, session.OutputMetadata, ["extrinsics"], false);
        workerContext.OutputNames = outputNames.ToArray();

        // NCHW is [batch, 3, height, width]; NHWC is [batch, height, width, 3]; a view pair is [1, 2, 3, height, width]
        var heightIndex = isBytePacked ? 1 : isFloatPair ? 3 : 2;
        return new ModelInfo
                   {
                       DeclaredHeight = dimensions[heightIndex] > 0 ? dimensions[heightIndex] : 0,
                       DeclaredWidth = dimensions[heightIndex + 1] > 0 ? dimensions[heightIndex + 1] : 0,
                       IsMetric = modelFile.IsMetric,
                       InputIsBytes = isBytePacked,
                       ViewCount = isFloatPair ? (dimensions[1] > 1 ? (int)dimensions[1] : 2) : 1
                   };
    }

    /** Appends the first output called one of <paramref name="aliases"/> and returns its index; without a match, a required output falls back to the first one. */
    private static int AddOutput(List<string> outputNames, IReadOnlyDictionary<string, NodeMetadata> metadata, string[] aliases, bool required)
    {
        foreach (var alias in aliases)
        {
            if (metadata.ContainsKey(alias))
            {
                outputNames.Add(alias);
                return outputNames.Count - 1;
            }
        }

        if (!required)
            return -1;

        outputNames.Add(FirstMetadataKey(metadata) ?? aliases[0]);
        return outputNames.Count - 1;
    }

    private static string? FirstMetadataKey(IReadOnlyDictionary<string, NodeMetadata> metadata)
    {
        foreach (var key in metadata.Keys)
        {
            return key;
        }

        return null;
    }

    private void WarmUpSession(WorkerContext workerContext, ModelInfo modelInfo, ModelSize modelSize, CancellationToken token, bool debug)
    {
        var session = workerContext.Session;
        if (session == null || token.IsCancellationRequested)
            return;

        try
        {
            // First DML inference compiles kernels; paying that on a dummy frame keeps it off a real one
            int width = modelInfo.DeclaredWidth;
            int height = modelInfo.DeclaredHeight;
            if (width <= 0 || height <= 0)
            {
                width = height = GetEffectiveInputSize(_requestedInputSize, modelSize) / PatchSize * PatchSize;
            }

            using var inputValue = modelInfo.ViewCount >= 2
                                       ? OrtValue.CreateTensorValueFromMemory(new float[modelInfo.ViewCount * 3 * width * height], [1, modelInfo.ViewCount, 3, height, width])
                                       : modelInfo.InputIsBytes
                                           ? OrtValue.CreateTensorValueFromMemory(new byte[3 * width * height], [1, height, width, 3])
                                           : OrtValue.CreateTensorValueFromMemory(new float[3 * width * height], [1, 3, height, width]);
            workerContext.InputValues[0] = inputValue;

            var stopwatch = Stopwatch.StartNew();
            using var outputs = session.Run(workerContext.RunOptions, workerContext.InputNames, workerContext.InputValues!, workerContext.OutputNames);
            stopwatch.Stop();

            Log.Debug($"[DepthAnything] Warm-up inference ({width}x{height}) took {stopwatch.ElapsedMilliseconds}ms", this);
        }
        catch (Exception ex)
        {
            // Best-effort - a failure here resurfaces on the first real frame
            if (debug) Log.Debug($"[DepthAnything] Warm-up skipped: {ex.Message}", this);
        }
    }

    private InferenceSession CreateSession(string modelPath, ExecutionProvider provider)
    {
        // Bind the package's GPU natives to this assembly's P/Invokes on first use. The version must match
        // StreamDiffusion's: the first loaded native "onnxruntime.dll" module serves every provider import
        // of the process, so a different version fails to initialize (Error 1114) and falls back to CPU.
        OnnxNativeSelector.Select(typeof(InferenceSession).Assembly, OnnxNativeSelector.Build.Gpu);

        // Without a thread cap the CPU provider saturates all cores during inference
        // and starves the render thread
        int intraOpThreads = Math.Min(4, Math.Max(1, Environment.ProcessorCount));

        // Walk the chain and keep the first provider that initializes; CPU is always last and always works
        foreach (var candidate in GetProviderChain(provider))
        {
            if (!IsProviderAvailableOnThisPlatform(candidate))
            {
                continue;
            }

            string cudaMissing = string.Empty;
            if (candidate is ExecutionProvider.Cuda or ExecutionProvider.TensorRt
                && ((OperatingSystem.IsLinux() && !LinuxCudaRuntime.TryPrepare(out cudaMissing))
                    || (OperatingSystem.IsWindows() && !WindowsCudaRuntime.TryPrepare(out cudaMissing))))
            {
                Log.Info($"[DepthAnything] {ProviderLabel(candidate)} unavailable, trying next provider: {cudaMissing}", this);
                continue;
            }

            // TensorRT's provider DLL imports nvinfer_10.dll by name; load the runtime before asking ORT for the provider
            if (candidate == ExecutionProvider.TensorRt)
            {
                var tensorRtReady = OperatingSystem.IsLinux()
                                        ? LinuxCudaRuntime.TryPrepareTensorRt(out var trtMissing, out var trtDirectory)
                                        : WindowsCudaRuntime.TryPrepareTensorRt(out trtMissing, out trtDirectory);
                if (!tensorRtReady)
                {
                    Log.Info($"[DepthAnything] TensorRT unavailable, trying next provider: {trtMissing}", this);
                    continue;
                }

                Log.Info($"[DepthAnything] TensorRT runtime at {trtDirectory}", this);
            }

            SessionOptions? options = null;
            try
            {
                options = new SessionOptions
                {
                    // ORT 1.24 fails to initialize fp16 transformer graphs (SimplifiedLayerNormFusion) on the CPU at ENABLE_ALL; EXTENDED costs nothing measurable
                    GraphOptimizationLevel = candidate == ExecutionProvider.Cpu ? GraphOptimizationLevel.ORT_ENABLE_EXTENDED : GraphOptimizationLevel.ORT_ENABLE_ALL,
                    IntraOpNumThreads = intraOpThreads
                };

                switch (candidate)
                {
                    case ExecutionProvider.Cuda:
                        // HEURISTIC avoids the exhaustive cuDNN autotune at session creation. The DA3 exports have
                        // fixed input sizes, so the algorithm is chosen once and stays.
                        using (var cudaOptions = new OrtCUDAProviderOptions())
                        {
                            cudaOptions.UpdateOptions(new Dictionary<string, string>
                            {
                                ["device_id"] = "0",
                                ["cudnn_conv_algo_search"] = "HEURISTIC",
                            });
                            options.AppendExecutionProvider_CUDA(cudaOptions);
                        }
                        break;
                    case ExecutionProvider.TensorRt:
                        // Engines are expensive to build; persist them per model. The models live inside the package,
                        // so the cache belongs in the user profile rather than next to the asset.
                        var cacheRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        if (string.IsNullOrEmpty(cacheRoot))
                            cacheRoot = System.IO.Path.GetDirectoryName(modelPath) ?? ".";

                        var trtCache = System.IO.Path.Combine(cacheRoot, "TiXL", "trt_cache", System.IO.Path.GetFileNameWithoutExtension(modelPath));
                        System.IO.Directory.CreateDirectory(trtCache);
                        using (var trtOptions = new OrtTensorRTProviderOptions())
                        {
                            trtOptions.UpdateOptions(new Dictionary<string, string>
                            {
                                ["device_id"] = "0",
                                ["trt_max_workspace_size"] = "2147483648",
                                // fp16 engines: the widely-used da3-small.onnx export is fp32, and without
                                // this flag TensorRT would build a 2x slower fp32 engine for it
                                ["trt_fp16_enable"] = "1",
                                ["trt_engine_cache_enable"] = "1",
                                ["trt_engine_cache_path"] = trtCache,
                                ["trt_timing_cache_enable"] = "1",
                                ["trt_timing_cache_path"] = trtCache
                            });
                            options.AppendExecutionProvider_Tensorrt(trtOptions);
                        }

                        // TensorRT only claims the subgraphs it can accelerate; CUDA runs the rest instead of the CPU
                        options.AppendExecutionProvider_CUDA(0);
                        break;
                    case ExecutionProvider.CoreMl:
                        options.AppendExecutionProvider_CoreML(CoreMLFlags.COREML_FLAG_USE_NONE);
                        break;
                }

                // From the path rather than from bytes, so models with external weight files load too
                var session = new InferenceSession(modelPath, options);
                _activeProviderLabel = ProviderLabel(candidate);
                return session;
            }
            catch (Exception ex) when (candidate != ExecutionProvider.Cpu)
            {
                Log.Info($"[DepthAnything] {ProviderLabel(candidate)} unavailable, trying next provider: {ex.Message}", this);
                options?.Dispose();
            }
        }

        throw new InvalidOperationException("No ONNX execution provider could create a session");
    }

    private static ExecutionProvider[] GetProviderChain(ExecutionProvider requested)
    {
        // DirectML requests land here only from saved projects; 1.28+ ORT has no DML build and the chain
        // falls through to CUDA, so the graph keeps working with GPU speed instead of dying on CPU.
        return requested switch
        {
            ExecutionProvider.DirectMl => [ExecutionProvider.Cuda, ExecutionProvider.TensorRt, ExecutionProvider.CoreMl, ExecutionProvider.Cpu],
            ExecutionProvider.Cuda => [ExecutionProvider.Cuda, ExecutionProvider.TensorRt, ExecutionProvider.CoreMl, ExecutionProvider.Cpu],
            ExecutionProvider.TensorRt => [ExecutionProvider.TensorRt, ExecutionProvider.Cuda, ExecutionProvider.CoreMl, ExecutionProvider.Cpu],
            ExecutionProvider.CoreMl => [ExecutionProvider.CoreMl, ExecutionProvider.Cpu],
            _ => [ExecutionProvider.Cpu]
        };
    }

    /** CUDA and TensorRT run on Windows and Linux, CoreML only on macOS; DirectML no longer ships (ORT 1.28+ dropped it). */
    private static bool IsProviderAvailableOnThisPlatform(ExecutionProvider provider)
    {
        return provider switch
        {
            ExecutionProvider.DirectMl => false,
            ExecutionProvider.Cuda or ExecutionProvider.TensorRt => OperatingSystem.IsWindows() || OperatingSystem.IsLinux(),
            ExecutionProvider.CoreMl => OperatingSystem.IsMacOS(),
            _ => true
        };
    }

    private DepthEstimationResult ProcessFrame(WorkerContext workerContext, DepthEstimationRequest request, ModelInfo modelInfo, bool debug)
    {
        // Rented buffers handed to the returned result; the catch path returns anything not attached
        float[]? resultDepth = null;
        float[]? resultConfidence = null;
        float[]? resultSky = null;
        byte[]? colorData = null;
        try
        {
            var session = workerContext.Session;
            if (session == null || (request.InputData == null && request.InputBytes == null))
            {
                return ErrorResult(request, "No session or pixel data");
            }

            // A layout mismatch means the request came from a different model; fail clearly instead of an ORT type error
            if ((request.InputBytes != null) != modelInfo.InputIsBytes)
            {
                return ErrorResult(request, modelInfo.InputIsBytes
                                                ? "Input format mismatch: the model expects uint8 NHWC"
                                                : "Input format mismatch: the model expects float NCHW");
            }

            using var inputValue = modelInfo.ViewCount == 2
                                       ? CreatePairInput(workerContext, request)
                                       : modelInfo.ViewCount > 2
                                           ? CreateChunkInput(workerContext, request, modelInfo.ViewCount)
                                           : request.InputBytes != null
                                           ? OrtValue.CreateTensorValueFromMemory(request.InputBytes, [1, request.Height, request.Width, 3])
                                           : OrtValue.CreateTensorValueFromMemory(request.InputData!, [1, 3, request.Height, request.Width]);
            workerContext.InputValues[0] = inputValue;

            var stopwatch = Stopwatch.StartNew();
            using var outputs = session.Run(workerContext.RunOptions, workerContext.InputNames, workerContext.InputValues!, workerContext.OutputNames);
            stopwatch.Stop();
            var postStartTicks = Stopwatch.GetTimestamp();
            long poseTicks = 0;

            var depthData = CopyPlane(outputs[workerContext.DepthIndex], out int outputWidth, out int outputHeight);
            if (depthData == null)
            {
                return ErrorResult(request, "Inference did not produce a float depth tensor");
            }

            resultDepth = depthData;
            var paddedHeight = outputHeight;
            outputHeight = CropLetterbox(depthData, outputWidth, paddedHeight, request.PadRows);
            int count = outputWidth * outputHeight;

            if (workerContext.ConfidenceIndex >= 0)
            {
                resultConfidence = CopyPlane(outputs[workerContext.ConfidenceIndex], out _, out _);
                CropLetterbox(resultConfidence, outputWidth, paddedHeight, request.PadRows);
            }

            if (workerContext.SkyIndex >= 0)
            {
                resultSky = CopyPlane(outputs[workerContext.SkyIndex], out _, out _);
                CropLetterbox(resultSky, outputWidth, paddedHeight, request.PadRows);
            }

            var verticalFov = ReadVerticalFov(workerContext, outputs, outputHeight, modelInfo, outputWidth);
            var cameraPose = ReadCameraPose(workerContext, outputs);

            if (modelInfo.ViewCount > 2)
            {
                // The window's views share one depth scale and consistent poses; chain the window onto the previous one
                AccumulateChunkPose(workerContext, outputs, request, modelInfo.ViewCount);
                if (workerContext.WorldScale != 1f)
                {
                    for (int i = 0; i < count; i++)
                    {
                        resultDepth[i] *= workerContext.WorldScale;
                    }
                }
            }
            else if (modelInfo.ViewCount == 2)
            {
                // Chain the pair's relative pose onto the world pose and bring this frame's depth into the same units
                var promoteKeyframe = AccumulateViewPose(workerContext, outputs, count, request.InputData!);
                if (workerContext.WorldScale != 1f)
                {
                    for (int i = 0; i < count; i++)
                    {
                        resultDepth[i] *= workerContext.WorldScale;
                    }
                }

                if (promoteKeyframe || !workerContext.HasPreviousView)
                {
                    Array.Copy(request.InputData!, workerContext.PreviousInput!, workerContext.PreviousInputLength);
                    workerContext.HasPreviousView = true;
                }
            }

            if (modelInfo.IsMetric)
            {
                // The metric model predicts depth as seen by a 300 px reference camera; the real focal length turns it into meters.
                // FieldOfView is vertical, so the focal length is measured along the image height
                var focalPx = 0.5f * outputHeight / MathF.Tan(_workerFieldOfViewDegrees * MathF.PI / 360f);
                var scale = focalPx / MetricReferenceFocalPx;
                for (int i = 0; i < count; i++)
                {
                    resultDepth[i] *= scale;
                }
            }

            var smoothing = _workerSmoothing;
            SmoothDepth(workerContext, resultDepth, outputWidth, outputHeight, smoothing);

            // Ego-motion from depth: the community exports' extrinsics stay at identity
            // while the camera moves, so derive the trajectory from the depth sequence.
            // A live extrinsics output overrides the tracker whenever it moves.
            var intrinsicsSpan = workerContext.IntrinsicsIndex >= 0
                                     ? outputs[workerContext.IntrinsicsIndex].GetTensorDataAsSpan<float>()
                                     : ReadOnlySpan<float>.Empty;
            float fx, fy, cx, cy;
            if (intrinsicsSpan.Length >= 9 && intrinsicsSpan[4] > 0)
            {
                fx = intrinsicsSpan[0];
                fy = intrinsicsSpan[4];
                cx = intrinsicsSpan[2];
                cy = intrinsicsSpan[5];
            }
            else
            {
                var fovRadians = _workerFieldOfViewDegrees * MathF.PI / 180f;
                fy = 0.5f * outputHeight / MathF.Tan(fovRadians * 0.5f);

                // FieldOfView is vertical. A model whose aspect differs from the source (a 16:9 frame squeezed into a
                // square input) has non-square pixels, so fx follows the horizontal scale, not fy.
                var pixelAspect = request.SourceWidth > 0 && request.SourceHeight > 0
                                      ? (float)outputWidth / request.SourceWidth / ((float)outputHeight / request.SourceHeight)
                                      : 1f;
                fx = fy * pixelAspect;
                cx = outputWidth * 0.5f;
                cy = outputHeight * 0.5f;
            }

            if (modelInfo.ViewCount == 1 && !_workerTrackPose)
            {
                if (workerContext.PoseTracker.HasPose)
                    workerContext.PoseTracker.Reset();
            }
            else if (modelInfo.ViewCount == 1)
            {
                var cameraMotion = _workerCameraMotion;
                if (workerContext.PoseTracker.Motion != cameraMotion)
                {
                    workerContext.PoseTracker.Motion = cameraMotion;
                    workerContext.PoseTracker.Reset();
                }

                var poseStartTicks = Stopwatch.GetTimestamp();
                if (cameraMotion == DepthCameraMotion.RotationOnly && TryGetGray(workerContext, request, outputWidth, outputHeight) is { } gray)
                    workerContext.PoseTracker.UpdateFromImage(gray, outputWidth, outputHeight, fx, fy, cx, cy);
                else
                    workerContext.PoseTracker.Update(resultDepth, resultConfidence, outputWidth, outputHeight, fx, fy, cx, cy);
                poseTicks = Stopwatch.GetTimestamp() - poseStartTicks;
            }

            ComputeDepthRange(resultDepth, resultSky, count, _workerStabilizeSky, _workerPercentileNormalization, out float min, out float max);
            if (smoothing > 0 && workerContext.HasRange)
            {
                // The model's depth scale drifts between frames; a settled range keeps the visualization from pulsing
                min += (workerContext.SmoothedMin - min) * smoothing;
                max += (workerContext.SmoothedMax - max) * smoothing;
            }

            workerContext.SmoothedMin = min;
            workerContext.SmoothedMax = max;
            workerContext.HasRange = true;

            // Stabilize: clamp sky pixels to the foreground maximum so sky reads as consistently "far"
            if (_workerStabilizeSky && resultSky != null)
            {
                for (int i = 0; i < count; i++)
                {
                    if (resultSky[i] > 0.5f && float.IsFinite(resultDepth[i]))
                        resultDepth[i] = max;
                }
            }

            // At model resolution the GPU sampler upscales when the texture is
            // used downstream, which skips all full-resolution CPU work per result
            int resultWidth = outputWidth;
            int resultHeight = outputHeight;
            // Match the source's aspect ratio at model resolution: a square model's output is stretched back to the
            // frame's shape (392x392 -> 392x220 for 16:9). Upscaling to the source's pixel size would only add CPU work.
            if (_matchInputAspect && request.SourceWidth > 0 && request.SourceHeight > 0
                && GetAspectMatchedSize(outputWidth, outputHeight, request.SourceWidth, request.SourceHeight, out var matchedWidth, out var matchedHeight))
            {
                resultDepth = ResizePlane(depthData, outputWidth, outputHeight, matchedWidth, matchedHeight);
                ReturnDepthBuffer(depthData);
                resultWidth = matchedWidth;
                resultHeight = matchedHeight;

                if (resultConfidence != null)
                {
                    var resizedConfidence = ResizePlane(resultConfidence, outputWidth, outputHeight, resultWidth, resultHeight);
                    ReturnDepthBuffer(resultConfidence);
                    resultConfidence = resizedConfidence;
                }

                if (resultSky != null)
                {
                    var resizedSky = ResizePlane(resultSky, outputWidth, outputHeight, resultWidth, resultHeight);
                    ReturnDepthBuffer(resultSky);
                    resultSky = resizedSky;
                }
            }

            var outputFormat = _workerOutputFormat;
            var enhanceContrast = _workerEnhanceContrast;
            var invertDepth = _workerInvertDepth;
            colorData = GetBuffer(resultWidth * resultHeight * 4);
            ColorizeDepth(resultDepth, resultWidth * resultHeight, colorData, min, max, outputFormat, enhanceContrast, invertDepth, farIsLarge: true);

            if (!workerContext.LoggedFirstInference)
            {
                workerContext.LoggedFirstInference = true;
                Log.Debug($"[DepthAnything] First inference ok: {outputWidth}x{outputHeight}, {stopwatch.ElapsedMilliseconds}ms", this);
            }

            // Prefer the model's extrinsics whenever they actually move this frame; with the
            // community exports they never do, so the depth-tracked pose is what gets published
            var extrinsicsLive = false;
            if (cameraPose.HasPose)
            {
                var positionMoved = !workerContext.HasLastExt
                                    || Vector3.Distance(cameraPose.Position, workerContext.LastExtPosition) > 0.002f;
                var forwardChanged = !workerContext.HasLastExt
                                     || DegreesBetween(cameraPose.Forward, workerContext.LastExtForward) > 0.5f;
                if (positionMoved || forwardChanged)
                {
                    extrinsicsLive = true;
                    workerContext.InertFrames = 0;
                }
                else
                {
                    workerContext.InertFrames++;
                }

                workerContext.HasLastExt = true;
                workerContext.LastExtPosition = cameraPose.Position;
                workerContext.LastExtForward = cameraPose.Forward;
            }

            Vector3 posePosition;
            Vector3 poseForward;
            Vector3 poseUp;
            string poseSource;
            if (modelInfo.ViewCount >= 2)
            {
                var worldRotation = workerContext.WorldRotation;
                posePosition = workerContext.WorldPosition;
                poseForward = new Vector3(worldRotation[2], worldRotation[5], worldRotation[8]);
                poseUp = new Vector3(-worldRotation[1], -worldRotation[4], -worldRotation[7]);
                poseSource = "multi-view";
            }
            else if (extrinsicsLive)
            {
                posePosition = cameraPose.Position;
                poseForward = cameraPose.Forward;
                poseUp = cameraPose.Up;
                poseSource = "extrinsics";
            }
            else if (workerContext.PoseTracker.HasPose)
            {
                posePosition = workerContext.PoseTracker.Position;
                poseForward = workerContext.PoseTracker.Forward;
                poseUp = workerContext.PoseTracker.Up;
                poseSource = workerContext.PoseTracker.Motion == DepthCameraMotion.RotationOnly
                                 ? workerContext.PoseTracker.Source
                                 : workerContext.PoseTracker.Tracking ? "depth-tracking" : "re-anchored";
            }
            else
            {
                posePosition = Vector3.Zero;
                poseForward = Vector3.UnitZ;
                poseUp = -Vector3.UnitY;
                poseSource = "reference";
            }

            // Every pose source lives in the model's OpenCV frame (y down, +Z forward); TiXL's world
            // is y up looking along -Z, so the published camera flips over, matching AxisPtzCamera
            posePosition = Vector3.Transform(posePosition, OpenCvToWorld);
            poseForward = Vector3.Transform(poseForward, OpenCvToWorld);
            poseUp = Vector3.Transform(poseUp, OpenCvToWorld);

            return new DepthEstimationResult
            {
                DepthData = resultDepth,
                ConfidenceData = resultConfidence,
                SkyData = resultSky,
                ColorData = colorData,
                Width = resultWidth,
                Height = resultHeight,
                Min = min,
                Max = max,
                VerticalFovDegrees = verticalFov,
                HasCameraPose = modelInfo.ViewCount >= 2 || extrinsicsLive || workerContext.PoseTracker.HasPose,
                PoseTrusted = modelInfo.ViewCount >= 2 || extrinsicsLive || workerContext.PoseTracker.Motion != DepthCameraMotion.RotationOnly || workerContext.PoseTracker.Tracking,
                CameraPosition = posePosition,
                CameraForward = poseForward,
                CameraUp = poseUp,
                PoseSource = poseSource,
                InferenceMs = (float)stopwatch.Elapsed.TotalMilliseconds,
                PostMs = (float)((Stopwatch.GetTimestamp() - postStartTicks) * 1000.0 / Stopwatch.Frequency),
                PoseMs = (float)(poseTicks * 1000.0 / Stopwatch.Frequency),
                CaptureTicks = request.CaptureTicks,
                ColorFormat = outputFormat,
                ColorEnhance = enhanceContrast,
                ColorInvert = invertDepth,
                FarIsLarge = true
            };
        }
        catch (Exception ex)
        {
            if (debug) Log.Error($"[DepthAnything] ProcessFrame error: {ex.Message}", this);
            ReturnDepthBuffer(resultDepth);
            ReturnDepthBuffer(resultConfidence);
            ReturnDepthBuffer(resultSky);
            ReturnBufferToPool(colorData);
            return ErrorResult(request, ex.Message);
        }
    }

    /** Vertical field of view in degrees from the model's camera estimate, or from the FieldOfView input for the metric model; 0 when unknown. */
    private float ReadVerticalFov(WorkerContext workerContext, IReadOnlyList<OrtValue> outputs, int outputHeight, ModelInfo modelInfo, int outputWidth)
    {
        if (workerContext.IntrinsicsIndex >= 0)
        {
            // Row-major 3x3 camera matrix on the output pixel grid: fx, 0, cx / 0, fy, cy / 0, 0, 1
            var intrinsics = outputs[workerContext.IntrinsicsIndex].GetTensorDataAsSpan<float>();
            if (intrinsics.Length >= 9)
            {
                var focalY = intrinsics[intrinsics.Length - 9 + 4];
                if (focalY > 0)
                {
                    return 2f * MathF.Atan(outputHeight / (2f * focalY)) * 180f / MathF.PI;
                }
            }
        }

        if (modelInfo.IsMetric)
        {
            return _workerFieldOfViewDegrees;
        }

        return 0f;
    }

    /** Copies a [.., H, W] float output into a pooled array; null when the tensor isn't float. */
    private float[]? CopyPlane(OrtValue value, out int width, out int height)
    {
        width = 0;
        height = 0;

        var info = value.GetTensorTypeAndShape();
        if (info.ElementDataType != TensorElementType.Float || info.Shape.Length < 2)
            return null;

        height = (int)info.Shape[^2];
        width = (int)info.Shape[^1];

        // Copy out before the result collection is disposed. Pooled arrays may exceed w*h
        // (size-class rounding) - downstream code must use explicit pixel counts
        var span = value.GetTensorDataAsSpan<float>();
        if (span.Length < width * height)
            return null;

        var buffer = TakeDepthBuffer(width * height);
        // A multi-view output holds one plane per view; the current frame is the last one
        span.Slice(span.Length - width * height, width * height).CopyTo(buffer);
        return buffer;
    }

    private static void SmoothDepth(WorkerContext workerContext, float[] depth, int width, int height, float smoothing)
    {
        var count = width * height;
        var previous = workerContext.PreviousDepth;
        if (smoothing > 0 && previous != null && workerContext.PreviousWidth == width && workerContext.PreviousHeight == height)
        {
            for (int i = 0; i < count; i++)
            {
                var current = depth[i];
                var last = previous[i];
                if (float.IsFinite(current) && float.IsFinite(last))
                {
                    depth[i] = current + (last - current) * smoothing;
                }
            }
        }

        if (previous == null || previous.Length != count)
        {
            previous = new float[count];
            workerContext.PreviousDepth = previous;
        }

        Array.Copy(depth, previous, count);
        workerContext.PreviousWidth = width;
        workerContext.PreviousHeight = height;
    }

    private static DepthEstimationResult ErrorResult(DepthEstimationRequest request, string message)
    {
        return new DepthEstimationResult
        {
            Width = request.Width,
            Height = request.Height,
            Error = message
        };
    }

    /** The model-resolution size with the source's aspect ratio; false when the output already has it. */
    private static bool GetAspectMatchedSize(int width, int height, int sourceWidth, int sourceHeight, out int matchedWidth, out int matchedHeight)
    {
        var sourceAspect = (float)sourceWidth / sourceHeight;
        var longSide = Math.Max(width, height);
        if (sourceAspect >= 1f)
        {
            matchedWidth = longSide;
            matchedHeight = Math.Max(1, (int)MathF.Round(longSide / sourceAspect));
        }
        else
        {
            matchedHeight = longSide;
            matchedWidth = Math.Max(1, (int)MathF.Round(longSide * sourceAspect));
        }

        return matchedWidth != width || matchedHeight != height;
    }

    private float[] ResizePlane(float[] sourceDepth, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var resized = TakeDepthBuffer(targetWidth * targetHeight);

        float scaleX = (float)sourceWidth / targetWidth;
        float scaleY = (float)sourceHeight / targetHeight;

        Parallel.For(0, targetHeight, y =>
        {
            int srcY = Math.Min((int)(y * scaleY), sourceHeight - 1);
            int rowOffset = srcY * sourceWidth;
            int dstRow = y * targetWidth;
            for (int x = 0; x < targetWidth; x++)
            {
                int srcX = Math.Min((int)(x * scaleX), sourceWidth - 1);
                resized[dstRow + x] = sourceDepth[rowOffset + srcX];
            }
        });

        return resized;
    }

    /// <summary>
    /// Depth range for the visualization. Sky pixels are excluded when the model predicts a sky plane -
    /// their far-plane values would otherwise stretch the range and wash out the foreground. In percentile
    /// mode the 2nd/98th quantiles bound the range instead of the extremes, ignoring outlier spikes.
    /// </summary>
    private static void ComputeDepthRange(float[] depthData, float[]? skyData, int count, bool excludeSky, bool percentile, out float minDepth, out float maxDepth)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            float depth = depthData[i];
            if (float.IsNaN(depth) || float.IsInfinity(depth))
                continue;
            if (excludeSky && skyData != null && skyData[i] > 0.5f)
                continue;
            if (depth < min)
                min = depth;
            if (depth > max)
                max = depth;
        }

        if (min == float.MaxValue)
        {
            minDepth = 0.0f;
            maxDepth = 1.0f;
            return;
        }

        if (percentile)
        {
            // 256-bin histogram over the finite, non-sky range; the 2%/98% quantiles bound the range
            var bins = new int[256];
            var scale = 255f / MathF.Max(1e-6f, max - min);
            var sampled = 0;
            for (int i = 0; i < count; i++)
            {
                float depth = depthData[i];
                if (float.IsNaN(depth) || float.IsInfinity(depth))
                    continue;
                if (excludeSky && skyData != null && skyData[i] > 0.5f)
                    continue;
                bins[(int)((depth - min) * scale)]++;
                sampled++;
            }

            if (sampled > 0)
            {
                var lowTarget = sampled * 2 / 100;
                var highTarget = sampled * 98 / 100;
                float low = min, high = max;
                var seen = 0;
                for (var bin = 0; bin < 256; bin++)
                {
                    seen += bins[bin];
                    if (seen >= lowTarget)
                    {
                        low = min + (bin + 0.5f) / scale;
                        break;
                    }
                }

                seen = 0;
                for (var bin = 255; bin >= 0; bin--)
                {
                    seen += bins[bin];
                    if (seen >= sampled - highTarget)
                    {
                        high = min + (bin + 0.5f) / scale;
                        break;
                    }
                }

                if (high > low)
                {
                    min = low;
                    max = high;
                }
            }
        }

        minDepth = min;
        maxDepth = max;
    }

    private void StopWorker(bool debug)
    {
        lock (_workerLock)
        {
            _cancellationTokenSource?.Cancel();
            try
            {
                // Never wait for a running inference on the render thread - the worker
                // disposes its own session when it notices the cancellation
                _processingTask?.Wait(0);
            }
            catch (Exception ex)
            {
                if (debug) Log.Error($"[DepthAnything] Error waiting for worker task: {ex.Message}", this);
            }

            _workerWake.Set();
            _preprocessor.Cancel();

            while (_inputQueue.TryDequeue(out _))
            {
            }

            while (_outputQueue.TryDequeue(out var stale))
            {
                ReturnResultBuffers(stale);
            }

            // Pauses request submission during re-init; the exiting worker disposes its own session
            _modelInfo = null;
            _requestInFlight = false;
        }
    }

    private void PauseProcessing()
    {
        // Disabling must not tear down the worker - re-enabling would reload the model
        if (_processingTask == null)
            return;

        _hasPendingInput = false;
        // Re-enabling only recaptures when the input turns dirty or its texture changes. A paused op that is
        // resumed on the same static texture would otherwise never capture again and stay blank forever.
        _lastInputTexture = null;
        _preprocessor.Cancel();

        while (_inputQueue.TryDequeue(out _))
        {
        }

        // Also drops results an in-flight request produced after it was paused
        while (_outputQueue.TryDequeue(out var stale))
        {
            ReturnResultBuffers(stale);
        }
    }

    #endregion

    #region Memory Management

    private byte[] GetBuffer(int size)
    {
        return _bufferPool?.RentByte(size) ?? new byte[size];
    }

    private void ReturnBufferToPool(byte[]? b)
    {
        if (b != null)
            _bufferPool?.ReturnByte(b);
    }

    private float[] TakeDepthBuffer(int length)
    {
        return _bufferPool?.RentFloat(length) ?? new float[length];
    }

    private void ReturnDepthBuffer(float[]? buffer)
    {
        if (buffer != null)
            _bufferPool?.ReturnFloat(buffer);
    }

    #endregion

    #region Output Updates

    private void ApplyResult(DepthEstimationResult result, DepthOutputFormat outputFormat, bool enhanceContrast, bool invertDepth)
    {
        var depthData = result.DepthData;
        int width = result.Width;
        int height = result.Height;

        // The pool guarantees >= w*h - a shortfall is a logic bug, not data to patch up
        if (depthData == null || depthData.Length < width * height)
        {
            Log.Warning($"[DepthAnything] Discarding malformed depth result ({depthData?.Length ?? 0} floats < {width * height})", this);
            ClearOutputs();
            return;
        }

        MinDepth.Value = result.Min;
        MaxDepth.Value = result.Max;

        // Update depth texture (R32_Float)
        UploadFloatTexture(ref _depthTexture, depthData, width, height);
        DepthTexture.Value = _depthTexture;

        ConfidenceTexture.Value = UploadOptionalPlane(ref _confidenceTexture, result.ConfidenceData, width, height);
        SkyTexture.Value = UploadOptionalPlane(ref _skyTexture, result.SkyData, width, height);

        // Update normalized depth texture (R8G8B8A8_UNorm)
        var colorData = result.ColorData;
        if (colorData != null && colorData.Length >= width * height * 4
            && result.ColorFormat == outputFormat && result.ColorEnhance == enhanceContrast && result.ColorInvert == invertDepth)
        {
            UploadRgbaTexture(colorData, width, height);
        }
        else
        {
            // The worker colorized with different settings - colorize on the spot
            int count = width * height;
            if (_normalizedBuffer == null || _normalizedBuffer.Length < count * 4)
            {
                _normalizedBuffer = new byte[count * 4];
            }

            ColorizeDepth(depthData, count, _normalizedBuffer, result.Min, result.Max, outputFormat, enhanceContrast, invertDepth, result.FarIsLarge);
            UploadRgbaTexture(_normalizedBuffer, width, height);
        }
    }

    private Texture2D? UploadOptionalPlane(ref Texture2D? texture, float[]? data, int width, int height)
    {
        if (data == null || data.Length < width * height)
            return null;

        UploadFloatTexture(ref texture, data, width, height);
        return texture;
    }

    private static unsafe void UploadFloatTexture(ref Texture2D? texture, float[] data, int width, int height)
    {
        if (texture == null || texture.Description.Width != width || texture.Description.Height != height)
        {
            texture?.Dispose();
            texture = CreateTexture(width, height, Format.R32_Float);
        }

        fixed (float* pointer = data)
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(new DataBox((IntPtr)pointer, width * sizeof(float), 0), texture);
        }
    }

    private unsafe void UploadRgbaTexture(byte[] rgbaData, int width, int height)
    {
        if (_normalizedDepthTexture == null || _normalizedDepthTexture.Description.Width != width || _normalizedDepthTexture.Description.Height != height)
        {
            _normalizedDepthTexture?.Dispose();
            _normalizedDepthTexture = CreateTexture(width, height, Format.R8G8B8A8_UNorm);
        }

        fixed (byte* pointer = rgbaData)
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(new DataBox((IntPtr)pointer, width * 4, 0), _normalizedDepthTexture);
        }

        NormalizedDepthTexture.Value = _normalizedDepthTexture;
    }

    private static Texture2D CreateTexture(int width, int height, Format format)
    {
        return new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, new Texture2DDescription
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
        }));
    }

    private static void ColorizeDepth(float[] depthData, int pixelCount, byte[] rgbaData, float minDepth, float maxDepth,
                                      DepthOutputFormat outputFormat, bool enhanceContrast, bool invertDepth, bool farIsLarge)
    {
        float range = maxDepth - minDepth;
        if (range < 0.0001f) range = 1.0f;

        for (int i = 0; i < pixelCount; i++)
        {
            float depth = depthData[i];
            if (float.IsNaN(depth) || float.IsInfinity(depth))
                depth = minDepth;

            float normalizedDepth = (depth - minDepth) / range;

            // V2 predicts disparity (near is large), V3 predicts depth (far is large); near stays bright for both
            if (farIsLarge)
                normalizedDepth = 1.0f - normalizedDepth;

            // Invert if requested
            if (invertDepth)
                normalizedDepth = 1.0f - normalizedDepth;

            // Enhance contrast
            if (enhanceContrast)
            {
                normalizedDepth = (normalizedDepth - 0.5f) * 2.0f;
            }

            normalizedDepth = Math.Clamp(normalizedDepth, 0.0f, 1.0f);
            byte depthByte = (byte)(normalizedDepth * 255);

            int rgbaIdx = i * 4;

            // Apply color format
            switch (outputFormat)
            {
                case DepthOutputFormat.Color:
                    // Mid-range pixels glow green, extremes drift toward red/blue.
                    // The green term goes negative near 0, so clamp before the cast.
                    int green = 255 - Math.Abs(depthByte - 128) * 2;
                    rgbaData[rgbaIdx + 0] = depthByte;
                    rgbaData[rgbaIdx + 1] = (byte)Math.Clamp(green, 0, 255);
                    rgbaData[rgbaIdx + 2] = (byte)(255 - depthByte);
                    rgbaData[rgbaIdx + 3] = 255;
                    break;

                case DepthOutputFormat.Rainbow:
                    var rgb = HsvToRgb(normalizedDepth, 1.0f, 1.0f);
                    rgbaData[rgbaIdx + 0] = (byte)(rgb.X * 255);
                    rgbaData[rgbaIdx + 1] = (byte)(rgb.Y * 255);
                    rgbaData[rgbaIdx + 2] = (byte)(rgb.Z * 255);
                    rgbaData[rgbaIdx + 3] = 255;
                    break;

                default:
                    rgbaData[rgbaIdx + 0] = depthByte;
                    rgbaData[rgbaIdx + 1] = depthByte;
                    rgbaData[rgbaIdx + 2] = depthByte;
                    rgbaData[rgbaIdx + 3] = 255;
                    break;
            }
        }
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        if (s == 0)
            return new Vector3(v, v, v);

        float i = (int)(h * 6);
        float f = h * 6 - i;
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float t = v * (1 - (1 - f) * s);

        return i switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q)
        };
    }

    private Texture2D? _depthTexture;
    private Texture2D? _confidenceTexture;
    private Texture2D? _skyTexture;
    private Texture2D? _normalizedDepthTexture;

    #endregion

    #region Input Parameters

    [Input(Guid = "12345678-1234-1234-1234-123456789012")]
    public readonly InputSlot<Texture2D> InputTexture = new();

    [Input(Guid = "23456789-2345-2345-2345-234567890123")]
    public readonly InputSlot<bool> Enabled = new(true);

    [Input(Guid = "34567890-3456-3456-3456-345678901234", MappedType = typeof(ModelSize))]
    public readonly InputSlot<int> ModelSizeParam = new((int)ModelSize.Da3Small);

    [Input(Guid = "90123456-9012-9012-9012-901234567890", MappedType = typeof(Resolution))]
    public readonly InputSlot<int> InputResolution = new((int)Resolution.Auto);

    /// <summary>Defaults to CUDA so all ONNX operators share one GPU native build; macOS normalizes CUDA onto CoreML.</summary>
    [Input(Guid = "012345ab-cdef-0123-4567-89abcdef0123", MappedType = typeof(ExecutionProvider))]
    public readonly InputSlot<int> ExecutionProviderParam = new((int)ExecutionProvider.Auto);

    [Input(Guid = "234567cd-8912-9012-3456-6789012345ab")]
    public readonly InputSlot<bool> MatchInputAspect = new(false);

    [Input(Guid = "d5c7a0fc-9eab-4c6e-b058-a06e2c4d7fbd")]
    public readonly InputSlot<float> Smoothing = new(0.25f);

    [Input(Guid = "f7e9c21e-bbcd-4e80-8a7a-c2800e6f91df")]
    public readonly InputSlot<float> FieldOfView = new(60f);

    [Input(Guid = "45678901-4567-4567-4567-456789012345", MappedType = typeof(DepthOutputFormat))]
    public readonly InputSlot<int> OutputFormat = new();

    /// <summary>
    /// How the NormalizedDepthTexture and color output map depth to brightness: Full Range stretches
    /// the exact frame minimum/maximum (sky pixels excluded when the model predicts sky); Percentile
    /// bounds the range at the 2nd/98th quantiles so outlier spikes do not compress the foreground.
    /// </summary>
    [Input(Guid = "3f4a5b6c-7d8e-4f90-a1b2-c3d4e5f6a7b8", MappedType = typeof(DepthNormalization))]
    public readonly InputSlot<int> Normalization = new();

    /// <summary>
    /// How sky pixels are treated: Stabilize (default) excludes them from the depth range and clamps
    /// their depth to the foreground maximum, so the output reads sky as consistently "far" instead of
    /// flickering with the model's raw sky values. Keep publishes the raw model output untouched.
    /// </summary>
    [Input(Guid = "d2e3f4a5-b6c7-4d8e-9f0a-1b2c3d4e5f60", MappedType = typeof(SkyHandling))]
    public readonly InputSlot<int> SkyHandlingParam = new((int)SkyHandling.Stabilize);

    /// <summary>
    /// How the physical camera moves. RotationOnly suits pan/tilt (PTZ) and tripod cameras: the camera pose may only
    /// turn, so depth-scale wobble cannot drift its position. Static keeps the reference pose.
    /// </summary>
    [Input(Guid = "e4f1c2a7-5b3d-4c8e-9a6f-2d7b1e0c3f58", MappedType = typeof(DepthCameraMotion))]
    public readonly InputSlot<int> CameraMotion = new((int)DepthCameraMotion.Free);

    [Input(Guid = "56789012-5678-5678-5678-567890123456")]
    public readonly InputSlot<bool> EnhanceContrast = new(false);

    [Input(Guid = "67890123-6789-6789-6789-678901234567")]
    public readonly InputSlot<bool> InvertDepth = new(false);

    [Input(Guid = "78901234-7890-7890-7890-789012345678")]
    public readonly InputSlot<bool> Debug = new(false);

    #endregion

    #region Enums

    /// <summary>
    /// The numeric values are saved in projects; new models are only ever appended. The V2-era values
    /// (0-4) keep their slots so old files still load; they are normalized onto their DA3 equivalents
    /// at runtime (the V2 models are no longer shipped or downloaded).
    /// </summary>
    public enum ModelSize
    {
        Small,
        Base,
        Large,
        SmallFp32,
        SmallInt8,
        Da3Small,
        Da3Base,
        Da3MonoLarge,
        Da3MetricLarge,
        Da3Large,
        Da3Giant,
        Da3NestedGiant,
        Da3BaseTracked,
        Da3BaseStream
    }

    /// <summary>
    /// Inference input resolution. Auto uses the model's native size; smaller
    /// sizes trade depth detail for speed. Values are multiples of the ViT patch size.
    /// Models exported at a fixed size ignore it.
    /// </summary>
    public enum Resolution
    {
        Auto,
        R196,
        R280,
        R392,
        R518,
        R504
    }

    public enum ExecutionProvider
    {
        Cpu,
        DirectMl,
        Cuda,
        CoreMl,
        TensorRt,

        /** Resolved at session setup to the fastest engine this machine can actually run. */
        Auto
    }

    public enum DepthOutputFormat
    {
        Grayscale,
        Color,
        Rainbow
    }

    /// <summary>Visualization range strategy; values are saved in projects and only ever appended.</summary>
    public enum DepthNormalization
    {
        FullRange,
        Percentile
    }

    /// <summary>Sky treatment; values are saved in projects and only ever appended.</summary>
    public enum SkyHandling
    {
        Keep,
        Stabilize
    }

    #endregion

    #region ICustomDropdownHolder

    private static readonly string[] OutputFormatNames = { "Grayscale", "Color", "Rainbow" };
    private static readonly string[] NormalizationNames = { "Full Range", "Percentile" };
    private static readonly string[] SkyHandlingNames = { "Keep Raw", "Stabilize (clamp to far)" };

    /** The dropdown only offers DA3 models; the index maps onto the ModelSize enum value. */
    private static readonly (string Name, ModelSize Size)[] ModelSizeOptions =
    {
        ("V3 Small", ModelSize.Da3Small),
        ("V3 Base", ModelSize.Da3Base),
        ("V3 Large", ModelSize.Da3Large),
        ("V3 Mono Large", ModelSize.Da3MonoLarge),
        ("V3 Metric Large", ModelSize.Da3MetricLarge),
        ("V3 Giant", ModelSize.Da3Giant),
        ("V3 Nested Giant", ModelSize.Da3NestedGiant),
        ("V3 Base (camera tracking)", ModelSize.Da3BaseTracked),
        ("V3 Base (6-view stream)", ModelSize.Da3BaseStream),
    };

    private static readonly string[] ResolutionNames = { "Auto", "196", "280", "392", "518", "504" };
    /**
     * The dropdown only lists the execution providers this operating system can run:
     * Windows/Linux: CPU, CUDA, TensorRT - macOS: CPU, CoreML.
     * The value maps onto the ExecutionProvider enum, so the order is free.
     */
    private static readonly (string Name, ExecutionProvider Provider)[] ExecutionProviderOptions = BuildExecutionProviderOptions();

    private static (string, ExecutionProvider)[] BuildExecutionProviderOptions()
    {
        var options = new List<(string, ExecutionProvider)> { ("Auto (recommended)", ExecutionProvider.Auto) };
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            options.Add(("CPU", ExecutionProvider.Cpu));
            options.Add(("CUDA", ExecutionProvider.Cuda));
            options.Add(("TensorRT", ExecutionProvider.TensorRt));
        }
        else if (OperatingSystem.IsMacOS())
        {
            options.Add(("CPU", ExecutionProvider.Cpu));
            options.Add(("CoreML", ExecutionProvider.CoreMl));
        }

        return options.ToArray();
    }

    /** The engine "Auto" stands for on this machine, probed with the cached runtime checks. */
    internal static ExecutionProvider ResolveAutoProvider()
    {
        if (OperatingSystem.IsMacOS())
            return ExecutionProvider.CoreMl;

        string reason;
        var trtOk = OperatingSystem.IsWindows()
                        ? WindowsCudaRuntime.TryPrepareTensorRt(out reason, out _)
                        : LinuxCudaRuntime.TryPrepareTensorRt(out reason, out _);
        if (trtOk)
            return ExecutionProvider.TensorRt;

        var cudaOk = OperatingSystem.IsWindows()
                         ? WindowsCudaRuntime.TryPrepare(out reason)
                         : LinuxCudaRuntime.TryPrepare(out reason);
        if (cudaOk)
            return ExecutionProvider.Cuda;

        // DirectML cannot run the DepthAnything session (the provider is not wired up for it), so the honest fallback is the CPU
        return ExecutionProvider.Cpu;
    }

    /** Label for the session logs and the status line; the dropdowns read the option table instead. */
    private static string ProviderLabel(ExecutionProvider provider) => provider switch
    {
        ExecutionProvider.DirectMl => "DirectML",
        ExecutionProvider.Cuda => "CUDA",
        ExecutionProvider.TensorRt => "TensorRT",
        ExecutionProvider.CoreMl => "CoreML",
        _ => "CPU"
    };

    /** Values that cannot run (saved DirectML anywhere, a CoreML on Windows/Linux) snap onto the closest provider that works; Auto resolves to the fastest engine available. */
    private static ExecutionProvider NormalizeProviderForOs(ExecutionProvider provider)
    {
        if (provider == ExecutionProvider.Auto)
            return ResolveAutoProvider();

        if (OperatingSystem.IsMacOS())
        {
            return provider == ExecutionProvider.Cpu ? ExecutionProvider.Cpu : ExecutionProvider.CoreMl;
        }

        if (provider == ExecutionProvider.DirectMl)
        {
            return ExecutionProvider.Cuda;
        }

        if (OperatingSystem.IsWindows())
        {
            return provider == ExecutionProvider.CoreMl ? ExecutionProvider.Cuda : provider;
        }

        // Linux: CoreML cannot run here
        return provider == ExecutionProvider.CoreMl ? ExecutionProvider.Cuda : provider;
    }

    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == OutputFormat.Id)
        {
            var val = _uiOutputFormat;
            return val >= 0 && val < OutputFormatNames.Length ? OutputFormatNames[val] : "Grayscale";
        }

        if (inputId == Normalization.Id)
        {
            var val = _uiNormalization;
            return val >= 0 && val < NormalizationNames.Length ? NormalizationNames[val] : "Full Range";
        }

        if (inputId == SkyHandlingParam.Id)
        {
            var val = _uiSkyHandling;
            return val >= 0 && val < SkyHandlingNames.Length ? SkyHandlingNames[val] : "Stabilize";
        }

        if (inputId == ModelSizeParam.Id)
        {
            var size = (ModelSize)_uiModelSize;
            return ModelSizeOptions.FirstOrDefault(o => o.Size == size).Name ?? "V3 Small";
        }

        if (inputId == InputResolution.Id)
        {
            var val = _uiInputResolution;
            return val >= 0 && val < ResolutionNames.Length ? ResolutionNames[val] : "Auto";
        }

        if (inputId == ExecutionProviderParam.Id)
        {
            var raw = (ExecutionProvider)_uiRawExecutionProvider;
            if (raw == ExecutionProvider.Auto)
            {
                var resolved = (ExecutionProvider)_uiExecutionProvider;
                var resolvedName = ExecutionProviderOptions.FirstOrDefault(o => o.Provider == resolved).Name ?? "CPU";
                return $"Auto ({resolvedName})";
            }

            var provider = (ExecutionProvider)_uiExecutionProvider;
            return ExecutionProviderOptions.FirstOrDefault(o => o.Provider == provider).Name ?? "CPU";
        }

        return "";
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == OutputFormat.Id)
        {
            foreach (var name in OutputFormatNames)
                yield return name;
            yield break;
        }

        if (inputId == Normalization.Id)
        {
            foreach (var name in NormalizationNames)
                yield return name;
            yield break;
        }

        if (inputId == SkyHandlingParam.Id)
        {
            foreach (var name in SkyHandlingNames)
                yield return name;
            yield break;
        }

        if (inputId == ModelSizeParam.Id)
        {
            foreach (var option in ModelSizeOptions)
                yield return option.Name;
            yield break;
        }

        if (inputId == InputResolution.Id)
        {
            foreach (var name in ResolutionNames)
                yield return name;
            yield break;
        }

        if (inputId == ExecutionProviderParam.Id)
        {
            foreach (var option in ExecutionProviderOptions)
                yield return option.Name;
            yield break;
        }

        yield return "undefined";
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected))
            return;

        if (inputId == OutputFormat.Id)
        {
            var index = Array.IndexOf(OutputFormatNames, selected);
            if (index >= 0)
                OutputFormat.SetTypedInputValue(index);
            return;
        }

        if (inputId == Normalization.Id)
        {
            var index = Array.IndexOf(NormalizationNames, selected);
            if (index >= 0)
                Normalization.SetTypedInputValue(index);
            return;
        }

        if (inputId == SkyHandlingParam.Id)
        {
            var index = Array.IndexOf(SkyHandlingNames, selected);
            if (index >= 0)
                SkyHandlingParam.SetTypedInputValue(index);
            return;
        }

        if (inputId == ModelSizeParam.Id)
        {
            var index = Array.FindIndex(ModelSizeOptions, o => o.Name == selected);
            if (index >= 0)
                ModelSizeParam.SetTypedInputValue((int)ModelSizeOptions[index].Size);
            return;
        }

        if (inputId == InputResolution.Id)
        {
            var index = Array.IndexOf(ResolutionNames, selected);
            if (index >= 0)
                InputResolution.SetTypedInputValue(index);
            return;
        }

        if (inputId == ExecutionProviderParam.Id)
        {
            var index = Array.FindIndex(ExecutionProviderOptions, o => o.Name == selected);
            if (index >= 0)
                ExecutionProviderParam.SetTypedInputValue((int)ExecutionProviderOptions[index].Provider);
            return;
        }
    }

    #endregion

    #region Camera reference

    public Vector3 CameraPosition { get; set; }
    public Vector3 CameraTarget { get; set; }
    public float CameraRoll { get; set; }
    public CameraDefinition CameraDefinition => _cameraDefinition;
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 CameraToClipSpace { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }

    private CameraDefinition _cameraDefinition = new();
    private DepthEstimationResult? _loggedPoseResult;

    #endregion

    #region Cleanup

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch { }

        _workerWake.Set();
        // The session is disposed by the worker's own exit path - disposing it here
        // could race an in-flight Run on the worker thread

        _preprocessor.Dispose();

        _depthTexture?.Dispose();
        _depthTexture = null;

        _confidenceTexture?.Dispose();
        _confidenceTexture = null;

        _skyTexture?.Dispose();
        _skyTexture = null;

        _normalizedDepthTexture?.Dispose();
        _normalizedDepthTexture = null;

        base.Dispose(isDisposing);
    }

    #endregion
}

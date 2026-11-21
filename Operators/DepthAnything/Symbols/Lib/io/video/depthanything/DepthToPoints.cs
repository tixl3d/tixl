using T3.Core.Resource;
using T3.Graphics.Compat;

namespace Lib.io.video.depthanything;

/// <summary>
/// Unprojects a depth map into a camera-space point buffer, one point per pixel (or per
/// PixelStride-th pixel). Feed the result into DrawPoints with UsePointsScale enabled to
/// get live depth splats: DrawPoints billboards each point in camera space, and Scale
/// carries the world-space splat diameter derived from the depth.
/// </summary>
[Guid("9ba5a498-af7a-45ec-84b8-b5540932e78b")]
public sealed class DepthToPoints : Instance<DepthToPoints>, IStatusProvider
{
    #region Outputs

    [Output(Guid = "111c5692-cce7-4c9b-a3e7-520b50477e40", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> Points = new();

    [Output(Guid = "c56de3f1-a90c-4b13-960c-4b59bfdb8f32", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> PointCount = new();

    #endregion

    public DepthToPoints()
    {
        Points.UpdateAction = Update;
        // Pulling only the count must still build the buffer once; pulling both must not dispatch twice.
        PointCount.UpdateAction = context =>
                                      {
                                          Points.GetValue(context);
                                          PointCount.Value = _lastPointCount;
                                      };
    }

    private void Update(EvaluationContext context)
    {
        _lastPointCount = 0;
        _lastError = null;

        if (!Enabled.GetValue(context))
        {
            Points.Value = null;
            PointCount.Value = 0;
            return;
        }

        var depthTexture = DepthTexture.GetValue(context);
        if (depthTexture == null || depthTexture.IsDisposed)
        {
            Points.Value = null;
            return;
        }

        var description = depthTexture.Description;
        if (description.Format != Format.R32_Float)
        {
            SetError($"Unsupported depth format {description.Format}; connect DepthAnything's DepthTexture (R32_Float).");
            Points.Value = null;
            return;
        }

        var colorTexture = ColorTexture.GetValue(context);
        if (colorTexture is { IsDisposed: true })
            colorTexture = null;

        var confidenceTexture = ConfidenceTexture.GetValue(context);
        if (confidenceTexture is { IsDisposed: true })
            confidenceTexture = null;

        var depthSrv = SrvManager.GetSrvForTexture(depthTexture);
        if (depthSrv == null)
        {
            SetError("The depth texture can't be read by a shader.");
            Points.Value = null;
            return;
        }

        var colorSrv = colorTexture != null ? SrvManager.GetSrvForTexture(colorTexture) : null;
        var confidenceSrv = confidenceTexture != null ? SrvManager.GetSrvForTexture(confidenceTexture) : null;

        var pixelStride = Math.Clamp(PixelStride.GetValue(context), 1, 64);
        var maxPoints = Math.Max(256, MaxPoints.GetValue(context));
        ComputeGrid(description.Width, description.Height, ref pixelStride, maxPoints, out var gridWidth, out var gridHeight);

        var pointCount = gridWidth * gridHeight;
        if (pointCount <= 0)
        {
            Points.Value = null;
            return;
        }

        var fov = FieldOfView.GetValue(context);
        if (fov <= 0)
            fov = 50;

        var smoothing = Math.Clamp(TemporalSmoothing.GetValue(context), 0f, 0.95f);

        var parameters = new Parameters
                             {
                                 TanHalfFovY = MathF.Tan(Math.Clamp(fov, 1f, 179f) * MathF.PI / 360f),
                                 DepthScale = DepthScale.GetValue(context),
                                 SplatSizePixels = Math.Max(0.1f, SplatSizePixels.GetValue(context)),
                                 Opacity = Math.Clamp(Opacity.GetValue(context), 0f, 1f),
                                 HasColor = colorSrv != null ? 1u : 0u,
                                 HasConfidence = confidenceSrv != null ? 1u : 0u,
                                 PixelStride = (uint)pixelStride,
                                 GridWidth = (uint)gridWidth,
                                 GridHeight = (uint)gridHeight,
                                 ConfidenceThreshold = ConfidenceThreshold.GetValue(context),
                                 TemporalSmoothing = smoothing
                             };

        EnsureBuffers(pointCount);
        var outputBuffer = _outputBuffer;
        var historyBuffer = _historyBuffer;
        if (outputBuffer.Buffer == null || outputBuffer.Uav == null)
        {
            SetError("Could not allocate the point buffer.");
            Points.Value = null;
            return;
        }

        // D3D11 doesn't draw this buffer: the compute shader writes it, and the backend only renders
        // CPU-uploaded point buffers here. Without this the chain just renders black while PointCount
        // reports the full count, which reads as a broken graph.
        WarnOnceAboutBackend();

        // The previous frame is only a valid history when the buffer layout did not change.
        var hasHistory = _hasHistory && smoothing > 0f && historyBuffer.Srv != null;
        parameters.HasHistory = hasHistory ? 1u : 0u;

        _shaderResource ??= ResourceManager.CreateShaderResource<ComputeShader>(
            "DepthAnything:shaders/depth-to-points-cs.hlsl", this, () => "main", null);

        var shader = _shaderResource.Value;
        if (shader == null)
        {
            SetError("The depth-to-points compute shader is not available.");
            Points.Value = null;
            return;
        }

        if (_constants == null)
            ResourceManager.SetupConstBuffer(parameters, ref _constants);
        else
            ResourceManager.UpdateConstBuffer(parameters, _constants!);

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;
        csStage.Set(shader);
        csStage.SetConstantBuffer(0, _constants);
        csStage.SetShaderResource(0, colorSrv);
        csStage.SetShaderResource(1, depthSrv);
        csStage.SetShaderResource(2, confidenceSrv);
        csStage.SetShaderResource(3, hasHistory ? historyBuffer.Srv : null);
        csStage.SetUnorderedAccessView(0, outputBuffer.Uav);

        const int threadGroupSize = 256;
        deviceContext.Dispatch((pointCount + threadGroupSize - 1) / threadGroupSize, 1, 1);

        csStage.SetUnorderedAccessView(0, null);
        csStage.SetShaderResource(0, null);
        csStage.SetShaderResource(1, null);
        csStage.SetShaderResource(2, null);
        csStage.SetShaderResource(3, null);
        csStage.Set(null);

        Points.Value = outputBuffer;
        PointCount.Value = pointCount;
        _lastPointCount = pointCount;

        // Keep the output buffer object stable for downstream consumers (DrawPoints caches its
        // bindings); the next frame blends against a separate history copy.
        if (smoothing > 0f && historyBuffer.Buffer != null)
        {
            deviceContext.CopyResource(outputBuffer.Buffer, historyBuffer.Buffer);
            _hasHistory = true;
        }
        else
        {
            _hasHistory = false;
        }
    }

    /// <summary>
    /// Keeps the point count under <paramref name="maxPoints"/> by growing the pixel stride,
    /// rather than truncating the buffer and dropping the bottom of the image.
    /// </summary>
    private static void ComputeGrid(int width, int height, ref int stride, int maxPoints, out int gridWidth, out int gridHeight)
    {
        while (true)
        {
            gridWidth = (width + stride - 1) / stride;
            gridHeight = (height + stride - 1) / stride;
            if ((long)gridWidth * gridHeight <= maxPoints || stride >= 64)
                return;

            stride++;
        }
    }

    private void EnsureBuffers(int pointCount)
    {
        if (_pointCount == pointCount && _outputBuffer.Buffer != null && _historyBuffer.Buffer != null)
            return;

        var sizeInBytes = pointCount * Point.Stride;
        SetupBuffer(_outputBuffer, sizeInBytes);
        SetupBuffer(_historyBuffer, sizeInBytes);

        _pointCount = pointCount;
        _hasHistory = false;
    }

    private static void SetupBuffer(BufferWithViews buffer, int sizeInBytes)
    {
        ResourceManager.SetupStructuredBuffer(sizeInBytes, Point.Stride, ref buffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
        ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
    }

    private void SetError(string message)
    {
        _lastError = message;
        Log.Warning($"[DepthToPoints] {message}", this);
    }

    /// <summary>
    /// The D3D11 backend does not draw point buffers that a compute shader wrote, so the splat chain is
    /// black there however healthy the points are. Say so once rather than leaving a silently empty view.
    /// </summary>
    private void WarnOnceAboutBackend()
    {
        if (_loggedBackend)
            return;

        _loggedBackend = true;
        if (ResourceManager.Device?.Backend is not { } backend)
            return;

        if (!backend.GetType().Name.Contains("D3D11", StringComparison.Ordinal))
            return;

        Log.Warning("[DepthToPoints] The D3D11 backend does not draw compute-written point buffers - " +
                    "connect the points to DrawPoints after switching the editor to Vulkan (TIXL_BACKEND=vulkan). " +
                    "PointCount still reports the full count here, the view is just empty.", this);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return _lastError == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _lastError ?? string.Empty;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _outputBuffer.Dispose();
        _historyBuffer.Dispose();
        _constants?.Dispose();
        _constants = null;
        base.Dispose(isDisposing);
    }

    // Padding keeps the constant buffer at whole 16-byte rows.
#pragma warning disable CS0649
    private struct Parameters
    {
        public float TanHalfFovY;
        public float DepthScale;
        public float SplatSizePixels;
        public float Opacity;
        public uint HasColor;
        public uint HasConfidence;
        public uint PixelStride;
        public uint GridWidth;
        public uint GridHeight;
        public float ConfidenceThreshold;
        public float TemporalSmoothing;
        public uint HasHistory;
        public float Padding1;
        public float Padding2;
        public float Padding3;
    }
#pragma warning restore CS0649

    private readonly BufferWithViews _outputBuffer = new();
    private readonly BufferWithViews _historyBuffer = new();
    private Resource<ComputeShader>? _shaderResource;
    private Buffer? _constants;
    private int _pointCount;
    private bool _hasHistory;
    private int _lastPointCount;
    private string? _lastError;
    private bool _loggedBackend;

    #region Inputs

    [Input(Guid = "bd22de74-c0b5-430b-b374-1f50a1d08acf")]
    public readonly InputSlot<Texture2D?> ColorTexture = new();

    [Input(Guid = "13e8c899-4847-4fbe-b05c-b146c28f6e1a")]
    public readonly InputSlot<Texture2D?> DepthTexture = new();

    [Input(Guid = "e694fb2e-1b13-469e-92ad-1f71eea71f49")]
    public readonly InputSlot<Texture2D?> ConfidenceTexture = new();

    [Input(Guid = "3acf35fe-7218-4011-8b7d-5f107c4a4b4a")]
    public readonly InputSlot<float> FieldOfView = new();

    [Input(Guid = "3494a9de-be76-4408-948f-f33a03de56d8")]
    public readonly InputSlot<float> DepthScale = new();

    [Input(Guid = "22ec87ea-deb5-405e-a109-1130e27b0a59")]
    public readonly InputSlot<int> PixelStride = new();

    [Input(Guid = "782f0918-4efe-46fc-8a2f-3456d509e2d4")]
    public readonly InputSlot<int> MaxPoints = new();

    [Input(Guid = "aad0c94b-e4a8-483a-a99b-7854a3a20dea")]
    public readonly InputSlot<float> SplatSizePixels = new();

    [Input(Guid = "23dd5434-b077-4780-9166-53307e7562fa")]
    public readonly InputSlot<float> Opacity = new();

    [Input(Guid = "e18fb9ca-7d80-4d53-9435-b7087701c0c3")]
    public readonly InputSlot<float> TemporalSmoothing = new();

    [Input(Guid = "b62bb5a6-fd54-49a0-abb5-8e860c036f50")]
    public readonly InputSlot<float> ConfidenceThreshold = new();

    [Input(Guid = "447053fe-f28b-41c6-ae16-d841e14e0b34")]
    public readonly InputSlot<bool> Enabled = new();

    #endregion
}

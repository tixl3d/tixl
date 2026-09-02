#nullable enable
using SharpDX.Direct3D11;

namespace Lib.render.skinning;

/// <summary>
/// Deforms a point buffer with skin weights and skinning matrices - the point
/// counterpart of [SkinMesh], so particles and point effects can ride a rig.
/// Weights come from [BindToSkeleton] (with the same point buffer connected).
/// </summary>
[Guid("d17f4b86-2ea9-4c50-b6d3-90c58a31f7e2")]
internal sealed class SkinPoints : Instance<SkinPoints>, IStatusProvider
{
    [Output(Guid = "49C0D6E2-8F75-4B18-A39C-E2617D80B4F5")]
    public readonly Slot<BufferWithViews?> Result = new();

    public SkinPoints()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var points = Points.GetValue(context);
        var weights = SkinWeights.GetValue(context);
        var matrices = SkinMatrices.GetValue(context);

        if (points?.Srv == null || points.Srv.IsDisposed)
        {
            Result.Value = null;
            return;
        }

        if (weights?.Srv == null || weights.Srv.IsDisposed
            || matrices?.Srv == null || matrices.Srv.IsDisposed)
        {
            _lastErrorMessage = "SkinWeights or SkinMatrices missing - passing points through unskinned";
            Result.Value = points;
            return;
        }

        var pointCount = points.Srv.Description.Buffer.ElementCount;
        var weightsCount = weights.Srv.Description.Buffer.ElementCount;
        if (weightsCount < pointCount)
        {
            _lastErrorMessage = $"Skin weights cover {weightsCount} of {pointCount} points - passing points through unskinned";
            Result.Value = points;
            return;
        }

        _shaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.ComputeShader>(
            "Lib:shaders/cs/SkinPoints-cs.hlsl", this, () => "main", null);

        var shader = _shaderResource.Value;
        if (shader == null)
        {
            _lastErrorMessage = "Skinning compute shader is not available";
            Result.Value = points;
            return;
        }

        ResourceManager.SetupStructuredBuffer(pointCount * Point.Stride, Point.Stride, ref _resultBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_resultBuffer.Buffer, ref _resultBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(_resultBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _resultBuffer.Uav);

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;

        csStage.Set(shader);
        csStage.SetShaderResource(0, points.Srv);
        csStage.SetShaderResource(1, weights.Srv);
        csStage.SetShaderResource(2, matrices.Srv);
        csStage.SetUnorderedAccessView(0, _resultBuffer.Uav);

        const int threadGroupSize = 64;
        deviceContext.Dispatch(pointCount / threadGroupSize + 1, 1, 1);

        csStage.SetUnorderedAccessView(0, null);
        csStage.SetShaderResource(0, null);
        csStage.SetShaderResource(1, null);
        csStage.SetShaderResource(2, null);
        csStage.Set(null);

        Result.Value = _resultBuffer;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _resultBuffer.Dispose();
    }

    #region status provider
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return _lastErrorMessage == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _lastErrorMessage ?? string.Empty;
    }
    #endregion

    private Resource<T3.Core.DataTypes.ComputeShader>? _shaderResource;
    private readonly BufferWithViews _resultBuffer = new();
    private string? _lastErrorMessage;

    [Input(Guid = "05e8a3c1-76b9-4d42-90f6-3ad25c81e7b0")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "8b3d90f4-c1e6-4a27-b584-67f0a92d3c15")]
    public readonly InputSlot<BufferWithViews> SkinWeights = new();

    [Input(Guid = "3a76c2e9-05d8-4f31-8c40-b9e1d64f52a7")]
    public readonly InputSlot<BufferWithViews> SkinMatrices = new();
}

#nullable enable
using SharpDX.Direct3D11;
using T3.Core.Rendering;

namespace Lib.render.skinning;

/// <summary>
/// Deforms a mesh on the GPU with skin weights and matrices, producing a regular
/// mesh in the standard vertex layout so all downstream mesh operators keep working.
/// </summary>
[Guid("c3d8f6b2-51ae-47c9-8e04-b96d20a15f73")]
internal sealed class SkinMesh : Instance<SkinMesh>, IStatusProvider
{
    [Output(Guid = "8A05E9D4-27C3-4B61-9F88-51E6B3A0C792")]
    public readonly Slot<MeshBuffers?> Result = new();

    public SkinMesh()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = null;

        var mesh = Mesh.GetValue(context);
        var weights = SkinWeights.GetValue(context);
        var matrices = SkinMatrices.GetValue(context);

        if (mesh?.VertexBuffer?.Srv == null || mesh.VertexBuffer.Srv.IsDisposed)
        {
            Result.Value = null;
            return;
        }

        if (weights?.Srv == null || weights.Srv.IsDisposed
            || matrices?.Srv == null || matrices.Srv.IsDisposed)
        {
            _lastErrorMessage = "SkinWeights or SkinMatrices missing - passing mesh through unskinned";
            Result.Value = mesh;
            return;
        }

        var vertexCount = mesh.VertexBuffer.Srv.Description.Buffer.ElementCount;
        var weightsCount = weights.Srv.Description.Buffer.ElementCount;
        if (weightsCount < vertexCount)
        {
            _lastErrorMessage = $"Skin weights cover {weightsCount} of {vertexCount} vertices - passing mesh through unskinned";
            Result.Value = mesh;
            return;
        }

        if (_shaderResource == null)
        {
            _shaderResource = ResourceManager.CreateShaderResource<T3.Core.DataTypes.ComputeShader>(ShaderPath, this, () => "main", null);
        }

        var shader = _shaderResource.Value;
        if (shader == null)
        {
            _lastErrorMessage = "Skinning compute shader is not available";
            Result.Value = mesh;
            return;
        }

        ResourceManager.SetupStructuredBuffer(vertexCount * PbrVertex.Stride, PbrVertex.Stride, ref _skinnedVertexBuffer.Buffer);
        ResourceManager.CreateStructuredBufferSrv(_skinnedVertexBuffer.Buffer, ref _skinnedVertexBuffer.Srv);
        ResourceManager.CreateStructuredBufferUav(_skinnedVertexBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _skinnedVertexBuffer.Uav);

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;

        csStage.Set(shader);
        csStage.SetShaderResource(0, mesh.VertexBuffer.Srv);
        csStage.SetShaderResource(1, weights.Srv);
        csStage.SetShaderResource(2, matrices.Srv);
        csStage.SetUnorderedAccessView(0, _skinnedVertexBuffer.Uav);

        const int threadGroupSize = 64;
        deviceContext.Dispatch(vertexCount / threadGroupSize + 1, 1, 1);

        csStage.SetUnorderedAccessView(0, null);
        csStage.SetShaderResource(0, null);
        csStage.SetShaderResource(1, null);
        csStage.SetShaderResource(2, null);
        csStage.Set(null);

        // Index and chunk buffers are shared with the source mesh - only vertices are replaced
        _resultMesh.VertexBuffer = _skinnedVertexBuffer;
        _resultMesh.IndicesBuffer = mesh.IndicesBuffer;
        _resultMesh.ChunkDefsBuffer = mesh.ChunkDefsBuffer;
        Result.Value = _resultMesh;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        // _resultMesh shares the source mesh's index/chunk buffers - only the vertex buffer is owned here
        _skinnedVertexBuffer.Dispose();
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

    private const string ShaderPath = "Lib:shaders/cs/SkinMeshVertices-cs.hlsl";
    private Resource<T3.Core.DataTypes.ComputeShader>? _shaderResource;
    private readonly BufferWithViews _skinnedVertexBuffer = new();
    private readonly MeshBuffers _resultMesh = new();
    private string? _lastErrorMessage;

    [Input(Guid = "62e91c07-84fa-4d3b-a45c-08d7f9126b3e")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "d94a70b6-1f28-45c1-83b7-6ea0d5429c15")]
    public readonly InputSlot<BufferWithViews> SkinWeights = new();

    [Input(Guid = "3b7f2ac9-e650-48d2-b12a-94c8607df3e6")]
    public readonly InputSlot<BufferWithViews> SkinMatrices = new();
}

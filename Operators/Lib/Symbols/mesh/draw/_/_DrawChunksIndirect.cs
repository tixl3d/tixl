#nullable enable
using System.Runtime.InteropServices;
using T3.Graphics.Compat;
using T3.Core.Rendering;

namespace Lib.mesh.draw._;

/// <summary>
/// Draws the chunks of a mesh at points with a single indexed multi-draw indirect command. A compute pass writes
/// one draw record per point (the index range of the chunk it references, with the point index as first
/// instance), so the draw needs neither a readback nor a per-face table, and vertices are reused within each
/// chunk. The records are rebuilt only when the mesh, the chunk indices or the point count change.
/// </summary>
[Guid("dcaba500-d78b-4193-9235-7666ab332385")]
internal sealed class _DrawChunksIndirect : Instance<_DrawChunksIndirect>
{
    [Output(Guid = "b66ff964-72bf-41e3-8f1c-5f2d567b5b17", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    public _DrawChunksIndirect()
    {
        Output.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        // Read before GetValue, which clears them. Point positions don't affect the records, only their count.
        var structureChanged = Mesh.DirtyFlag.IsDirty || ChunkIndices.DirtyFlag.IsDirty;

        var points = GPoints.GetValue(context);
        var mesh = Mesh.GetValue(context);
        var chunkIndices = ChunkIndices.GetValue(context);
        var forceUpdate = ForceUpdate.GetValue(context);

        var pointsBuffer = points?.Buffer;
        var chunkDefs = mesh?.ChunkDefsBuffer;
        var indices = mesh?.IndicesBuffer?.Buffer;
        if (pointsBuffer == null || pointsBuffer.IsDisposed
            || chunkDefs?.Buffer == null || chunkDefs.Buffer.IsDisposed || chunkDefs.Srv == null
            || indices == null || indices.IsDisposed)
            return;

        var pointCount = pointsBuffer.Description.SizeInBytes / Point.Stride;
        var chunkCount = chunkDefs.Buffer.Description.SizeInBytes / MeshChunkDef.Stride;
        var hasChunkIndices = chunkIndices?.Buffer != null && !chunkIndices.Buffer.IsDisposed && chunkIndices.Srv != null;
        var chunkIndexCount = hasChunkIndices ? chunkIndices!.Buffer!.Description.SizeInBytes / sizeof(int) : 0;
        if (pointCount == 0 || chunkCount == 0)
            return;

        var deviceContext = ResourceManager.Device.ImmediateContext;
        if (deviceContext.VertexShader.Shader == null || deviceContext.PixelShader.Shader == null)
            return;

        var countsChanged = pointCount != _pointCount || chunkCount != _chunkCount || chunkIndexCount != _chunkIndexCount;
        if (structureChanged || countsChanged || forceUpdate || _indirectArgs == null)
        {
            if (!WriteDrawArgs(pointCount, chunkCount, chunkIndexCount, chunkDefs.Srv, hasChunkIndices ? chunkIndices!.Srv : null))
                return;

            _pointCount = pointCount;
            _chunkCount = chunkCount;
            _chunkIndexCount = chunkIndexCount;
        }

        // The mesh's int3 index buffer is consecutive 32-bit indices.
        deviceContext.InputAssembler.SetIndexBuffer(indices, Format.R32_UInt, 0);
        deviceContext.DrawIndexedInstancedIndirect(_indirectArgs!, 0, pointCount, DeviceContext.DrawIndexedInstancedIndirectArgsStride);
        deviceContext.InputAssembler.SetIndexBuffer(null, Format.R32_UInt, 0);
    }

    private bool WriteDrawArgs(int pointCount, int chunkCount, int chunkIndexCount, ShaderResourceView chunkDefsSrv,
                               ShaderResourceView? chunkIndicesSrv)
    {
        _shaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.ComputeShader>(ShaderPath, this, () => "main");
        var shader = _shaderResource.Value;
        if (shader == null)
            return false;

        const int stride = DeviceContext.DrawIndexedInstancedIndirectArgsStride;
        var sizeInBytes = pointCount * stride;
        ResourceManager.SetupStructuredBuffer(sizeInBytes, stride, ref _argsWritten.Buffer);
        ResourceManager.CreateStructuredBufferUav(_argsWritten.Buffer, UnorderedAccessViewBufferFlags.None, ref _argsWritten.Uav);
        if (_argsWritten.Uav == null)
            return false;

        // A structured buffer can't be an indirect argument buffer on every backend, so the records are copied into one.
        if (_indirectArgs == null || _indirectArgs.IsDisposed || _indirectArgs.Description.SizeInBytes != sizeInBytes)
        {
            _indirectArgs?.Dispose();
            _indirectArgs = new Buffer(ResourceManager.Device,
                                       new BufferDescription
                                           {
                                               SizeInBytes = sizeInBytes,
                                               Usage = ResourceUsage.Default,
                                               BindFlags = BindFlags.None,
                                               OptionFlags = ResourceOptionFlags.DrawIndirectArguments,
                                           });
        }

        _params = new DrawArgsParams { PointCount = pointCount, ChunkCount = chunkCount, ChunkIndexCount = chunkIndexCount };
        ResourceManager.SetupConstBuffer(_params, ref _paramsBuffer);

        var deviceContext = ResourceManager.Device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;
        csStage.Set(shader);
        csStage.SetConstantBuffer(0, _paramsBuffer);
        csStage.SetShaderResource(0, chunkIndicesSrv);
        csStage.SetShaderResource(1, chunkDefsSrv);
        csStage.SetUnorderedAccessView(0, _argsWritten.Uav);
        deviceContext.Dispatch((pointCount + ThreadGroupSize - 1) / ThreadGroupSize, 1, 1);

        csStage.SetUnorderedAccessView(0, null);
        csStage.SetShaderResource(0, null);
        csStage.SetShaderResource(1, null);
        csStage.SetConstantBuffer(0, null);

        deviceContext.CopyResource(_argsWritten.Buffer!, _indirectArgs);
        return true;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _argsWritten.Dispose();
        _indirectArgs?.Dispose();
        _paramsBuffer?.Dispose();
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct DrawArgsParams
    {
        [FieldOffset(0)]
        public int PointCount;

        [FieldOffset(4)]
        public int ChunkCount;

        [FieldOffset(8)]
        public int ChunkIndexCount;
    }

    private const string ShaderPath = "Lib:shaders/3d/mesh/chunks/MeshChunks-WriteDrawArgs.hlsl";
    private const int ThreadGroupSize = 256;

    private Resource<T3.Core.DataTypes.ComputeShader>? _shaderResource;
    private readonly BufferWithViews _argsWritten = new();
    private Buffer? _indirectArgs;
    private Buffer? _paramsBuffer;
    private DrawArgsParams _params;
    private int _pointCount = -1;
    private int _chunkCount = -1;
    private int _chunkIndexCount = -1;

    [Input(Guid = "b79e3a8e-ceac-4d4d-998d-27f125217896")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "dea56386-8447-4f3e-8c1a-bef2ec4f3795")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "595ea09b-f995-4359-8acc-3cb5ce2fe5fb")]
    public readonly InputSlot<BufferWithViews> ChunkIndices = new();

    [Input(Guid = "a5e83232-0ece-4f14-b2a3-6965b44a95e4")]
    public readonly InputSlot<bool> ForceUpdate = new();
}

#nullable enable
using System;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.Rendering;

namespace Lib.mesh.draw._;

/// <summary>
/// Builds the per-face draw table for chunk instancing: for every point, the faces
/// of the chunk it references. The table only depends on the structure (point count,
/// chunk indices, chunk definitions), not on point positions, so it is rebuilt only
/// when one of those changes - the readback of the two small structure buffers then
/// happens once instead of every frame.
/// </summary>
[Guid("2d7c4a9e-6b13-4f58-9a0e-c5d1e8f3b726")]
internal sealed class _BuildChunkDrawData : Instance<_BuildChunkDrawData>
{
    [Output(Guid = "8e1f5c2a-d743-4b09-a6e8-3f9c0b7d1a54")]
    public readonly Slot<BufferWithViews?> DrawData = new();

    [Output(Guid = "5a9d3e71-c826-4f04-b1d7-e0c4a8f2b693")]
    public readonly Slot<int> VertexCount = new();

    [Output(Guid = "c3b7f1e5-9a40-4d62-8e5b-2d6f0c1a7e38")]
    public readonly Slot<int> FaceCount = new();

    public _BuildChunkDrawData()
    {
        DrawData.UpdateAction = Update;
        VertexCount.UpdateAction = Update;
        FaceCount.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var points = GPoints.GetValue(context);
        var mesh = Mesh.GetValue(context);
        var chunkIndices = ChunkIndices.GetValue(context);
        var everyFrame = UpdateEveryFrame.GetValue(context);
        DrawData.DirtyFlag.Trigger = everyFrame ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;

        var pointsBuffer = points?.Buffer;
        var chunkDefsBuffer = mesh?.ChunkDefsBuffer?.Buffer;
        var chunkIndicesBuffer = chunkIndices?.Buffer;
        if (pointsBuffer == null || pointsBuffer.IsDisposed || chunkDefsBuffer == null || chunkDefsBuffer.IsDisposed)
        {
            DrawData.Value = null;
            VertexCount.Value = 0;
            FaceCount.Value = 0;
            return;
        }

        var pointCount = pointsBuffer.Description.SizeInBytes / Point.Stride;
        var chunkCount = chunkDefsBuffer.Description.SizeInBytes / MeshChunkDef.Stride;
        var chunkIndexCount = chunkIndicesBuffer != null && !chunkIndicesBuffer.IsDisposed
                                  ? chunkIndicesBuffer.Description.SizeInBytes / sizeof(int)
                                  : 0;

        var structureKey = HashCode.Combine(pointCount, chunkCount, chunkIndexCount,
                                            chunkDefsBuffer.NativePointer, chunkIndicesBuffer?.NativePointer ?? IntPtr.Zero);
        if (!everyFrame && structureKey == _structureKey && DrawData.Value != null)
            return;

        _structureKey = structureKey;
        if (pointCount == 0 || chunkCount == 0)
        {
            DrawData.Value = null;
            VertexCount.Value = 0;
            FaceCount.Value = 0;
            return;
        }

        ReadBack(chunkDefsBuffer, ref _chunkDefs, ref _chunkDefsStaging, chunkCount);
        if (chunkIndexCount > 0)
            ReadBack(chunkIndicesBuffer!, ref _chunkIndices, ref _chunkIndicesStaging, chunkIndexCount);

        var totalFaces = 0;
        for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            totalFaces += _chunkDefs[ChunkForPoint(pointIndex, chunkIndexCount, chunkCount)].FaceCount;
        }

        if (_entries.Length < totalFaces)
            _entries = new DrawEntry[totalFaces];

        var write = 0;
        for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            var def = _chunkDefs[ChunkForPoint(pointIndex, chunkIndexCount, chunkCount)];
            for (var face = 0; face < def.FaceCount; face++)
            {
                _entries[write++] = new DrawEntry(pointIndex, def.StartFaceIndex + face);
            }
        }

        // The buffer keeps its capacity; the draw only covers the first totalFaces entries
        ResourceManager.SetupBufferWithViews(_entries, ref _drawData);
        DrawData.Value = _drawData;
        FaceCount.Value = totalFaces;
        VertexCount.Value = totalFaces * 3;
    }

    private int ChunkForPoint(int pointIndex, int chunkIndexCount, int chunkCount)
    {
        var chunk = chunkIndexCount > 0 ? _chunkIndices[pointIndex % chunkIndexCount] : pointIndex;
        return ((chunk % chunkCount) + chunkCount) % chunkCount;
    }

    /// <summary>One synchronous staging copy - only on structure changes, never per frame.</summary>
    private static void ReadBack<T>(Buffer source, ref T[] target, ref Buffer? staging, int count) where T : unmanaged
    {
        if (target.Length < count)
            target = new T[count];

        if (staging == null || staging.IsDisposed || staging.Description.SizeInBytes != source.Description.SizeInBytes)
        {
            staging?.Dispose();
            staging = new Buffer(ResourceManager.Device,
                                 new BufferDescription
                                     {
                                         SizeInBytes = source.Description.SizeInBytes,
                                         Usage = ResourceUsage.Staging,
                                         BindFlags = BindFlags.None,
                                         CpuAccessFlags = CpuAccessFlags.Read,
                                         OptionFlags = ResourceOptionFlags.None,
                                         StructureByteStride = source.Description.StructureByteStride,
                                     });
        }

        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.CopyResource(source, staging);
        deviceContext.MapSubresource(staging, MapMode.Read, MapFlags.None, out var stream);
        try
        {
            stream.ReadRange(target, 0, count);
        }
        finally
        {
            deviceContext.UnmapSubresource(staging, 0);
            stream.Dispose();
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _chunkDefsStaging?.Dispose();
        _chunkIndicesStaging?.Dispose();
        _drawData?.Dispose();
    }

    /// <summary>Mirrors DrawData in DrawChunksAtPoints.hlsl: the point placing the face and the face's index into the mesh index buffer.</summary>
    private readonly record struct DrawEntry(int PointIndex, int FaceIndex);

    private int _structureKey;
    private MeshChunkDef[] _chunkDefs = [];
    private int[] _chunkIndices = [];
    private DrawEntry[] _entries = [];
    private Buffer? _chunkDefsStaging;
    private Buffer? _chunkIndicesStaging;
    private BufferWithViews? _drawData;

    [Input(Guid = "6f2a8d4c-1e97-4b35-a0c6-d8e3b5f7c219")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "b9e4c7a1-5d06-4f83-9c2e-7a1f3d8b6e05")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "3c8f1b6d-a2e5-4d79-b4a0-9e6c2f5d8a17")]
    public readonly InputSlot<BufferWithViews> ChunkIndices = new();

    [Input(Guid = "e7d3a5f9-8c21-4e64-a1b8-5f0d9c3e7b42")]
    public readonly InputSlot<bool> UpdateEveryFrame = new();
}

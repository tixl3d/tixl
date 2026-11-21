using Lib.Utils;
using T3.Core.Rendering;
using T3.Graphics.Compat;

namespace Lib.geometry;

/// <summary>
/// Reads a GPU mesh back into a <see cref="MeshGeometry"/>, so meshes from [LoadGltf], [CubeMesh] and the other mesh
/// operators can feed geometry operators and physics. The readback stalls the GPU briefly, so it only runs when the
/// incoming mesh changed.
/// </summary>
[Guid("f4489290-24d9-4533-86ce-a5f5972ea952")]
internal sealed class MeshToGeometry : Instance<MeshToGeometry>
{
    [Output(Guid = "8fe1e5eb-c3f4-4d5e-a869-c9e6725ec7fc")]
    public readonly Slot<MeshGeometry> Result = new();

    public MeshToGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var mesh = Mesh.GetValue(context);
        var weld = WeldPoints.GetValue(context);
        try
        {
            if (mesh == null
                || !TryRead(mesh.VertexBuffer, PbrVertex.Stride, ref _vertices, ref _vertexStaging, out var vertexCount)
                || !TryRead(mesh.IndicesBuffer, 3 * sizeof(int), ref _triangles, ref _indexStaging, out var triangleCount)
                || vertexCount == 0
                || triangleCount == 0)
            {
                Result.Value = null;
                return;
            }

            // Most mesh operators have no chunk buffer; a mesh without one is a single part.
            var chunkCount = 0;
            if (mesh.ChunkDefsBuffer?.Buffer != null)
                TryRead(mesh.ChunkDefsBuffer, MeshChunkDef.Stride, ref _chunks, ref _chunkStaging, out chunkCount);

            if (!MeshBuffersToGeometry.Build(_vertices.AsSpan(0, vertexCount), _triangles.AsSpan(0, triangleCount), _chunks.AsSpan(0, chunkCount),
                                             weld, _output, _weldLookup))
            {
                WarnOnce("MeshToGeometry: the mesh has indices outside its vertices");
                Result.Value = null;
                return;
            }

            _hasWarned = false;
            Result.Value = _output;
        }
        catch (Exception e)
        {
            WarnOnce($"MeshToGeometry: reading the mesh failed: {e.Message}");
            Result.Value = null;
        }
    }

    /// <summary>Copies a structured buffer through a staging buffer that is kept while its size stays the same.</summary>
    private static bool TryRead<T>(BufferWithViews source, int expectedStride, ref T[] data, ref Buffer staging, out int count) where T : struct
    {
        count = 0;
        if (source?.Buffer == null || source.Buffer.IsDisposed || source.ElementStride != expectedStride)
            return false;

        count = source.ElementCount;
        if (count <= 0)
            return false;

        var device = ResourceManager.Device;
        var immediateContext = device.ImmediateContext;
        var description = source.Buffer.Description;
        if (staging == null || staging.IsDisposed || staging.Description.SizeInBytes != description.SizeInBytes)
        {
            staging?.Dispose();
            description.Usage = ResourceUsage.Staging;
            description.BindFlags = BindFlags.None;
            description.CpuAccessFlags = CpuAccessFlags.Read;
            description.OptionFlags = ResourceOptionFlags.None;
            staging = new Buffer(device, description);
        }

        immediateContext.CopyResource(source.Buffer, staging);
        var dataBox = immediateContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            if (data.Length < count)
                data = new T[count];

            GraphicsUtilities.Read(dataBox.DataPointer, data, 0, count);
            return true;
        }
        finally
        {
            immediateContext.UnmapSubresource(staging, 0);
        }
    }

    private void WarnOnce(string message)
    {
        if (_hasWarned)
            return;

        Log.Warning(message, this);
        _hasWarned = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _vertexStaging?.Dispose();
            _indexStaging?.Dispose();
            _chunkStaging?.Dispose();
        }

        base.Dispose(disposing);
    }

    private readonly MeshGeometry _output = new();
    private readonly Dictionary<Vector3, int> _weldLookup = new();
    private PbrVertex[] _vertices = [];
    private Int3[] _triangles = [];
    private MeshChunkDef[] _chunks = [];
    private Buffer _vertexStaging;
    private Buffer _indexStaging;
    private Buffer _chunkStaging;
    private bool _hasWarned;

    [Input(Guid = "1c26862c-483f-4266-bad4-4521bef57cd6")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "1d5f3f1b-118e-4618-bc9d-653f566a9c9d")]
    public readonly InputSlot<bool> WeldPoints = new(true);
}

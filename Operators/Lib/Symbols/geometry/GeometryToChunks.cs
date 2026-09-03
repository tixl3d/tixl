using Lib.Utils;

namespace Lib.geometry;

/// <summary>
/// Compiles geometry parts into one mesh buffer with a chunk per part, plus the
/// pivot points and chunk indices that [DrawMeshChunksAtPoints] needs. Vertices are
/// stored relative to their part's pivot, so moving, rotating or scaling a pivot
/// point transforms the whole chunk on the GPU.
/// </summary>
[Guid("c7e3a9f1-5d28-4b64-8a0f-2e9c6b1d7f35")]
internal sealed class GeometryToChunks : Instance<GeometryToChunks>
{
    [Output(Guid = "3a8f1c5e-b2d7-4e90-9c46-d1f5a7e3b082")]
    public readonly Slot<MeshBuffers> Buffers = new();

    [Output(Guid = "e6b2d4a8-1f73-4c05-8e9b-a4c7f2d9e516")]
    public readonly Slot<StructuredList> Points = new();

    [Output(Guid = "9d4c7f2a-6e18-4b53-a7c0-f3e8b5d1a294")]
    public readonly Slot<BufferWithViews> GPoints = new();

    [Output(Guid = "52f8e1b7-c3a9-4d26-b0e4-7a1d9c6f3e58")]
    public readonly Slot<BufferWithViews> ChunkIndices = new();

    [Output(Guid = "b1e7c4d3-8a52-4f09-9d6e-c2f5a8b7e143")]
    public readonly Slot<int> ChunkCount = new();

    public GeometryToChunks()
    {
        Buffers.UpdateAction = Update;
        Points.UpdateAction = Update;
        GPoints.UpdateAction = Update;
        ChunkIndices.UpdateAction = Update;
        ChunkCount.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var geometry = Geometry.GetValue(context);
        if (geometry == null || geometry.FaceCount == 0)
        {
            Buffers.Value = null;
            Points.Value = null;
            GPoints.Value = null;
            ChunkIndices.Value = null;
            ChunkCount.Value = 0;
            return;
        }

        _compiler.Compile(geometry, relativeToPartPivots: true);
        var chunks = _compiler.Chunks;
        var parts = geometry.Parts;

        if (_pointList.NumElements != chunks.Length)
            _pointList.SetLength(chunks.Length);
        if (_chunkIndices.Length != chunks.Length)
            _chunkIndices = new int[chunks.Length];

        for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            // Part-less geometry is a single chunk anchored at the origin, so it renders where it was
            var pivot = parts.Length > 0 ? parts[chunkIndex].Pivot : Vector3.Zero;
            _pointList.TypedElements[chunkIndex] = new Point
                                                       {
                                                           Position = pivot,
                                                           F1 = 1,
                                                           F2 = parts.Length > 0 ? parts[chunkIndex].SeedIndex : 0,
                                                           Orientation = Quaternion.Identity,
                                                           Scale = Vector3.One,
                                                           Color = Vector4.One,
                                                       };
            _chunkIndices[chunkIndex] = chunkIndex;
        }

        ResourceManager.SetupBufferWithViews(_compiler.Vertices, ref _vertexBuffer);
        ResourceManager.SetupBufferWithViews(_compiler.Triangles, ref _indexBuffer);
        ResourceManager.SetupBufferWithViews(chunks, ref _chunkDefsBuffer);
        ResourceManager.SetupBufferWithViews(_pointList.TypedElements, ref _pointsBuffer);
        ResourceManager.SetupBufferWithViews(_chunkIndices, ref _chunkIndicesBuffer);

        _meshBuffers.VertexBuffer = _vertexBuffer;
        _meshBuffers.IndicesBuffer = _indexBuffer;
        _meshBuffers.ChunkDefsBuffer = _chunkDefsBuffer;

        Buffers.Value = _meshBuffers;
        Points.Value = _pointList;
        GPoints.Value = _pointsBuffer;
        ChunkIndices.Value = _chunkIndicesBuffer;
        ChunkCount.Value = chunks.Length;
    }

    private readonly GeometryMeshCompiler _compiler = new();
    private readonly StructuredList<Point> _pointList = new(1);
    private int[] _chunkIndices = [];
    private BufferWithViews _vertexBuffer;
    private BufferWithViews _indexBuffer;
    private BufferWithViews _chunkDefsBuffer;
    private BufferWithViews _pointsBuffer;
    private BufferWithViews _chunkIndicesBuffer;
    private readonly MeshBuffers _meshBuffers = new();

    [Input(Guid = "7f2c9e4b-d631-4a85-b9e7-0c5d8a3f1b62")]
    public readonly InputSlot<MeshGeometry> Geometry = new();
}

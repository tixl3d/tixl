using Lib.Utils;

namespace Lib.geometry;

[Guid("4fea8c0e-cb7f-41fe-a410-df1d3886bf98")]
internal sealed class GeometryToMesh : Instance<GeometryToMesh>
{
    [Output(Guid = "4bf00eb5-a39c-4567-a81e-46c2276d1418")]
    public readonly Slot<MeshBuffers> Buffers = new();

    public GeometryToMesh()
    {
        Buffers.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var geometry = Geometry.GetValue(context);
        if (geometry == null || geometry.FaceCount == 0)
        {
            Buffers.Value = null;
            _compiledGeometry = null;
            return;
        }

        // Compiling the mesh and re-uploading its buffers costs milliseconds on large geometry, and
        // animated sources re-evaluate this slot every frame with unchanged content. The geometry's
        // version only moves when a producer actually rebuilt it, so that is the cache key.
        if (ReferenceEquals(geometry, _compiledGeometry) && geometry.Version == _compiledVersion)
            return;

        _compiledGeometry = geometry;
        _compiledVersion = geometry.Version;

        _compiler.Compile(geometry, relativeToPartPivots: false);

        ResourceManager.SetupBufferWithViews(_compiler.Vertices, ref _vertexBufferWithViews);
        ResourceManager.SetupBufferWithViews(_compiler.Triangles, ref _indexBufferWithViews);
        _data.VertexBuffer = _vertexBufferWithViews;
        _data.IndicesBuffer = _indexBufferWithViews;
        Buffers.Value = _data;
    }

    private readonly GeometryMeshCompiler _compiler = new();
    private BufferWithViews _vertexBufferWithViews;
    private BufferWithViews _indexBufferWithViews;
    private readonly MeshBuffers _data = new();
    private MeshGeometry _compiledGeometry;
    private int _compiledVersion;

    [Input(Guid = "57469ea6-f0c5-4e8d-a53c-40e8ac495bc3")]
    public readonly InputSlot<MeshGeometry> Geometry = new();
}

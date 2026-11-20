using Lib.Utils;

namespace Lib.geometry.convert;

/// <summary>
/// Compiles the polygons of a Geometry into GPU mesh buffers; curve primitives are ignored. During the move to the
/// unified Geometry type the original input keeps taking MeshGeometry (as Mesh), so existing graphs stay connected;
/// Geometry wins when both are connected.
/// </summary>
[Guid("4fea8c0e-cb7f-41fe-a410-df1d3886bf98")]
internal sealed class GeometryToMesh : Instance<GeometryToMesh>, IStatusProvider
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
        var mesh = Mesh.GetValue(context);
        _statusMessage = string.Empty;
        if (geometry != null)
        {
            _compiler.Compile(geometry, relativeToPartPivots: false);
        }
        else if (mesh != null)
        {
            _compiler.Compile(mesh, relativeToPartPivots: false);
        }
        else
        {
            Buffers.Value = null;
            return;
        }

        if (_compiler.Triangles.Length == 0)
        {
            if (geometry != null && geometry.PrimitiveCount > 0)
                _statusMessage = "The geometry has no polygons, only curves. Fill or extrude them with [ExtrudeCurves] first.";

            Buffers.Value = null;
            return;
        }

        ResourceManager.SetupBufferWithViews(_compiler.Vertices, ref _vertexBufferWithViews);
        ResourceManager.SetupBufferWithViews(_compiler.Triangles, ref _indexBufferWithViews);
        _data.VertexBuffer = _vertexBufferWithViews;
        _data.IndicesBuffer = _indexBufferWithViews;
        Buffers.Value = _data;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private string _statusMessage = string.Empty;
    private readonly GeometryMeshCompiler _compiler = new();
    private BufferWithViews _vertexBufferWithViews;
    private BufferWithViews _indexBufferWithViews;
    private readonly MeshBuffers _data = new();

    [Input(Guid = "8919e38f-63ea-4ca1-b45f-623464b93184")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "57469ea6-f0c5-4e8d-a53c-40e8ac495bc3")]
    public readonly InputSlot<MeshGeometry> Mesh = new();
}

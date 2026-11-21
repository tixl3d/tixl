using Lib.Utils;
using LibTessDotNet;
using T3.Core.Rendering;

namespace Lib.geometry.convert;

/// <summary>
/// Compiles the polygons of a Geometry into GPU mesh buffers. With FillCurves, closed curve primitives are filled flat
/// (like [FillCurves]) and drawn too, so text and shapes render without an extrusion. Parts that already have polygons
/// (extruded text that kept its curves) aren't filled again; open curves are ignored.
/// </summary>
[Guid("4fea8c0e-cb7f-41fe-a410-df1d3886bf98")]
[ExportDependencies("LibTessDotNet.dll")]
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
        _statusMessage = string.Empty;
        if (geometry == null)
        {
            Buffers.Value = null;
            return;
        }

        _compiler.Compile(geometry, relativeToPartPivots: false);
        var vertices = _compiler.Vertices;
        var triangles = _compiler.Triangles;

        if (FillCurves.GetValue(context) && HasClosedCurves(geometry))
        {
            _fillBuilder.FillFlat(geometry, FillTolerance, weight: 0, WindingRule.NonZero, keepCurves: false, _filled, skipPartsWithPolygons: true);
            _fillCompiler.Compile(_filled, relativeToPartPivots: false);
            Append(_compiler, _fillCompiler);
            vertices = _mergedVertices;
            triangles = _mergedTriangles;
        }

        if (triangles.Length == 0)
        {
            if (geometry.PrimitiveCount > 0)
                _statusMessage = "The geometry has no polygons and no closed curves to fill. Close the curves, or extrude them with [ExtrudeCurves].";

            Buffers.Value = null;
            return;
        }

        ResourceManager.SetupBufferWithViews(vertices, ref _vertexBufferWithViews);
        ResourceManager.SetupBufferWithViews(triangles, ref _indexBufferWithViews);
        _data.VertexBuffer = _vertexBufferWithViews;
        _data.IndicesBuffer = _indexBufferWithViews;
        Buffers.Value = _data;
    }

    private static bool HasClosedCurves(Geometry geometry)
    {
        for (var primitive = 0; primitive < geometry.PrimitiveCount; primitive++)
        {
            if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Curve && geometry.PrimitiveClosed[primitive])
                return true;
        }

        return false;
    }

    /** The polygons' mesh followed by the filled curves, with the fill's indices shifted behind the polygons' vertices. */
    private void Append(GeometryMeshCompiler polygons, GeometryMeshCompiler fill)
    {
        var vertexCount = polygons.Vertices.Length + fill.Vertices.Length;
        var triangleCount = polygons.Triangles.Length + fill.Triangles.Length;
        if (_mergedVertices.Length != vertexCount)
            _mergedVertices = new PbrVertex[vertexCount];

        if (_mergedTriangles.Length != triangleCount)
            _mergedTriangles = new Int3[triangleCount];

        Array.Copy(polygons.Vertices, _mergedVertices, polygons.Vertices.Length);
        Array.Copy(fill.Vertices, 0, _mergedVertices, polygons.Vertices.Length, fill.Vertices.Length);
        Array.Copy(polygons.Triangles, _mergedTriangles, polygons.Triangles.Length);
        var offset = polygons.Vertices.Length;
        for (var index = 0; index < fill.Triangles.Length; index++)
        {
            var triangle = fill.Triangles[index];
            _mergedTriangles[polygons.Triangles.Length + index] = new Int3(triangle.X + offset, triangle.Y + offset, triangle.Z + offset);
        }
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private string _statusMessage = string.Empty;
    private readonly GeometryMeshCompiler _compiler = new();

    /** Flattening tolerance of the built-in fill; [FillCurves] exposes it for finer control. */
    private const float FillTolerance = 0.002f;

    private readonly CurveSolidBuilder _fillBuilder = new();
    private readonly GeometryMeshCompiler _fillCompiler = new();
    private readonly Geometry _filled = new();
    private PbrVertex[] _mergedVertices = [];
    private Int3[] _mergedTriangles = [];
    private BufferWithViews _vertexBufferWithViews;
    private BufferWithViews _indexBufferWithViews;
    private readonly MeshBuffers _data = new();

    [Input(Guid = "57469ea6-f0c5-4e8d-a53c-40e8ac495bc3")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "94d5cf91-080f-44f4-9c8c-80a81ebd88f6")]
    public readonly InputSlot<bool> FillCurves = new();
}

using Lib.Utils;
using LibTessDotNet;
using T3.Core.Rendering;

namespace Lib.geometry.convert;

/// <summary>
/// Compiles the polygons of a Geometry into GPU mesh buffers. With FillCurves, closed curve primitives are filled flat
/// (like [FillCurves]) and drawn too, so text and shapes render without an extrusion. Parts that already have polygons
/// (extruded text that kept its curves) aren't filled again; open curves are ignored. Optionally drops the duplicate
/// vertices that one-vertex-per-corner compilation produces.
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
        _duplicateLookup = new Dictionary<int, int>(_duplicateComparer);
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

        if (MergeDuplicateVertices.GetValue(context) && WeldDuplicateVertices(vertices, triangles))
        {
            vertices = _weldedVertices;
            triangles = _weldedTriangles;
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

    /// <summary>
    /// Drops vertices that repeat an earlier one and remaps the triangles to the survivors, e.g. the corners [FillCurves]
    /// creates at every face. Vertices count as duplicates when position, normal, UVs, color and selection agree; the
    /// tangent basis of the first is kept, since it is derived from those values.
    /// </summary>
    /// <returns>False when every vertex is unique, in which case the welded arrays are left as they were.</returns>
    private bool WeldDuplicateVertices(PbrVertex[] vertices, Int3[] triangles)
    {
        _duplicateComparer.Vertices = vertices;
        _duplicateLookup.Clear();
        EnsureLength(ref _vertexRemap, vertices.Length);
        EnsureLength(ref _isFirstVertex, vertices.Length);
        Array.Clear(_isFirstVertex, 0, vertices.Length);

        var keptCount = 0;
        for (var vertex = 0; vertex < vertices.Length; vertex++)
        {
            if (_duplicateLookup.TryGetValue(vertex, out var first))
            {
                _vertexRemap[vertex] = _vertexRemap[first];
                continue;
            }

            _duplicateLookup[vertex] = vertex;
            _isFirstVertex[vertex] = true;
            _vertexRemap[vertex] = keptCount++;
        }

        if (keptCount == vertices.Length)
            return false;

        if (_weldedVertices.Length != keptCount)
            _weldedVertices = new PbrVertex[keptCount];

        if (_weldedTriangles.Length != triangles.Length)
            _weldedTriangles = new Int3[triangles.Length];

        for (var vertex = 0; vertex < vertices.Length; vertex++)
        {
            if (_isFirstVertex[vertex])
                _weldedVertices[_vertexRemap[vertex]] = vertices[vertex];
        }

        for (var triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
        {
            var triangle = triangles[triangleIndex];
            _weldedTriangles[triangleIndex] = new Int3(_vertexRemap[triangle.X], _vertexRemap[triangle.Y], _vertexRemap[triangle.Z]);
        }

        return true;
    }

    /** Vertex indices of equal values resolve to the same key, so the lookup is a duplicate set of the first occurrences. */
    private sealed class DuplicateComparer : IEqualityComparer<int>
    {
        public PbrVertex[] Vertices = [];

        public bool Equals(int a, int b)
        {
            var first = Vertices[a];
            var second = Vertices[b];
            return first.Position == second.Position
                   && first.Normal == second.Normal
                   && first.Texcoord == second.Texcoord
                   && first.Texcoord2 == second.Texcoord2
                   && first.ColorRgb == second.ColorRgb
                   && first.Selection == second.Selection;
        }

        public int GetHashCode(int index)
        {
            var vertex = Vertices[index];
            var hash = new HashCode();
            hash.Add(vertex.Position);
            hash.Add(vertex.Normal);
            hash.Add(vertex.Texcoord);
            hash.Add(vertex.Texcoord2);
            hash.Add(vertex.ColorRgb);
            hash.Add(vertex.Selection);
            return hash.ToHashCode();
        }
    }

    private static void EnsureLength<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            array = new T[length];
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

    /** Duplicates resolve through the values of the vertices being welded. */
    private readonly DuplicateComparer _duplicateComparer = new();
    private readonly Dictionary<int, int> _duplicateLookup;
    private int[] _vertexRemap = [];
    private bool[] _isFirstVertex = [];
    private PbrVertex[] _weldedVertices = [];
    private Int3[] _weldedTriangles = [];

    [Input(Guid = "57469ea6-f0c5-4e8d-a53c-40e8ac495bc3")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "94d5cf91-080f-44f4-9c8c-80a81ebd88f6")]
    public readonly InputSlot<bool> FillCurves = new();

    [Input(Guid = "afa480eb-6d84-44e1-ae1c-28ac32d856f3")]
    public readonly InputSlot<bool> MergeDuplicateVertices = new();
}

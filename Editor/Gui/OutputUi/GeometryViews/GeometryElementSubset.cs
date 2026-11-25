#nullable enable
using T3.Core.DataTypes;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Builds a small geometry from some elements of one domain, for the preview to highlight them (see
/// <see cref="T3.Core.Rendering.PreviewOverlayState"/>): points as loose points, vertices as loose points pulled a
/// little towards their primitive (so shared points can be told apart), edges as two-point curves, primitives as copies
/// (curves with their handles) and parts as their primitives plus the pivot as a loose point. A new instance is built
/// only when the elements or the geometry change, so the preview can keep using the previous one meanwhile.
/// </summary>
internal sealed class GeometryElementSubset
{
    public Geometry? Result { get; private set; }

    /// <summary>The rows <paramref name="isIncluded"/> accepts, rebuilt when <paramref name="contentVersion"/> or the geometry changed.</summary>
    public Geometry? Update(Geometry geometry, UnifiedGeometryTableModel.Domains domain, int contentVersion, Func<int, bool> isIncluded)
    {
        if (ReferenceEquals(geometry, _geometry) && geometry.Version == _geometryVersion && domain == _domain && contentVersion == _contentVersion)
            return Result;

        _geometry = geometry;
        _geometryVersion = geometry.Version;
        _domain = domain;
        _contentVersion = contentVersion;
        Build(geometry, domain, isIncluded);
        return Result;
    }

    private void Build(Geometry geometry, UnifiedGeometryTableModel.Domains domain, Func<int, bool> isIncluded)
    {
        _positions.Clear();
        _offsets.Clear();
        _vertexPoints.Clear();
        _kinds.Clear();
        _closed.Clear();
        _handlesIn.Clear();
        _handlesOut.Clear();
        _hasHandles = false;
        geometry.Attributes.TryGet<Vector3>(GeometryAttributeNames.HandleIn, AttributeDomain.Point, out var handlesIn);
        geometry.Attributes.TryGet<Vector3>(GeometryAttributeNames.HandleOut, AttributeDomain.Point, out var handlesOut);

        var rowCount = UnifiedGeometryTableModel.GetRowCount(geometry, domain);
        for (var row = 0; row < rowCount; row++)
        {
            if (!isIncluded(row))
                continue;

            switch (domain)
            {
                case UnifiedGeometryTableModel.Domains.Points:
                    AddLoosePoint(geometry.Positions[row]);
                    break;
                case UnifiedGeometryTableModel.Domains.Vertices:
                    var primitive = FindPrimitive(geometry, row);
                    var position = geometry.Positions[geometry.VertexPoints[row]];
                    AddLoosePoint(Vector3.Lerp(position, UnifiedGeometryTableModel.GetPrimitiveCenter(geometry, primitive), VertexInset));
                    break;
                case UnifiedGeometryTableModel.Domains.Edges:
                    var edge = geometry.Edges.Edges[row];
                    AddPrimitive(PrimitiveKinds.Curve, false, geometry.Positions[edge.PointA], geometry.Positions[edge.PointB]);
                    break;
                case UnifiedGeometryTableModel.Domains.Primitives:
                    CopyPrimitive(geometry, row, handlesIn, handlesOut);
                    break;
                case UnifiedGeometryTableModel.Domains.Parts:
                    var part = geometry.Parts[row];
                    for (var p = part.PrimitiveStart; p < Math.Min(part.PrimitiveStart + part.PrimitiveCount, geometry.PrimitiveCount); p++)
                        CopyPrimitive(geometry, p, handlesIn, handlesOut);

                    AddLoosePoint(part.Pivot);
                    break;
            }
        }

        var result = new Geometry();
        result.Resize(_positions.Count, _kinds.Count, _vertexPoints.Count);
        _positions.CopyTo(result.Positions);
        _vertexPoints.CopyTo(result.VertexPoints);
        for (var index = 0; index < _kinds.Count; index++)
        {
            result.PrimitiveVertexOffsets[index] = _offsets[index];
            result.PrimitiveKind[index] = _kinds[index];
            result.PrimitiveClosed[index] = _closed[index];
        }

        result.PrimitiveVertexOffsets[_kinds.Count] = _vertexPoints.Count;
        if (_hasHandles)
        {
            _handlesIn.CopyTo(result.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.HandleIn, AttributeDomain.Point, _positions.Count).Values);
            _handlesOut.CopyTo(result.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.HandleOut, AttributeDomain.Point, _positions.Count).Values);
        }

        result.InvalidateTopologyCaches();
        Result = _positions.Count > 0 ? result : null;
    }

    private void AddLoosePoint(Vector3 position)
    {
        _positions.Add(position);
        _handlesIn.Add(Vector3.Zero);
        _handlesOut.Add(Vector3.Zero);
    }

    private void AddPrimitive(PrimitiveKinds kind, bool closed, Vector3 a, Vector3 b)
    {
        _offsets.Add(_vertexPoints.Count);
        _kinds.Add(kind);
        _closed.Add(closed);
        _vertexPoints.Add(_positions.Count);
        AddLoosePoint(a);
        _vertexPoints.Add(_positions.Count);
        AddLoosePoint(b);
    }

    /** A copy of the primitive with its own points; curves keep their handles. */
    private void CopyPrimitive(Geometry geometry, int primitive, GeometryAttribute<Vector3>? handlesIn, GeometryAttribute<Vector3>? handlesOut)
    {
        _offsets.Add(_vertexPoints.Count);
        _kinds.Add(geometry.PrimitiveKind[primitive]);
        _closed.Add(geometry.PrimitiveClosed[primitive]);
        for (var vertex = geometry.PrimitiveVertexOffsets[primitive]; vertex < geometry.PrimitiveVertexOffsets[primitive + 1]; vertex++)
        {
            var point = geometry.VertexPoints[vertex];
            _vertexPoints.Add(_positions.Count);
            _positions.Add(geometry.Positions[point]);
            _handlesIn.Add(handlesIn != null ? handlesIn.Values[point] : Vector3.Zero);
            _handlesOut.Add(handlesOut != null ? handlesOut.Values[point] : Vector3.Zero);
            _hasHandles |= handlesIn != null || handlesOut != null;
        }
    }

    private static int FindPrimitive(Geometry geometry, int vertex)
    {
        var offsets = geometry.PrimitiveVertexOffsets;
        int low = 0, high = geometry.PrimitiveCount - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (offsets[middle] <= vertex)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    /** How far a vertex moves from its point towards the primitive's center, so shared points can be told apart. */
    private const float VertexInset = 0.2f;

    private readonly List<Vector3> _positions = [];
    private readonly List<int> _offsets = [];
    private readonly List<int> _vertexPoints = [];
    private readonly List<PrimitiveKinds> _kinds = [];
    private readonly List<bool> _closed = [];
    private readonly List<Vector3> _handlesIn = [];
    private readonly List<Vector3> _handlesOut = [];
    private bool _hasHandles;
    private Geometry? _geometry;
    private int _geometryVersion;
    private UnifiedGeometryTableModel.Domains _domain;
    private int _contentVersion;
}

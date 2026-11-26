#nullable enable
namespace Lib.geometry.convert;

/// <summary>
/// Cuts the polygons of a geometry with one or several parallel planes and returns the intersection lines as straight-segment
/// curves, one part per slice. Polygons are fan-triangulated like the mesh compiler does; crossings at the same position are
/// shared, so split vertices and vertices lying exactly on a plane still join into one curve. Each segment runs along
/// cross(plane normal, face normal), which makes the outlines of an outward-facing surface counter-clockwise and its holes
/// clockwise when seen from the normal side. Each part carries its slice number as an int SliceIndex attribute. Curves of
/// the input are ignored.
/// </summary>
[Guid("1e9be31f-406a-4f2d-97e9-7c647f8e9e16")]
internal sealed class SliceGeometryToCurves : Instance<SliceGeometryToCurves>, IStatusProvider
{
    [Output(Guid = "511351b4-55cc-4201-9565-078ccde9af28")]
    public readonly Slot<Geometry?> Result = new();

    public SliceGeometryToCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var center = Center.GetValue(context);
        var normal = Normal.GetValue(context);
        var sliceCount = Math.Clamp(SliceCount.GetValue(context), 1, MaxSliceCount);
        var spacing = Spacing.GetValue(context);

        _statusLevel = IStatusProvider.StatusLevel.Success;
        _statusMessage = string.Empty;
        ClearOutputLists();

        if (source == null)
        {
            WriteOutput(0, center, normal, spacing);
            return;
        }

        if (normal.LengthSquared() < 1e-12f)
        {
            _statusLevel = IStatusProvider.StatusLevel.Warning;
            _statusMessage = "The plane normal is zero.";
            WriteOutput(0, center, normal, spacing);
            return;
        }

        normal = Vector3.Normalize(normal);
        EnsureLength(ref _pointDistances, source.PointCount);

        var closedCount = 0;
        for (var sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
        {
            var origin = center + normal * GetSliceOffset(sliceIndex, sliceCount, spacing);
            CollectSegments(source, origin, normal);
            closedCount += ChainSegments();
            _slicePrimitiveEnds.Add(_outputVertexOffsets.Count);
        }

        WriteOutput(sliceCount, center, normal, spacing);

        var curveCount = _outputVertexOffsets.Count;
        if (curveCount == 0)
        {
            _statusMessage = source.Polygons.GetEnumerator().MoveNext()
                                 ? "No plane intersects the polygons."
                                 : "The input has no polygons to slice.";
            return;
        }

        _statusMessage = $"{curveCount:N0} curves, {closedCount:N0} closed.";
    }

    private static float GetSliceOffset(int sliceIndex, int sliceCount, float spacing) => (sliceIndex - (sliceCount - 1) * 0.5f) * spacing;

    /// <summary>Fills the segment and node lists with the intersections of the triangulated polygons and one plane.</summary>
    private void CollectSegments(Geometry source, Vector3 origin, Vector3 normal)
    {
        var positions = source.Positions;
        for (var pointIndex = 0; pointIndex < positions.Length; pointIndex++)
            _pointDistances[pointIndex] = Vector3.Dot(positions[pointIndex] - origin, normal);

        _nodeIndexByPosition.Clear();
        _nodePositions.Clear();
        _segmentStartNodes.Clear();
        _segmentEndNodes.Clear();

        foreach (var polygon in source.Polygons)
        {
            var vertexCount = polygon.VertexCount;
            if (vertexCount < 3)
                continue;

            var firstPoint = polygon.GetPointIndex(0);
            for (var corner = 1; corner < vertexCount - 1; corner++)
                AddTriangleSegment(positions, firstPoint, polygon.GetPointIndex(corner), polygon.GetPointIndex(corner + 1), normal);
        }
    }

    private void AddTriangleSegment(Vector3[] positions, int pointA, int pointB, int pointC, Vector3 normal)
    {
        // A distance of exactly 0 counts as above, so every crossing edge has one endpoint strictly below the plane.
        var aboveA = _pointDistances[pointA] >= 0;
        var aboveB = _pointDistances[pointB] >= 0;
        var aboveC = _pointDistances[pointC] >= 0;
        if (aboveA == aboveB && aboveB == aboveC)
            return;

        int lonePoint, otherPoint1, otherPoint2;
        if (aboveA != aboveB && aboveA != aboveC)
        {
            (lonePoint, otherPoint1, otherPoint2) = (pointA, pointB, pointC);
        }
        else if (aboveB != aboveA)
        {
            (lonePoint, otherPoint1, otherPoint2) = (pointB, pointA, pointC);
        }
        else
        {
            (lonePoint, otherPoint1, otherPoint2) = (pointC, pointA, pointB);
        }

        var crossing1 = GetCrossing(positions, lonePoint, otherPoint1);
        var crossing2 = GetCrossing(positions, lonePoint, otherPoint2);
        var startNode = GetOrAddNode(crossing1);
        var endNode = GetOrAddNode(crossing2);
        if (startNode == endNode)
            return;

        var faceNormal = Vector3.Cross(positions[pointB] - positions[pointA], positions[pointC] - positions[pointA]);
        if (Vector3.Dot(crossing2 - crossing1, Vector3.Cross(normal, faceNormal)) < 0)
            (startNode, endNode) = (endNode, startNode);

        _segmentStartNodes.Add(startNode);
        _segmentEndNodes.Add(endNode);
    }

    /// <summary>
    /// Interpolates from the endpoint below the plane, so duplicated points produce bit-identical crossings and share a node.
    /// </summary>
    private Vector3 GetCrossing(Vector3[] positions, int pointIndex1, int pointIndex2)
    {
        var below = pointIndex1;
        var above = pointIndex2;
        if (_pointDistances[below] >= 0)
            (below, above) = (above, below);

        var belowDistance = _pointDistances[below];
        var aboveDistance = _pointDistances[above];
        if (aboveDistance == 0)
            return positions[above];

        var t = -belowDistance / (aboveDistance - belowDistance);
        return positions[below] + (positions[above] - positions[below]) * t;
    }

    private int GetOrAddNode(Vector3 position)
    {
        if (_nodeIndexByPosition.TryGetValue(position, out var node))
            return node;

        node = _nodePositions.Count;
        _nodePositions.Add(position);
        _nodeIndexByPosition.Add(position, node);
        return node;
    }

    /// <summary>Joins the collected segments into curves and appends them to the output lists.</summary>
    /// <returns>How many of the new curves are closed.</returns>
    private int ChainSegments()
    {
        var nodeCount = _nodePositions.Count;
        var segmentCount = _segmentStartNodes.Count;
        if (segmentCount == 0)
            return 0;

        BuildNodeIncidence(nodeCount, segmentCount);

        var closedCount = 0;

        // Starting at nodes with an odd number of segments first keeps open chains in one piece; the rest are loops.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var node = 0; node < nodeCount; node++)
            {
                var degree = _incidenceOffsets[node + 1] - _incidenceOffsets[node];
                if (pass == 0 && degree % 2 == 0)
                    continue;

                while (TryTakeUnusedSegment(node, out var segment))
                {
                    if (TraceChain(node, segment))
                        closedCount++;
                }
            }
        }

        return closedCount;
    }

    private void BuildNodeIncidence(int nodeCount, int segmentCount)
    {
        EnsureLength(ref _incidenceOffsets, nodeCount + 1);
        EnsureLength(ref _incidenceFill, nodeCount);
        EnsureLength(ref _incidentSegments, segmentCount * 2);
        EnsureLength(ref _segmentUsed, segmentCount);
        Array.Clear(_incidenceOffsets, 0, nodeCount + 1);
        Array.Clear(_incidenceFill, 0, nodeCount);
        Array.Clear(_segmentUsed, 0, segmentCount);

        for (var segment = 0; segment < segmentCount; segment++)
        {
            _incidenceOffsets[_segmentStartNodes[segment] + 1]++;
            _incidenceOffsets[_segmentEndNodes[segment] + 1]++;
        }

        for (var node = 0; node < nodeCount; node++)
            _incidenceOffsets[node + 1] += _incidenceOffsets[node];

        for (var segment = 0; segment < segmentCount; segment++)
        {
            var start = _segmentStartNodes[segment];
            var end = _segmentEndNodes[segment];
            _incidentSegments[_incidenceOffsets[start] + _incidenceFill[start]++] = segment;
            _incidentSegments[_incidenceOffsets[end] + _incidenceFill[end]++] = segment;
        }
    }

    private bool TryTakeUnusedSegment(int node, out int segment)
    {
        for (var index = _incidenceOffsets[node]; index < _incidenceOffsets[node + 1]; index++)
        {
            segment = _incidentSegments[index];
            if (_segmentUsed[segment])
                continue;

            _segmentUsed[segment] = true;
            return true;
        }

        segment = -1;
        return false;
    }

    /// <summary>
    /// Walks from a node along unused segments until it returns to the start or runs out, then appends the chain as a
    /// curve, reversed if most of its segments point the other way.
    /// </summary>
    /// <returns>True if the chain is closed.</returns>
    private bool TraceChain(int startNode, int firstSegment)
    {
        var chainStart = _outputPositions.Count;
        _outputPositions.Add(_nodePositions[startNode]);

        var currentNode = startNode;
        var segment = firstSegment;
        var forwardScore = 0;
        var isClosed = false;
        while (true)
        {
            var isForward = _segmentStartNodes[segment] == currentNode;
            forwardScore += isForward ? 1 : -1;
            currentNode = isForward ? _segmentEndNodes[segment] : _segmentStartNodes[segment];
            if (currentNode == startNode)
            {
                isClosed = true;
                break;
            }

            _outputPositions.Add(_nodePositions[currentNode]);
            if (!TryTakeUnusedSegment(currentNode, out segment))
                break;
        }

        var chainLength = _outputPositions.Count - chainStart;
        if (isClosed && chainLength < 3)
        {
            // Two segments between the same pair of nodes enclose nothing; keep them as one open line.
            isClosed = false;
        }

        if (forwardScore < 0)
            _outputPositions.Reverse(chainStart, chainLength);

        _outputVertexOffsets.Add(chainStart);
        _outputClosed.Add(isClosed);
        return isClosed;
    }

    private void ClearOutputLists()
    {
        _outputPositions.Clear();
        _outputVertexOffsets.Clear();
        _outputClosed.Clear();
        _slicePrimitiveEnds.Clear();
    }

    private void WriteOutput(int sliceCount, Vector3 center, Vector3 normal, float spacing)
    {
        var pointCount = _outputPositions.Count;
        var primitiveCount = _outputVertexOffsets.Count;
        _output.Resize(pointCount, primitiveCount, pointCount);
        _outputPositions.CopyTo(_output.Positions);

        for (var point = 0; point < pointCount; point++)
            _output.VertexPoints[point] = point;

        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            _output.PrimitiveVertexOffsets[primitive] = _outputVertexOffsets[primitive];
            _output.PrimitiveKind[primitive] = PrimitiveKinds.Curve;
            _output.PrimitiveClosed[primitive] = _outputClosed[primitive];
        }

        _output.PrimitiveVertexOffsets[primitiveCount] = pointCount;

        if (_output.Parts.Length != sliceCount)
            _output.Parts = new PrimitivePart[sliceCount];

        _output.Attributes.Clear();
        var sliceIndices = _output.Attributes.GetOrCreate<int>(SliceIndexAttributeName, AttributeDomain.Part, sliceCount).Values;

        var primitiveStart = 0;
        for (var sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
        {
            sliceIndices[sliceIndex] = sliceIndex;
            var primitiveEnd = _slicePrimitiveEnds[sliceIndex];
            var pivot = center + normal * GetSliceOffset(sliceIndex, sliceCount, spacing);
            _output.Parts[sliceIndex] = new PrimitivePart(primitiveStart, primitiveEnd - primitiveStart, pivot, sliceIndex, sliceIndex);
            primitiveStart = primitiveEnd;
        }

        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private static void EnsureLength<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            array = new T[length];
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;

    public string GetStatusMessage() => _statusMessage;

    private const int MaxSliceCount = 10000;
    private const string SliceIndexAttributeName = "SliceIndex";

    private string _statusMessage = string.Empty;
    private IStatusProvider.StatusLevel _statusLevel = IStatusProvider.StatusLevel.Success;

    /** Per input point: its signed distance to the current plane. */
    private float[] _pointDistances = [];

    /** Per node plus one: start of its range in <see cref="_incidentSegments"/>. */
    private int[] _incidenceOffsets = [];

    /** Per node: how many of its segments are already written while building the incidence ranges. */
    private int[] _incidenceFill = [];

    /** The segments touching each node, in the ranges given by <see cref="_incidenceOffsets"/>. */
    private int[] _incidentSegments = [];

    /** Per segment: whether a chain already contains it. */
    private bool[] _segmentUsed = [];

    private readonly Dictionary<Vector3, int> _nodeIndexByPosition = [];
    private readonly List<Vector3> _nodePositions = [];
    private readonly List<int> _segmentStartNodes = [];
    private readonly List<int> _segmentEndNodes = [];

    private readonly List<Vector3> _outputPositions = [];
    private readonly List<int> _outputVertexOffsets = [];
    private readonly List<bool> _outputClosed = [];

    /** Per slice: the output primitive count after it, which bounds its part. */
    private readonly List<int> _slicePrimitiveEnds = [];

    private readonly Geometry _output = new();

    [Input(Guid = "2ac84842-b7db-410e-b5b8-a2b2412f6a3e")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "4e2ffa2a-2425-4c7b-94cb-78c4cd4305c6")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "b537a326-e52a-4e14-8a61-81a371f12ed5")]
    public readonly InputSlot<Vector3> Normal = new();

    [Input(Guid = "76f02e33-9a8c-4af8-a608-b0ff8112d3b6")]
    public readonly InputSlot<int> SliceCount = new();

    [Input(Guid = "6663a6c6-3cca-483a-a631-05fd0a551082")]
    public readonly InputSlot<float> Spacing = new();
}

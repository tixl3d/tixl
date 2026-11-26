#nullable enable
using Lib.Utils;

namespace Lib.geometry.generate;

/// <summary>
/// Extrudes the closed curves of a Geometry straight along Z into a solid: front and back caps stay N-gons and the
/// walls stay quads. In contrast to [ExtrudeCurves] it does not fill, resolve overlaps, bevel or triangulate, so the
/// outline of a cap remains a single polygon and the walls remain quads for further modelling. Nested contours become
/// holes by splicing each one into its parent at the closest pair of points, like a keyhole: the cap is one N-gon with
/// a zero-width slit, and the two bridge edges carry the IsBridge attribute and get no wall. Contours are oriented by
/// nesting (outlines counter-clockwise, holes clockwise); overlapping contours are not resolved. Only closed curves
/// form the outline; open curves and polygons are ignored.
/// </summary>
[Guid("29b77355-3eb3-4d56-a75d-6d129c2e684e")]
internal sealed class Extrude : Instance<Extrude>, IStatusProvider
{
    /// <summary>A closed outline and whether each edge of it is an artificial bridge rather than contour.</summary>
    private sealed class Contour
    {
        public readonly List<Vector3> Points = [];
        public readonly List<bool> IsBridgeEdge = [];

        public void Clear()
        {
            Points.Clear();
            IsBridgeEdge.Clear();
        }

        public void SetFrom(List<Vector3> loop)
        {
            Points.AddRange(loop);
            IsBridgeEdge.EnsureCapacity(loop.Count);
            for (var index = 0; index < loop.Count; index++)
                IsBridgeEdge.Add(false);
        }
    }

    [Output(Guid = "2cadce4a-3e9d-4764-9d12-f078fe8f46d1")]
    public readonly Slot<Geometry?> Result = new();

    public Extrude()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Curves.GetValue(context);
        var depth = MathF.Max(Depth.GetValue(context), 0);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-5f);
        var front = Front.GetValue(context);
        var back = Back.GetValue(context);
        var sides = Sides.GetValue(context);
        _statusMessage = string.Empty;

        if (source == null || source.PrimitiveCount == 0)
        {
            Result.Value = null;
            return;
        }

        Build(source, depth, tolerance, front, back, sides);
        if (_output.PrimitiveCount == 0)
        {
            _statusMessage = "No closed curves to extrude.";
            Result.Value = null;
            return;
        }

        Result.Value = _output;
    }

    private void Build(Geometry source, float depth, float tolerance, bool front, bool back, bool sides)
    {
        _positions.Clear();
        _corners.Clear();
        _faceOffsets.Clear();
        _faceOffsets.Add(0);
        _normals.Clear();
        _isSide.Clear();
        _parts.Clear();
        _partAttributes.Clear();
        _bridgeEdgePointPairs.Clear();

        var hasWalls = sides && depth > WallDepthEpsilon;
        var curveViews = source.Curves;
        var hasParts = source.Parts.Length > 0;
        var partCount = hasParts ? source.Parts.Length : 1;
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            var primitiveStart = hasParts ? source.Parts[partIndex].PrimitiveStart : 0;
            var primitiveEnd = hasParts ? primitiveStart + source.Parts[partIndex].PrimitiveCount : source.PrimitiveCount;
            if (!CollectLoops(source, curveViews, primitiveStart, primitiveEnd, tolerance))
                continue;

            OrientLoops();
            FindParents();
            var partStart = _faceOffsets.Count - 1;
            for (var loopIndex = 0; loopIndex < _loops.Count; loopIndex++)
            {
                if (_parents[loopIndex] >= 0)
                    continue;

                var contour = BuildContour(loopIndex);
                AddContour(contour, depth, front, back, hasWalls);
                ReturnContour(contour);
            }

            if (_faceOffsets.Count - 1 == partStart)
                continue;

            var pivot = hasParts ? source.Parts[partIndex].Pivot : Vector3.Zero;
            var id = hasParts ? source.Parts[partIndex].Id : 0;
            var seed = hasParts ? source.Parts[partIndex].SeedIndex : 0;
            _parts.Add(new PrimitivePart(partStart, _faceOffsets.Count - 1 - partStart, pivot, id, seed));
            _partAttributes.AddCopy(partIndex);
        }

        WriteOutput(source);
    }

    /// <summary>Flattens the part's closed curves into loops, dropping duplicate and closing points.</summary>
    /// <returns>False without a usable contour.</returns>
    private bool CollectLoops(Geometry source, Geometry.CurveEnumerable curveViews, int primitiveStart, int primitiveEnd, float tolerance)
    {
        _loops.Clear();
        for (var primitive = primitiveStart; primitive < primitiveEnd; primitive++)
        {
            if (source.PrimitiveKind[primitive] != PrimitiveKinds.Curve || !source.PrimitiveClosed[primitive])
                continue;

            _scratch.Clear();
            curveViews[primitive].Flatten(tolerance, _scratch);
            if (_scratch.Count < 3)
                continue;

            while (_loopPool.Count <= _loops.Count)
                _loopPool.Add([]);

            var loop = _loopPool[_loops.Count];
            loop.Clear();
            foreach (var point in _scratch)
            {
                if (loop.Count == 0 || Vector2.DistanceSquared(To2D(loop[^1]), To2D(point)) > DedupeDistanceSquared)
                    loop.Add(point);
            }

            if (loop.Count > 2 && Vector2.DistanceSquared(To2D(loop[0]), To2D(loop[^1])) < DedupeDistanceSquared)
                loop.RemoveAt(loop.Count - 1);

            if (loop.Count >= 3)
                _loops.Add(loop);
        }

        return _loops.Count > 0;
    }

    /// <summary>
    /// Makes outlines counter-clockwise and holes clockwise from the nesting depth, so caps and walls face outwards
    /// whatever winding the input uses. Contours that merely overlap stay as they come.
    /// </summary>
    private void OrientLoops()
    {
        for (var index = 0; index < _loops.Count; index++)
        {
            var probe = To2D(_loops[index][0]);
            var depth = 0;
            for (var other = 0; other < _loops.Count; other++)
            {
                if (other != index && Contains(_loops[other], probe))
                    depth++;
            }

            var isCounterClockwise = SignedArea(_loops[index]) > 0;
            if (isCounterClockwise != (depth % 2 == 0))
                _loops[index].Reverse();
        }
    }

    /// <summary>Immediate containing contour per loop; -1 for the outermost ones.</summary>
    private void FindParents()
    {
        if (_parents.Length < _loops.Count)
            _parents = new int[_loops.Count];

        for (var loopIndex = 0; loopIndex < _loops.Count; loopIndex++)
        {
            var probe = To2D(_loops[loopIndex][0]);
            var parent = -1;
            var smallestArea = float.MaxValue;
            for (var candidate = 0; candidate < _loops.Count; candidate++)
            {
                if (candidate == loopIndex || !Contains(_loops[candidate], probe))
                    continue;

                var area = MathF.Abs(SignedArea(_loops[candidate]));
                if (area >= smallestArea)
                    continue;

                smallestArea = area;
                parent = candidate;
            }

            _parents[loopIndex] = parent;
        }
    }

    private Contour BuildContour(int loopIndex)
    {
        var contour = RentContour();
        contour.SetFrom(_loops[loopIndex]);
        for (var child = 0; child < _loops.Count; child++)
        {
            if (_parents[child] != loopIndex)
                continue;

            var childContour = BuildContour(child);
            Splice(contour, childContour);
            ReturnContour(childContour);
        }

        return contour;
    }

    /// <summary>
    /// Splices a nested contour into its parent at the closest pair of points, so the hole becomes a zero-width slit
    /// of the parent outline. The two bridge edges are marked to keep them free of walls.
    /// </summary>
    private void Splice(Contour parent, Contour child)
    {
        FindClosestPair(parent.Points, child.Points, out var parentIndex, out var childIndex);

        _splicePoints.Clear();
        _spliceBridgeEdges.Clear();

        for (var index = 0; index <= parentIndex; index++)
            _splicePoints.Add(parent.Points[index]);
        for (var edge = 0; edge < parentIndex; edge++)
            _spliceBridgeEdges.Add(parent.IsBridgeEdge[edge]);

        _spliceBridgeEdges.Add(true);
        for (var step = 0; step < child.Points.Count; step++)
            _splicePoints.Add(child.Points[(childIndex + step) % child.Points.Count]);
        _splicePoints.Add(child.Points[childIndex]);

        for (var step = 0; step < child.Points.Count; step++)
            _spliceBridgeEdges.Add(child.IsBridgeEdge[(childIndex + step) % child.Points.Count]);
        _spliceBridgeEdges.Add(true);

        for (var index = parentIndex; index < parent.Points.Count; index++)
            _splicePoints.Add(parent.Points[index]);
        for (var edge = parentIndex; edge < parent.Points.Count; edge++)
            _spliceBridgeEdges.Add(parent.IsBridgeEdge[edge]);

        parent.Points.Clear();
        parent.Points.AddRange(_splicePoints);
        parent.IsBridgeEdge.Clear();
        parent.IsBridgeEdge.AddRange(_spliceBridgeEdges);
    }

    private static void FindClosestPair(List<Vector3> parent, List<Vector3> child, out int parentIndex, out int childIndex)
    {
        parentIndex = 0;
        childIndex = 0;
        var closest = float.MaxValue;
        for (var i = 0; i < parent.Count; i++)
        {
            for (var j = 0; j < child.Count; j++)
            {
                var distance = Vector3.DistanceSquared(parent[i], child[j]);
                if (distance >= closest)
                    continue;

                closest = distance;
                parentIndex = i;
                childIndex = j;
            }
        }
    }

    private Contour RentContour() => _contourPool.Count > 0 ? _contourPool.Pop() : new Contour();

    private void ReturnContour(Contour contour)
    {
        contour.Clear();
        _contourPool.Push(contour);
    }

    private void AddContour(Contour contour, float depth, bool front, bool back, bool hasWalls)
    {
        var count = contour.Points.Count;
        var addFrontRing = front || hasWalls;
        var addBackRing = depth > WallDepthEpsilon && (back || hasWalls);
        if (!addFrontRing && !addBackRing)
            return;

        // Bridge endpoints appear twice in a contour; both copies must reference one point so the slit stays closed
        if (addFrontRing)
        {
            _frontLookup.Clear();
            _frontIds.Clear();
            foreach (var position in contour.Points)
            {
                if (!_frontLookup.TryGetValue(position, out var point))
                {
                    point = _positions.Count;
                    _positions.Add(position);
                    _frontLookup[position] = point;
                }

                _frontIds.Add(point);
            }
        }

        if (addBackRing)
        {
            _backLookup.Clear();
            _backIds.Clear();
            var backOffset = new Vector3(0, 0, depth);
            foreach (var position in contour.Points)
            {
                var shifted = position - backOffset;
                if (!_backLookup.TryGetValue(shifted, out var point))
                {
                    point = _positions.Count;
                    _positions.Add(shifted);
                    _backLookup[shifted] = point;
                }

                _backIds.Add(point);
            }
        }

        // The cap follows the contour's winding, so its normal points away from the solid
        var frontNormal = SignedArea(contour.Points) > 0 ? Vector3.UnitZ : -Vector3.UnitZ;
        if (front)
            AddPolygon(_frontIds, reversed: false, frontNormal);

        if (back && addBackRing)
            AddPolygon(_backIds, reversed: true, -frontNormal);

        for (var edge = 0; edge < count; edge++)
        {
            if (!contour.IsBridgeEdge[edge])
                continue;

            var next = (edge + 1) % count;
            if (front)
                _bridgeEdgePointPairs.Add((_frontIds[edge], _frontIds[next]));
            if (back && addBackRing)
                _bridgeEdgePointPairs.Add((_backIds[edge], _backIds[next]));
        }

        if (!hasWalls)
            return;

        // The solid lies to the left of the contour direction and Z decreases towards the back ring,
        // so a -> d -> c -> b faces outwards for outlines and holes alike
        for (var edge = 0; edge < count; edge++)
        {
            if (contour.IsBridgeEdge[edge])
                continue;

            var next = (edge + 1) % count;
            var a = _frontIds[edge];
            var b = _frontIds[next];
            var d = _backIds[edge];
            var c = _backIds[next];
            var pa = _positions[a];
            var pb = _positions[b];
            var pd = _positions[d];
            var normal = Vector3.Cross(pd - pa, pb - pa);
            if (normal.LengthSquared() < 1e-14f)
                continue;

            normal = Vector3.Normalize(normal);
            AddCorner(a, normal);
            AddCorner(d, normal);
            AddCorner(c, normal);
            AddCorner(b, normal);
            _faceOffsets.Add(_corners.Count);
            _isSide.Add(1);
        }
    }

    private void AddPolygon(List<int> points, bool reversed, Vector3 normal)
    {
        for (var step = 0; step < points.Count; step++)
            AddCorner(points[reversed ? points.Count - 1 - step : step], normal);

        _faceOffsets.Add(_corners.Count);
        _isSide.Add(0);
    }

    private void AddCorner(int point, Vector3 normal)
    {
        _corners.Add(point);
        _normals.Add(normal);
    }

    private void WriteOutput(Geometry source)
    {
        var pointCount = _positions.Count;
        var primitiveCount = _faceOffsets.Count - 1;
        var vertexCount = _corners.Count;
        _output.Resize(pointCount, primitiveCount, vertexCount);
        _positions.CopyTo(_output.Positions);
        _corners.CopyTo(_output.VertexPoints);
        _faceOffsets.CopyTo(_output.PrimitiveVertexOffsets);
        _output.SetAllPrimitiveKinds(PrimitiveKinds.Polygon);
        _output.Parts = _parts.ToArray();
        _output.Attributes.Clear();

        if (vertexCount > 0)
        {
            var normals = _output.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Vertex, vertexCount);
            _normals.CopyTo(normals.Values);
        }

        if (primitiveCount > 0)
        {
            var isSide = _output.Attributes.GetOrCreate<float>(GeometryAttributeNames.IsSide, AttributeDomain.Primitive, primitiveCount);
            _isSide.CopyTo(isSide.Values);
        }

        if (_parts.Count > 0)
            _partAttributes.Apply(source.Attributes, _output.Attributes, AttributeDomain.Part);

        _output.InvalidateTopologyCaches();
        if (_bridgeEdgePointPairs.Count > 0)
            WriteBridgeEdges();
    }

    /// <summary>Marks the artificial bridge edges, so downstream ops can tell them from the contour.</summary>
    private void WriteBridgeEdges()
    {
        var edges = _output.Edges.Edges;
        if (edges.Length == 0)
            return;

        var isBridge = _output.Attributes.GetOrCreate<float>(GeometryAttributeNames.IsBridge, AttributeDomain.Edge, edges.Length);
        foreach (var (a, b) in _bridgeEdgePointPairs)
        {
            var low = Math.Min(a, b);
            var high = Math.Max(a, b);
            for (var edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                var edge = edges[edgeIndex];
                if (edge.PointA != low || edge.PointB != high)
                    continue;

                isBridge.Values[edgeIndex] = 1;
                break;
            }
        }
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private const float WallDepthEpsilon = 1e-6f;
    private const float DedupeDistanceSquared = 1e-12f;

    private readonly List<Vector3> _positions = [];
    private readonly List<int> _corners = [];
    private readonly List<int> _faceOffsets = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<float> _isSide = [];
    private readonly List<PrimitivePart> _parts = [];
    private readonly List<List<Vector3>> _loops = [];
    private readonly List<List<Vector3>> _loopPool = [];
    private readonly List<Vector3> _scratch = [];
    private readonly List<int> _frontIds = [];
    private readonly List<int> _backIds = [];
    private readonly Dictionary<Vector3, int> _frontLookup = [];
    private readonly Dictionary<Vector3, int> _backLookup = [];
    private readonly Stack<Contour> _contourPool = new();
    private readonly List<Vector3> _splicePoints = [];
    private readonly List<bool> _spliceBridgeEdges = [];
    private readonly List<(int A, int B)> _bridgeEdgePointPairs = [];
    private int[] _parents = [];
    private readonly GeometryAttributeStencil _partAttributes = new();
    private readonly Geometry _output = new();
    private string _statusMessage = string.Empty;

    private static Vector2 To2D(Vector3 point) => new(point.X, point.Y);

    private static float SignedArea(List<Vector3> loop)
    {
        var area = 0f;
        for (var index = 0; index < loop.Count; index++)
        {
            var a = loop[index];
            var b = loop[(index + 1) % loop.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5f;
    }

    private static bool Contains(List<Vector3> loop, Vector2 point)
    {
        var inside = false;
        for (int index = 0, previous = loop.Count - 1; index < loop.Count; previous = index++)
        {
            var a = loop[index];
            var b = loop[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    [Input(Guid = "7c148d6e-84b6-43f5-8a09-9f88d6183b48")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "6256a7bd-bd12-4d43-a5f2-21d0c674e225")]
    public readonly InputSlot<float> Depth = new();

    [Input(Guid = "5722145b-471e-4fa5-95c4-0308b155a096")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "d827b3e9-301b-4024-9ee4-7ea3f7a1a69f")]
    public readonly InputSlot<bool> Front = new();

    [Input(Guid = "5be214cd-c690-406d-b5bc-32dd1e36d56f")]
    public readonly InputSlot<bool> Back = new();

    [Input(Guid = "f3d1bfbd-349b-494a-b5a3-4ce8583c45fb")]
    public readonly InputSlot<bool> Sides = new();
}

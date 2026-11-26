#nullable enable
using Lib.Utils;

namespace Lib.geometry.generate;

/// <summary>
/// Extrudes the closed curves of a Geometry straight along Z into a solid: front and back caps stay N-gons, one polygon
/// per contour, and the walls stay quads. In contrast to [ExtrudeCurves] it does not fill, resolve overlaps, bevel or
/// triangulate, so the outline of a cap remains a single polygon and the walls remain quads for further modelling.
/// Contours are oriented by nesting (outlines counter-clockwise, holes clockwise); overlapping contours are not
/// resolved. Only closed curves form the outline; open curves and polygons are ignored.
/// </summary>
[Guid("29b77355-3eb3-4d56-a75d-6d129c2e684e")]
internal sealed class Extrude : Instance<Extrude>, IStatusProvider
{
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
            var partStart = _faceOffsets.Count - 1;
            foreach (var loop in _loops)
                AddLoop(loop, depth, front, back, hasWalls);

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

    private void AddLoop(List<Vector3> loop, float depth, bool front, bool back, bool hasWalls)
    {
        var count = loop.Count;
        var addFrontRing = front || hasWalls;
        var addBackRing = depth > WallDepthEpsilon && (back || hasWalls);
        if (!addFrontRing && !addBackRing)
            return;

        var frontStart = -1;
        if (addFrontRing)
        {
            frontStart = _positions.Count;
            _positions.AddRange(loop);
        }

        var backStart = -1;
        if (addBackRing)
        {
            backStart = _positions.Count;
            for (var index = 0; index < count; index++)
                _positions.Add(loop[index] - new Vector3(0, 0, depth));
        }

        // The cap follows the contour's winding, so its normal points away from the solid
        var frontNormal = SignedArea(loop) > 0 ? Vector3.UnitZ : -Vector3.UnitZ;
        if (front)
            AddPolygon(frontStart, count, reversed: false, frontNormal);

        if (back && addBackRing)
            AddPolygon(backStart, count, reversed: true, -frontNormal);

        if (!hasWalls)
            return;

        // The solid lies to the left of the contour direction and Z decreases towards the back ring,
        // so a -> d -> c -> b faces outwards for outlines and holes alike
        for (var index = 0; index < count; index++)
        {
            var next = (index + 1) % count;
            var a = frontStart + index;
            var b = frontStart + next;
            var d = backStart + index;
            var c = backStart + next;
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

    private void AddPolygon(int firstPoint, int count, bool reversed, Vector3 normal)
    {
        for (var step = 0; step < count; step++)
            AddCorner(firstPoint + (reversed ? count - 1 - step : step), normal);

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

#nullable enable
using Lib.Utils;
using LibTessDotNet;

namespace Lib.geometry.modify;

/// <summary>
/// Merges overlapping closed curves into clean outlines: Union removes the segments inside other shapes, Xor turns
/// overlaps into holes, Intersect keeps only the overlaps. Works in the XY plane on the flattened curves, so the
/// result consists of straight segments. Source parts whose shapes merge into one island become one part, so touching
/// letters join while an untouched letter keeps all its pieces. Open curves and polygons pass through.
/// </summary>
[Guid("edfa6c74-9efb-4209-b099-2c8d20cb3e37")]
[ExportDependencies("LibTessDotNet.dll")]
internal sealed class MergeCurves : Instance<MergeCurves>, IStatusProvider
{
    private enum Modes
    {
        Union,
        Xor,
        Intersect,
    }

    [Output(Guid = "59b509a9-252c-4cb0-bde9-b7ecf04bc604")]
    public readonly Slot<Geometry?> Result = new();

    public MergeCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Curves.GetValue(context);
        var mode = (Modes)Math.Clamp(Mode.GetValue(context), 0, 2);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-5f);
        _statusMessage = string.Empty;
        if (source == null)
        {
            Result.Value = null;
            return;
        }

        if (!CollectLoops(source, tolerance))
        {
            _statusMessage = "No closed curves to merge.";
            Result.Value = source;
            return;
        }

        var windingRule = mode switch
                              {
                                  Modes.Xor       => WindingRule.EvenOdd,
                                  Modes.Intersect => WindingRule.AbsGeqTwo,
                                  _               => WindingRule.NonZero,
                              };
        Merge(windingRule);
        BuildIslands();
        WriteOutput(source);
        Result.Value = _passThrough.Combine(_output, source, GeometryCurvePassThrough.Selections.OpenCurvesAndPolygons);
    }

    /// <summary>Flattens the closed curves into XY loops and remembers each loop's part.</summary>
    /// <returns>False without closed curves.</returns>
    private bool CollectLoops(Geometry source, float tolerance)
    {
        _sourceLoops.Clear();
        _sourceLoopParts.Clear();
        var curves = source.Curves;
        var zSum = 0.0;
        var zCount = 0;
        for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
        {
            if (source.PrimitiveKind[primitive] != PrimitiveKinds.Curve || !source.PrimitiveClosed[primitive])
                continue;

            _scratch.Clear();
            curves[primitive].Flatten(tolerance, _scratch);
            var loop = new List<Vector2>(_scratch.Count);
            foreach (var p in _scratch)
            {
                var point = new Vector2(p.X, p.Y);
                if (loop.Count == 0 || Vector2.DistanceSquared(loop[^1], point) > 1e-12f)
                    loop.Add(point);

                zSum += p.Z;
                zCount++;
            }

            if (loop.Count > 2 && Vector2.DistanceSquared(loop[0], loop[^1]) < 1e-12f)
                loop.RemoveAt(loop.Count - 1);

            if (loop.Count < 3)
                continue;

            _sourceLoops.Add(loop);
            _sourceLoopParts.Add(FindPart(source, primitive));
        }

        _z = zCount > 0 ? (float)(zSum / zCount) : 0;
        return _sourceLoops.Count > 0;
    }

    private static int FindPart(Geometry source, int primitive)
    {
        for (var part = 0; part < source.Parts.Length; part++)
        {
            var range = source.Parts[part];
            if (primitive >= range.PrimitiveStart && primitive < range.PrimitiveStart + range.PrimitiveCount)
                return part;
        }

        return -1;
    }

    /** The tessellator resolves the overlaps with the fill rule and returns the boundary loops. */
    private void Merge(WindingRule windingRule)
    {
        var tess = new Tess();
        foreach (var loop in _sourceLoops)
        {
            var contour = new ContourVertex[loop.Count];
            for (var i = 0; i < loop.Count; i++)
                contour[i] = new ContourVertex(new Vec3(loop[i].X, loop[i].Y, 0));

            tess.AddContour(contour, ContourOrientation.Original);
        }

        tess.Tessellate(windingRule, ElementType.BoundaryContours, 0, null, new Vec3(0, 0, 1));
        _loops.Clear();
        for (var element = 0; element < tess.ElementCount; element++)
        {
            var start = tess.Elements[element * 2];
            var count = tess.Elements[element * 2 + 1];
            var loop = new List<Vector2>(count);
            for (var i = 0; i < count; i++)
            {
                var v = tess.Vertices[start + i].Position;
                var point = new Vector2(v.X, v.Y);
                if (loop.Count == 0 || Vector2.DistanceSquared(loop[^1], point) > 1e-12f)
                    loop.Add(point);
            }

            if (loop.Count > 2 && Vector2.DistanceSquared(loop[0], loop[^1]) < 1e-12f)
                loop.RemoveAt(loop.Count - 1);

            if (loop.Count >= 3)
                _loops.Add(loop);
        }
    }

    /// <summary>
    /// Sorts the merged loops into islands: a loop inside an even number of others is an outline (made counter-clockwise),
    /// one inside an odd number is a hole (clockwise) of the smallest outline around it.
    /// </summary>
    private void BuildIslands()
    {
        var count = _loops.Count;
        _islandOf.Clear();
        _isOuter.Clear();
        for (var i = 0; i < count; i++)
        {
            var depth = 0;
            for (var j = 0; j < count; j++)
            {
                if (i != j && Contains(_loops[j], _loops[i][0]))
                    depth++;
            }

            var isOuter = depth % 2 == 0;
            _isOuter.Add(isOuter);
            var area = SignedArea(_loops[i]);
            if (isOuter != area > 0)
                _loops[i].Reverse();

            _islandOf.Add(-1);
        }

        for (var i = 0; i < count; i++)
        {
            if (_isOuter[i])
                continue;

            var smallest = float.MaxValue;
            for (var j = 0; j < count; j++)
            {
                if (!_isOuter[j] || !Contains(_loops[j], _loops[i][0]))
                    continue;

                var area = MathF.Abs(SignedArea(_loops[j]));
                if (area < smallest)
                {
                    smallest = area;
                    _islandOf[i] = j;
                }
            }
        }
    }

    /// <summary>
    /// One output part per group of source parts whose shapes ended up in the same island, so an untouched letter keeps
    /// all its pieces (the dot of an i) and touching letters become one part. Islands no source part reaches (e.g. pure
    /// intersections) get a part each. Pivot and part attributes come from the group's first source part.
    /// </summary>
    private void WriteOutput(Geometry source)
    {
        _positions.Clear();
        _offsets.Clear();
        _parts.Clear();
        _partStencil.Clear();
        var sourcePartCount = source.Parts.Length;
        EnsureGroups(sourcePartCount);
        _lastGroup = -1;

        // Which island each outline is, and which source parts reach it; parts in one island are joined
        _islandFirstPart.Clear();
        for (var outer = 0; outer < _loops.Count; outer++)
        {
            var first = -1;
            if (_isOuter[outer])
            {
                for (var loop = 0; loop < _sourceLoops.Count; loop++)
                {
                    var part = _sourceLoopParts[loop];
                    if (part < 0 || !Reaches(_loops[outer], _sourceLoops[loop]))
                        continue;

                    if (first < 0)
                        first = part;
                    else
                        Join(first, part);
                }
            }

            _islandFirstPart.Add(first);
        }

        // Groups in the order of their first source part, then islands no part reaches
        for (var part = 0; part <= sourcePartCount; part++)
        {
            var isUnreached = part == sourcePartCount;
            if (!isUnreached && FindGroup(part) != part)
                continue;

            for (var outer = 0; outer < _loops.Count; outer++)
            {
                if (!_isOuter[outer])
                    continue;

                var islandPart = _islandFirstPart[outer];
                var belongs = isUnreached ? islandPart < 0 : islandPart >= 0 && FindGroup(islandPart) == part;
                if (!belongs)
                    continue;

                var partStart = _offsets.Count;
                AddIsland(outer);
                if (isUnreached)
                    AddPart(source, -1, partStart, GetCentroid(_loops[outer]));
                else if (_parts.Count == 0 || _lastGroup != part)
                    AddPart(source, part, partStart, source.Parts[part].Pivot);
                else
                    _parts[^1] = _parts[^1] with { PrimitiveCount = _offsets.Count - _parts[^1].PrimitiveStart };

                _lastGroup = isUnreached ? -1 : part;
            }
        }

        var primitiveCount = _offsets.Count;
        _output.Resize(_positions.Count, primitiveCount, _positions.Count);
        _positions.CopyTo(_output.Positions);
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            _output.PrimitiveVertexOffsets[primitive] = _offsets[primitive];
            _output.PrimitiveKind[primitive] = PrimitiveKinds.Curve;
            _output.PrimitiveClosed[primitive] = true;
        }

        _output.PrimitiveVertexOffsets[primitiveCount] = _positions.Count;
        for (var vertex = 0; vertex < _positions.Count; vertex++)
            _output.VertexPoints[vertex] = vertex;

        _output.Parts = _parts.ToArray();
        _output.Attributes.Clear();
        if (sourcePartCount > 0)
            _partStencil.Apply(source.Attributes, _output.Attributes, AttributeDomain.Part);

        _output.InvalidateTopologyCaches();
    }

    private void AddIsland(int outer)
    {
        AddLoop(_loops[outer]);
        for (var hole = 0; hole < _loops.Count; hole++)
        {
            if (_islandOf[hole] == outer)
                AddLoop(_loops[hole]);
        }
    }

    private void AddPart(Geometry source, int sourcePart, int partStart, Vector3 pivot)
    {
        var id = sourcePart >= 0 ? source.Parts[sourcePart].Id : _parts.Count;
        var seed = sourcePart >= 0 ? source.Parts[sourcePart].SeedIndex : _parts.Count;
        _parts.Add(new PrimitivePart(partStart, _offsets.Count - partStart, pivot, id, seed));
        if (source.Parts.Length > 0)
            _partStencil.AddCopy(Math.Max(sourcePart, 0));
    }

    /** True when a point of the source loop lies inside or on the outline, i.e. the loop contributed to the island. */
    private static bool Reaches(List<Vector2> outline, List<Vector2> sourceLoop)
    {
        foreach (var point in sourceLoop)
        {
            if (Contains(outline, point) || IsOnOutline(outline, point))
                return true;
        }

        return false;
    }

    private static bool IsOnOutline(List<Vector2> outline, Vector2 point)
    {
        foreach (var vertex in outline)
        {
            if (Vector2.DistanceSquared(vertex, point) < 1e-10f)
                return true;
        }

        return false;
    }

    private void EnsureGroups(int count)
    {
        if (_groups.Length < count)
            _groups = new int[count];

        for (var index = 0; index < count; index++)
            _groups[index] = index;
    }

    /** Union-find: the smallest part index stands for its group. */
    private int FindGroup(int part)
    {
        while (_groups[part] != part)
            part = _groups[part] = _groups[_groups[part]];

        return part;
    }

    private void Join(int a, int b)
    {
        var groupA = FindGroup(a);
        var groupB = FindGroup(b);
        if (groupA == groupB)
            return;

        if (groupA < groupB)
            _groups[groupB] = groupA;
        else
            _groups[groupA] = groupB;
    }

    private void AddLoop(List<Vector2> loop)
    {
        _offsets.Add(_positions.Count);
        foreach (var point in loop)
            _positions.Add(new Vector3(point.X, point.Y, _z));
    }

    private static Vector3 GetCentroid(List<Vector2> loop)
    {
        var sum = Vector2.Zero;
        foreach (var point in loop)
            sum += point;

        var center = sum / loop.Count;
        return new Vector3(center, 0);
    }

    private static float SignedArea(List<Vector2> loop)
    {
        var area = 0f;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5f;
    }

    private static bool Contains(List<Vector2> loop, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var a = loop[i];
            var b = loop[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    private readonly Geometry _output = new();
    private readonly GeometryCurvePassThrough _passThrough = new();
    private readonly GeometryAttributeStencil _partStencil = new();
    private readonly List<List<Vector2>> _sourceLoops = [];
    private readonly List<int> _sourceLoopParts = [];
    private readonly List<List<Vector2>> _loops = [];
    private readonly List<bool> _isOuter = [];
    private readonly List<int> _islandOf = [];
    private readonly List<Vector3> _scratch = [];
    private readonly List<Vector3> _positions = [];
    private readonly List<int> _offsets = [];
    private readonly List<PrimitivePart> _parts = [];
    private readonly List<int> _islandFirstPart = [];
    private int[] _groups = [];
    private int _lastGroup = -1;
    private float _z;
    private string _statusMessage = string.Empty;

    [Input(Guid = "db5a0ef8-d464-4606-a7c4-4a57063deb27")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "d5994346-fa1f-4152-9bd4-a0e15a6122af", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "ad16ec6a-842a-44fd-9fd0-4212be66d128")]
    public readonly InputSlot<float> Tolerance = new();
}

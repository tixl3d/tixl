#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using LibTessDotNet;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Turns the closed curve primitives of a Geometry into polygons, part by part: a flat fill (depth 0, front cap only)
/// or a solid with back cap, side walls and bevel, as [ExtrudeCurves] and [FillCurves] use it. Accumulates faces over
/// all parts. Per part: contours are flattened and oriented
/// so the solid lies on their left (outers counter-clockwise, holes clockwise, by
/// nesting parity), inset by the bevel for the caps, tessellated with the fill
/// rule, and connected by profile rings for bevel and wall faces.
/// </summary>
internal sealed class CurveSolidBuilder
{
    /// <summary>Flat fill of every part's closed curves: depth 0, front cap only, no bevel.</summary>
    /// <param name="skipPartsWithPolygons">
    /// Leaves parts that already have polygons alone, e.g. extruded text whose curves were kept: filling them again would
    /// duplicate its front cap.
    /// </param>
    public void FillFlat(Geometry curves, float tolerance, float weight, WindingRule windingRule, bool keepCurves, Geometry target,
                         bool skipPartsWithPolygons = false, float inset = 0)
    {
        Begin();
        var parts = curves.Parts;
        var curveViews = curves.Curves;
        var partCount = parts.Length > 0 ? parts.Length : 1;
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            var primitiveStart = parts.Length > 0 ? parts[partIndex].PrimitiveStart : 0;
            var primitiveEnd = parts.Length > 0 ? primitiveStart + parts[partIndex].PrimitiveCount : curves.PrimitiveCount;
            var pivot = parts.Length > 0 ? parts[partIndex].Pivot : Vector3.Zero;
            var id = parts.Length > 0 ? parts[partIndex].Id : 0;
            var seed = parts.Length > 0 ? parts[partIndex].SeedIndex : 0;
            if (skipPartsWithPolygons && HasPolygons(curves, primitiveStart, primitiveEnd))
                continue;

            BuildPart(curves, curveViews, primitiveStart, primitiveEnd, tolerance, depth: 0, bevel: 0, bevelSegments: 1, weight,
                      flatShading: false, smoothAngle: 40, front: true, back: false, sides: false, windingRule, keepCurves, pivot, id, seed, inset);
        }

        Finish(target, curves);
    }

    private static bool HasPolygons(Geometry geometry, int primitiveStart, int primitiveEnd)
    {
        for (var primitive = primitiveStart; primitive < Math.Min(primitiveEnd, geometry.PrimitiveCount); primitive++)
        {
            if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Polygon)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Cell size of a square grid that splits the caps into regular quads (0: off), so they subdivide and deform
    /// without long thin triangles. The outlines get a point where they cross a grid line, unless an existing point is
    /// within a tenth of a cell; cap points that close to an outline point use it instead, so caps and walls share
    /// their points. Not combined with a bevel.
    /// </summary>
    public float Lattice { get; set; }

    /// <summary>Why the last build ignored <see cref="Lattice"/>, or null.</summary>
    public string? LatticeIssue { get; private set; }

    /// <summary>Why the last build used a smaller inset than requested, or null.</summary>
    public string? InsetIssue { get; private set; }

    public void Begin()
    {
        LatticeIssue = null;
        InsetIssue = null;
        _pointSources.Clear();
        _primitiveSources.Clear();
        _carriesAttributes = false;
        _positions.Clear();
        _corners.Clear();
        _normals.Clear();
        _faceOffsets.Clear();
        _faceOffsets.Add(0);
        _isSide.Clear();
        _parts.Clear();
        _kinds.Clear();
        _closed.Clear();
        _curvePointHandles.Clear();
        _ringZeroSeed.Clear();
        _innerLoops.Clear();
        _innerAlignedIds.Clear();
        _innerLoopSources.Clear();
        _collapsedStrips.Clear();
        _capBoundaryIsInset = false;
    }

    public void Finish(Geometry target, Geometry source)
    {
        RemoveUnusedPoints();
        var pointCount = _positions.Count;
        var primitiveCount = _faceOffsets.Count - 1;
        var vertexCount = _corners.Count;
        PadPolygonKinds(primitiveCount);

        target.Resize(pointCount, primitiveCount, vertexCount);
        _positions.CopyTo(target.Positions);
        _corners.CopyTo(target.VertexPoints);
        _faceOffsets.CopyTo(target.PrimitiveVertexOffsets);
        _kinds.CopyTo(target.PrimitiveKind);
        _closed.CopyTo(target.PrimitiveClosed);
        target.Parts = _parts.ToArray();
        target.Attributes.Clear();

        var normals = target.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Vertex, vertexCount);
        _normals.CopyTo(normals.Values);
        var isSide = target.Attributes.GetOrCreate<float>(GeometryAttributeNames.IsSide, AttributeDomain.Primitive, primitiveCount);
        _isSide.CopyTo(isSide.Values);

        if (_curvePointHandles.Count > 0)
        {
            var handlesIn = target.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.HandleIn, AttributeDomain.Point, pointCount).Values;
            var handlesOut = target.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.HandleOut, AttributeDomain.Point, pointCount).Values;
            Array.Clear(handlesIn);
            Array.Clear(handlesOut);
            foreach (var (point, handleIn, handleOut) in _curvePointHandles)
            {
                handlesIn[point] = handleIn;
                handlesOut[point] = handleOut;
            }
        }

        if (_carriesAttributes && _pointSources.Count == pointCount && _primitiveSources.Count == primitiveCount)
            CarryAttributes(source, target);

        // Part attributes of the source (glyph indices etc.) apply 1:1 to the output parts and are shared as they are
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain == AttributeDomain.Part && attribute.Count == _parts.Count)
                target.Attributes.Add(attribute);
        }

        target.InvalidateTopologyCaches();
    }

    /// <summary>
    /// Profile rings that no face uses (the second ring of a flat fill, the back ring without a back cap or walls) would
    /// stay behind as loose duplicates, so points without a vertex are removed before the geometry is written.
    /// </summary>
    private void RemoveUnusedPoints()
    {
        var pointCount = _positions.Count;
        if (_pointRemap.Length < pointCount)
            _pointRemap = new int[pointCount];

        Array.Fill(_pointRemap, -1, 0, pointCount);
        foreach (var point in _corners)
            _pointRemap[point] = 0;

        var kept = 0;
        for (var point = 0; point < pointCount; point++)
        {
            if (_pointRemap[point] < 0)
                continue;

            _pointRemap[point] = kept;
            _positions[kept] = _positions[point];
            if (_carriesAttributes && _pointSources.Count == pointCount)
                _pointSources[kept] = _pointSources[point];

            kept++;
        }

        if (kept == pointCount)
            return;

        _positions.RemoveRange(kept, pointCount - kept);
        if (_carriesAttributes && _pointSources.Count == pointCount)
            _pointSources.RemoveRange(kept, pointCount - kept);

        for (var index = 0; index < _corners.Count; index++)
            _corners[index] = _pointRemap[_corners[index]];

        for (var index = 0; index < _curvePointHandles.Count; index++)
        {
            var (point, handleIn, handleOut) = _curvePointHandles[index];
            _curvePointHandles[index] = (_pointRemap[point], handleIn, handleOut);
        }
    }

    private int[] _pointRemap = [];

    public void BuildPart(Geometry geometry, Geometry.CurveEnumerable curveViews, int primitiveStart, int primitiveEnd, float tolerance, float depth,
                          float bevel, int bevelSegments, float weight, bool flatShading, float smoothAngle, bool front, bool back, bool sides,
                          WindingRule windingRule, bool keepCurves, Vector3 pivot, int id, int seed, float inset = 0)
    {
        var partStart = _faceOffsets.Count - 1;
        var pointStart = _positions.Count;
        _carriesAttributes = HasAttributesToCarry(geometry);
        _samples.Clear();
        BuildFaces(geometry, curveViews, primitiveStart, primitiveEnd, tolerance, depth, bevel, bevelSegments, weight, flatShading, smoothAngle,
                   front, back, sides, windingRule, inset);
        if (_carriesAttributes)
            MapToSamples(pointStart, partStart);

        if (keepCurves)
            AppendCurves(geometry, curveViews, primitiveStart, primitiveEnd);

        var primitiveCount = _faceOffsets.Count - 1 - partStart;
        if (primitiveCount > 0)
            _parts.Add(new PrimitivePart(partStart, primitiveCount, pivot, id, seed));
    }

    /// <summary>Copies the part's curve primitives after its polygons, with their handles; other curve attributes are dropped.</summary>
    private void AppendCurves(Geometry geometry, Geometry.CurveEnumerable curveViews, int primitiveStart, int primitiveEnd)
    {
        for (var primitive = primitiveStart; primitive < primitiveEnd; primitive++)
        {
            if (geometry.PrimitiveKind[primitive] != PrimitiveKinds.Curve)
                continue;

            PadPolygonKinds(_faceOffsets.Count - 1);
            var curve = curveViews[primitive];
            for (var index = 0; index < curve.VertexCount; index++)
            {
                var position = curve.GetPosition(index);
                if (_carriesAttributes)
                    _pointSources.Add(new PointSource(curve.GetPointIndex(index), curve.GetPointIndex(index), 0));

                _curvePointHandles.Add((_positions.Count, curve.GetHandleInPosition(index) - position, curve.GetHandleOutPosition(index) - position));
                _corners.Add(_positions.Count);
                _normals.Add(Vector3.Zero);
                _positions.Add(position);
            }

            _faceOffsets.Add(_corners.Count);
            _isSide.Add(0);
            if (_carriesAttributes)
                _primitiveSources.Add(primitive);

            _kinds.Add(PrimitiveKinds.Curve);
            _closed.Add(curve.IsClosed);
        }
    }

    /// <summary>
    /// Point and primitive attributes of the curves are carried over: each output point blends the two curve points of
    /// the segment its nearest flattened sample lies on, wall faces copy their curve's primitive attributes, caps get
    /// zero. Handles, normals and IsSide are written by the builder itself.
    /// </summary>
    private void CarryAttributes(Geometry source, Geometry target)
    {
        _pointStencil.Clear();
        foreach (var pointSource in _pointSources)
        {
            _pointStencil.Begin();
            if (pointSource.A < 0)
                continue;

            var w = pointSource.W;
            PushPair(pointSource.A, pointSource.B, pointSource.T, 1 - w);
            if (w > 0)
                PushPair(pointSource.C, pointSource.D, pointSource.U, w);
        }

        _primitiveStencil.Clear();
        foreach (var primitive in _primitiveSources)
        {
            if (primitive >= 0)
                _primitiveStencil.AddCopy(primitive);
            else
                _primitiveStencil.Begin();
        }

        _pointStencil.Apply(source.Attributes, target.Attributes, AttributeDomain.Point, _builderPointAttributes);
        _primitiveStencil.Apply(source.Attributes, target.Attributes, AttributeDomain.Primitive, _builderPrimitiveAttributes);
    }

    private void PushPair(int a, int b, float t, float weight)
    {
        _pointStencil.Push(a, (1 - t) * weight);
        if (t > 0)
            _pointStencil.Push(b, t * weight);
    }

    /** Only point and primitive attributes besides the ones the builder writes are carried; without any the bookkeeping is skipped. */
    private static bool HasAttributesToCarry(Geometry geometry)
    {
        for (var index = 0; index < geometry.Attributes.Count; index++)
        {
            var attribute = geometry.Attributes[index];
            if (attribute.Domain == AttributeDomain.Primitive && !IsBuilderAttribute(attribute.Name, _builderPrimitiveAttributes))
                return true;

            if (attribute.Domain == AttributeDomain.Point && !IsBuilderAttribute(attribute.Name, _builderPointAttributes))
                return true;
        }

        return false;
    }

    private static bool IsBuilderAttribute(string name, string[] names)
    {
        foreach (var builderName in names)
        {
            if (string.Equals(name, builderName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The flattened samples of one curve with the curve points of their segment and their position along it (by
    /// length), split at the samples that are exactly the curve's points.
    /// </summary>
    private void RecordSamples(CurveView curve, int primitive)
    {
        var sampleCount = _scratch.Count;
        var vertexCount = curve.VertexCount;
        if (sampleCount == 0 || vertexCount == 0)
            return;

        var firstSample = _samples.Count;
        var segmentStart = 0;
        for (var segment = 0; segment < curve.SegmentCount && segmentStart < sampleCount; segment++)
        {
            var endVertex = (segment + 1) % vertexCount;
            var isClosingSegment = segment == curve.SegmentCount - 1 && curve.IsClosed;
            var segmentEnd = sampleCount;
            if (!isClosingSegment)
            {
                var endPosition = curve.GetPosition(endVertex);
                for (var k = segmentStart + 1; k < sampleCount; k++)
                {
                    if (_scratch[k] != endPosition)
                        continue;

                    segmentEnd = k;
                    break;
                }
            }

            // Length along the segment up to its end (the next curve point, or the first sample when closing)
            var endSample = segmentEnd < sampleCount ? _scratch[segmentEnd] : _scratch[0];
            var total = 0f;
            for (var k = segmentStart; k < segmentEnd; k++)
                total += Vector3.Distance(_scratch[k], k + 1 < segmentEnd ? _scratch[k + 1] : endSample);

            var a = curve.GetPointIndex(segment);
            var b = curve.GetPointIndex(endVertex);
            var length = 0f;
            for (var k = segmentStart; k < segmentEnd; k++)
            {
                _samples.Add(new Sample(new Vector2(_scratch[k].X, _scratch[k].Y), a, b, total > 1e-9f ? length / total : 0, primitive));
                length += Vector3.Distance(_scratch[k], k + 1 < segmentEnd ? _scratch[k + 1] : endSample);
            }

            segmentStart = segmentEnd;
        }

        // An open curve ends on its last point
        if (!curve.IsClosed && segmentStart < sampleCount)
        {
            var last = curve.GetPointIndex(vertexCount - 1);
            _samples.Add(new Sample(new Vector2(_scratch[^1].X, _scratch[^1].Y), last, last, 0, primitive));
        }

        // Link the samples along the curve for projecting and scanning
        var end = _samples.Count;
        for (var index = firstSample; index < end; index++)
        {
            var next = index + 1 < end ? index + 1 : curve.IsClosed ? firstSample : -1;
            _samples[index] = _samples[index] with { Next = next };
        }
    }

    /** Every point and wall face of the part takes the source of its nearest sample, found through a coarse grid. */
    private void MapToSamples(int pointStart, int faceStart)
    {
        BuildSegmentGrid();
        for (var point = pointStart; point < _positions.Count; point++)
        {
            // Lattice points clipped onto the outline are cap points too, but lie on it: they take the outline's value
            var position = new Vector2(_positions[point].X, _positions[point].Y);
            var onOutline = ProjectOntoOutline(position, out var distanceSquared);
            _pointSources.Add(_interiorCapPoints.Contains(point) && distanceSquared > OnOutlineDistanceSquared ? ScanAcross(position) : onOutline);
        }

        _interiorCapPoints.Clear();

        for (var face = faceStart; face < _faceOffsets.Count - 1; face++)
        {
            if (_isSide[face] <= 0)
            {
                _primitiveSources.Add(-1);
                continue;
            }

            var first = _positions[_corners[_faceOffsets[face]]];
            var segment = FindNearestSegment(new Vector2(first.X, first.Y), out _, out _);
            _primitiveSources.Add(segment >= 0 ? _samples[segment].Primitive : -1);
        }
    }

    /** The outline position closest to the point, on the nearest segment between two consecutive samples. */
    private PointSource ProjectOntoOutline(Vector2 position) => ProjectOntoOutline(position, out _);

    private PointSource ProjectOntoOutline(Vector2 position, out float distanceSquared)
    {
        var segment = FindNearestSegment(position, out var u, out distanceSquared);
        if (segment < 0)
            return new PointSource(-1, -1, 0);

        var next = _samples[segment].Next;
        return next < 0 ? _samples[segment].Source : GetSourceBetween(segment, next, u);
    }

    /// <summary>
    /// For a point inside a cap: the outline to its left and right on a horizontal line, blended by distance, so values
    /// that change across a letter (a selection, a gradient) continue smoothly through the cap. Falls back to the
    /// nearest outline position when the line finds only one side.
    /// </summary>
    private PointSource ScanAcross(Vector2 position)
    {
        var left = new PointSource(-1, -1, 0);
        var right = left;
        float leftX = float.MinValue, rightX = float.MaxValue;
        for (var index = 0; index < _samples.Count; index++)
        {
            var next = _samples[index].Next;
            if (next < 0)
                continue;

            var a = _samples[index].Position;
            var b = _samples[next].Position;
            if ((a.Y - position.Y) * (b.Y - position.Y) > 0 || MathF.Abs(b.Y - a.Y) < 1e-12f)
                continue;

            var u = (position.Y - a.Y) / (b.Y - a.Y);
            var x = a.X + (b.X - a.X) * u;
            if (x <= position.X && x > leftX)
            {
                leftX = x;
                left = GetSourceBetween(index, next, u);
            }
            else if (x > position.X && x < rightX)
            {
                rightX = x;
                right = GetSourceBetween(index, next, u);
            }
        }

        if (left.A < 0 || right.A < 0)
            return ProjectOntoOutline(position);

        var w = (position.X - leftX) / MathF.Max(rightX - leftX, 1e-9f);
        return PointSource.Blend(left, right, w);
    }

    /// <summary>
    /// The curve position at <paramref name="u"/> between two consecutive samples, as a single segment position: both
    /// usually lie on one segment, or the second starts the next segment (then it is the first segment's end).
    /// </summary>
    private PointSource GetSourceBetween(int from, int to, float u)
    {
        var a = _samples[from];
        var b = _samples[to];
        if (a.A == b.A && a.B == b.B)
            return new PointSource(a.A, a.B, a.T + (b.T - a.T) * u);

        if (b.T == 0 && b.A == a.B)
            return new PointSource(a.A, a.B, a.T + (1 - a.T) * u);

        return u < 0.5f ? a.Source : b.Source;
    }

    /// <summary>
    /// Every segment from a sample to its next one is listed in the grid cells its bounds overlap; a sample without a
    /// next one (the end of an open curve) as a point. Long straight segments have only two samples, so searching
    /// segments rather than samples is what finds the closest outline position.
    /// </summary>
    private void BuildSegmentGrid()
    {
        foreach (var list in _segmentGrid.Values)
            list.Clear();

        if (_samples.Count == 0)
            return;

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var sample in _samples)
        {
            min = Vector2.Min(min, sample.Position);
            max = Vector2.Max(max, sample.Position);
        }

        var extent = max - min;
        _segmentCellSize = MathF.Max(MathF.Max(extent.X, extent.Y) / MathF.Max(1, MathF.Sqrt(_samples.Count)), 1e-5f);
        for (var index = 0; index < _samples.Count; index++)
        {
            var a = _samples[index].Position;
            var next = _samples[index].Next;
            var b = next >= 0 ? _samples[next].Position : a;
            var (x0, y0) = SegmentCell(Vector2.Min(a, b));
            var (x1, y1) = SegmentCell(Vector2.Max(a, b));
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    if (!_segmentGrid.TryGetValue((x, y), out var list))
                    {
                        list = [];
                        _segmentGrid[(x, y)] = list;
                    }

                    list.Add(index);
                }
            }
        }
    }

    private (long, long) SegmentCell(Vector2 position)
    {
        return ((long)MathF.Floor(position.X / _segmentCellSize), (long)MathF.Floor(position.Y / _segmentCellSize));
    }

    /** Searches rings of grid cells outwards until no closer segment can exist; returns its first sample and the position along it. */
    private int FindNearestSegment(Vector2 position, out float bestU, out float bestDistance)
    {
        bestU = 0;
        bestDistance = float.MaxValue;
        if (_samples.Count == 0)
            return -1;

        var (cx, cy) = SegmentCell(position);
        var best = -1;
        for (var ring = 0; ring < 4096; ring++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || !_segmentGrid.TryGetValue((cx + dx, cy + dy), out var list))
                        continue;

                    foreach (var index in list)
                    {
                        var a = _samples[index].Position;
                        var next = _samples[index].Next;
                        var segment = next >= 0 ? _samples[next].Position - a : Vector2.Zero;
                        var lengthSquared = segment.LengthSquared();
                        var u = lengthSquared > 1e-14f ? Math.Clamp(Vector2.Dot(position - a, segment) / lengthSquared, 0, 1) : 0;
                        var distance = Vector2.DistanceSquared(a + segment * u, position);
                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        bestU = u;
                        best = index;
                    }
                }
            }

            // Segments not met yet lie entirely in cells at least ring cells away
            var reach = ring * _segmentCellSize;
            if (best >= 0 && reach * reach >= bestDistance)
                break;
        }

        return best;
    }

    private readonly record struct Sample(Vector2 Position, int A, int B, float T, int Primitive, int Next = -1)
    {
        public PointSource Source => new(A, B, T);
    }

    /// <summary>A blend of two positions on the curves: lerp(lerp(A, B, T), lerp(C, D, U), W); A &lt; 0 means no source.</summary>
    private readonly record struct PointSource(int A, int B, float T, int C, int D, float U, float W)
    {
        public PointSource(int a, int b, float t) : this(a, b, t, a, b, t, 0) { }

        public static PointSource Blend(PointSource first, PointSource second, float w) => new(first.A, first.B, first.T, second.A, second.B, second.T, w);
    }

    private static readonly string[] _builderPointAttributes = [GeometryAttributeNames.HandleIn, GeometryAttributeNames.HandleOut, GeometryAttributeNames.Normal];
    private static readonly string[] _builderPrimitiveAttributes = [GeometryAttributeNames.IsSide];

    private const float OnOutlineDistanceSquared = 1e-5f * 1e-5f;

    private bool _carriesAttributes;
    private readonly HashSet<int> _interiorCapPoints = [];
    private readonly List<Sample> _samples = [];
    private readonly List<PointSource> _pointSources = [];
    private readonly List<int> _primitiveSources = [];
    private readonly Dictionary<(long, long), List<int>> _segmentGrid = [];
    private float _segmentCellSize = 1;
    private readonly GeometryAttributeStencil _pointStencil = new();
    private readonly GeometryAttributeStencil _primitiveStencil = new();

    /** Faces are added without a kind; everything up to <paramref name="primitiveCount"/> not yet tagged is a closed polygon. */
    private void PadPolygonKinds(int primitiveCount)
    {
        while (_kinds.Count < primitiveCount)
        {
            _kinds.Add(PrimitiveKinds.Polygon);
            _closed.Add(true);
        }
    }

    private void BuildFaces(Geometry geometry, Geometry.CurveEnumerable curveViews, int primitiveStart, int primitiveEnd, float tolerance, float depth,
                            float bevel, int bevelSegments, float weight, bool flatShading, float smoothAngle, bool front, bool back, bool sides,
                            WindingRule windingRule, float inset)
    {
        // Flatten the part's closed curves into 2D loops
        _loops.Clear();
        for (var primitive = primitiveStart; primitive < primitiveEnd; primitive++)
        {
            if (geometry.PrimitiveKind[primitive] != PrimitiveKinds.Curve || !geometry.PrimitiveClosed[primitive])
                continue;

            var loop = new List<Vector2>();
            _scratch.Clear();
            curveViews[primitive].Flatten(tolerance, _scratch);
            if (_carriesAttributes)
                RecordSamples(curveViews[primitive], primitive);
            foreach (var p in _scratch)
            {
                if (loop.Count == 0 || Vector2.DistanceSquared(loop[^1], new Vector2(p.X, p.Y)) > 1e-12f)
                    loop.Add(new Vector2(p.X, p.Y));
            }

            if (loop.Count > 2 && Vector2.DistanceSquared(loop[0], loop[^1]) < 1e-12f)
                loop.RemoveAt(loop.Count - 1);

            if (loop.Count >= 3)
                _loops.Add(loop);
        }

        if (_loops.Count == 0)
            return;

        // Overlapping contours (bold variable instances, stacked strokes) must become one
        // outline before walls are built, otherwise walls run through the solid. The
        // tessellator resolves them with the fill rule and hands back the boundary loops.
        ResolveOverlaps(windingRule);
        if (_loops.Count == 0)
            return;

        OrientLoops();

        // Faux bold: offset the resolved outline outwards (or inwards for a lighter look).
        // The offset folds at concave corners and can invert counters; a second pass through
        // the fill rule drops those inverted loops, the same trick polygon offsetters use.
        if (MathF.Abs(weight) > 1e-6f)
        {
            OffsetLoops(-weight);
            ResolveOverlaps(WindingRule.NonZero);
            if (_loops.Count == 0)
                return;

            OrientLoops();
        }

        _usesLattice = Lattice > 0 && bevel <= 0;
        if (Lattice > 0 && bevel > 0)
            LatticeIssue = "Lattice is ignored with a bevel.";

        var hasWalls = sides && depth > 0;
        var profile = BuildProfile(depth, bevel, bevelSegments, hasWalls);
        var useInset = inset > 1e-6f && front && !hasWalls;
        _capBoundaryIsInset = false;
        if (useInset)
        {
            // The outline ring is built first so the inset strip can share its points; the caps then use the
            // smaller inner rings, which BuildInsetBand builds its own rings for.
            BuildRings(profile);
            BuildInsetBand(inset);
            if (_loops.Count == 0)
                return; // the inset consumed the whole outline and the strip is all that is left
        }
        else
        {
            if (_usesLattice)
                InsertGridCrossings(Lattice, Lattice * LatticeSnap);

            BuildRings(profile);
        }

        // Caps and walls share the ring points, so the solid closes without a weld
        if (front)
            AddCap(ringIndex: 0, windingRule, flip: false);

        if (back && depth > 0)
            AddCap(ringIndex: profile.Count - 1, windingRule, flip: true);

        if (hasWalls)
            AddWalls(profile, flatShading, MathF.Cos(smoothAngle * MathF.PI / 180f));
    }

    /// <summary>Profile of the extrusion as (inset, z) samples from the front cap to the back cap.</summary>
    private List<(float Inset, float Z)> BuildProfile(float depth, float bevel, int segments, bool hasWalls)
    {
        _profile.Clear();
        if (!hasWalls)
        {
            _profile.Add((0, 0));
            _profile.Add((0, -depth));
            return _profile;
        }

        if (bevel <= 0)
        {
            _profile.Add((0, 0));
            _profile.Add((0, -depth));
            return _profile;
        }

        // Quarter circles at both ends: inset shrinks from bevel to 0 while z goes from 0 to -bevel
        for (var i = 0; i <= segments; i++)
        {
            var angle = i / (float)segments * MathF.PI * 0.5f;
            _profile.Add((bevel * (1 - MathF.Sin(angle)), -bevel * (1 - MathF.Cos(angle))));
        }

        for (var i = segments; i >= 0; i--)
        {
            var angle = i / (float)segments * MathF.PI * 0.5f;
            _profile.Add((bevel * (1 - MathF.Sin(angle)), -depth + bevel * (1 - MathF.Cos(angle))));
        }

        return _profile;
    }

    private void OffsetLoops(float inset)
    {
        foreach (var loop in _loops)
        {
            _scratch2.Clear();
            for (var i = 0; i < loop.Count; i++)
                _scratch2.Add(InsetPoint(loop, i, inset));

            loop.Clear();
            loop.AddRange(_scratch2);
        }
    }

    /// <summary>
    /// Replaces the part's outlines with the inner side of a boundary strip: every outline edge runs to the inset
    /// ring, and the cap is tessellated inside the inset rings. The inset ring is the straight skeleton of each
    /// loop, so corners keep their miter and short edges collapse to a point instead of folding. Loops are clamped
    /// against each other first, so a thin stem leaves a cap between its sides.
    /// </summary>
    private void BuildInsetBand(float requested)
    {
        _innerLoops.Clear();
        _innerAlignedIds.Clear();
        _innerLoopSources.Clear();
        _collapsedStrips.Clear();
        _ringZeroSeed.Clear();

        var width = requested;
        var crossLoopDistance = MinDistanceBetweenLoops(_loops);
        if (crossLoopDistance < float.MaxValue)
        {
            var safeWidth = crossLoopDistance * InsetOverlapSafety;
            if (width > safeWidth)
            {
                width = safeWidth;
                InsetIssue = $"Inset limited to {width:0.###} so opposite outlines stay apart.";
            }
        }

        if (width <= 1e-6f)
            return;

        // BuildRings below replaces _loopRings, so keep the outline rings for the strip
        var outlineRings = new List<int[]>(_loopRings);
        for (var l = 0; l < _loops.Count; l++)
        {
            var loop = _loops[l];
            var count = loop.Count;
            BuildLoopSkeleton(loop);
            var limit = _skeleton.Limit(width, Vector3.UnitZ, CancellationToken.None);

            if (_insetSlotPointIds.Length < count)
                _insetSlotPointIds = new int[count];

            // Corners that arrive at the same spot after a collapse share one point
            var weldDistanceSq = MathF.Max(limit * InsetCollapseWeldFactor, 1e-7f);
            weldDistanceSq *= weldDistanceSq;
            for (var slot = 0; slot < count; slot++)
            {
                var vertex = _skeleton[slot];
                _insetSlotPointIds[slot] = -1;
                if (!vertex.Alive)
                    continue;

                var position = vertex.Origin + vertex.Velocity * limit;
                var previousSlot = _skeleton.ResolveSlot(vertex.Previous);
                if (previousSlot < slot && _insetSlotPointIds[previousSlot] >= 0
                    && Vector3.DistanceSquared(_positions[_insetSlotPointIds[previousSlot]], position) < weldDistanceSq)
                {
                    _insetSlotPointIds[slot] = _insetSlotPointIds[previousSlot];
                    continue;
                }

                _insetSlotPointIds[slot] = AddPoint(position);
            }

            _insetInnerIds.Clear();
            for (var i = 0; i < count; i++)
                _insetInnerIds.Add(_insetSlotPointIds[_skeleton.ResolveSlot(i)]);

            // The cap runs inside the unique inset points; collapsed runs of corners become one vertex
            _scratch2.Clear();
            _innerLoopIds.Clear();
            for (var i = 0; i < count; i++)
            {
                var point = _insetInnerIds[i];
                if (_innerLoopIds.Count > 0 && _innerLoopIds[^1] == point)
                    continue;

                _innerLoopIds.Add(point);
                _scratch2.Add(new Vector2(_positions[point].X, _positions[point].Y));
            }

            while (_innerLoopIds.Count > 1 && _innerLoopIds[^1] == _innerLoopIds[0])
            {
                _innerLoopIds.RemoveAt(_innerLoopIds.Count - 1);
                _scratch2.RemoveAt(_scratch2.Count - 1);
            }

            if (_innerLoopIds.Count < 3)
            {
                // The inset collapsed this loop to a point or ridge; it gets a closing fan but no cap
                _collapsedStrips.Add((outlineRings[l], new List<int>(_insetInnerIds)));
                continue;
            }

            _innerLoops.Add(new List<Vector2>(_scratch2));
            _innerAlignedIds.Add(new List<int>(_insetInnerIds));
            _innerLoopSources.Add(l);
            for (var i = 0; i < _innerLoopIds.Count; i++)
                _ringZeroSeed.Add((_scratch2[i], _innerLoopIds[i]));
        }

        _loops.Clear();
        _loops.AddRange(_innerLoops);
        _capBoundaryIsInset = _innerLoops.Count > 0;
        if (_innerLoops.Count > 0)
        {
            // No grid crossings are inserted into the inset ring: the cap resolves its intersections with the
            // ring onto the nearest inset point instead, so the strip stays one quad per outline edge.
            BuildRings(_profile);
            _ringZeroSeed.Clear();

            for (var l = 0; l < _innerLoops.Count; l++)
                AddInsetStrip(outlineRings[_innerLoopSources[l]], _innerAlignedIds[l]);
        }
        else
        {
            _ringZeroSeed.Clear();
        }

        foreach (var (outlineIds, alignedInnerIds) in _collapsedStrips)
            AddCollapsedInsetStrip(outlineIds, alignedInnerIds);
    }

    /// <summary>Emits the strip of a loop whose inset collapsed: its inner points are already at a point or ridge.</summary>
    private void AddCollapsedInsetStrip(int[] outlineIds, List<int> alignedInnerIds)
    {
        var count = alignedInnerIds.Count;
        for (var i = 0; i < count; i++)
        {
            var next = i + 1 == count ? 0 : i + 1;
            _polygon.Clear();
            _polygon.Add(outlineIds[i]);
            _polygon.Add(outlineIds[next]);
            _polygon.Add(alignedInnerIds[next]);
            _polygon.Add(alignedInnerIds[i]);
            AddCapPolygon(Vector3.UnitZ, flip: false);
        }
    }

    /// <summary>
    /// Emits the strip around one loop: every outline edge becomes a quad between the outline and its inset edge.
    /// A collapsed inset edge leaves a triangle that closes the strip at the collapsed corner. The inset ring is
    /// not split by lattice crossings; the cap snaps its border cells to these points instead.
    /// </summary>
    private void AddInsetStrip(int[] outlineIds, List<int> alignedInnerIds)
    {
        var count = alignedInnerIds.Count;
        for (var i = 0; i < count; i++)
        {
            var next = i + 1 == count ? 0 : i + 1;
            var insetStart = alignedInnerIds[i];
            var insetEnd = alignedInnerIds[next];
            _polygon.Clear();
            _polygon.Add(outlineIds[i]);
            _polygon.Add(outlineIds[next]);
            _polygon.Add(insetEnd);
            if (insetStart != insetEnd)
                _polygon.Add(insetStart);

            AddCapPolygon(Vector3.UnitZ, flip: false);
        }
    }

    /// <summary>Feeds one outline loop into <see cref="_skeleton"/> with its inward edge normals.</summary>
    private void BuildLoopSkeleton(List<Vector2> loop)
    {
        _skeleton.Clear();
        var count = loop.Count;
        for (var i = 0; i < count; i++)
        {
            var current = loop[i];
            var previous = loop[i == 0 ? count - 1 : i - 1];
            var next = loop[i + 1 == count ? 0 : i + 1];
            var position = new Vector3(current.X, current.Y, 0);
            var previousNormal = StraightSkeletonInset.EdgeInwardNormal(new Vector3(previous.X, previous.Y, 0), position, Vector3.UnitZ);
            var nextNormal = StraightSkeletonInset.EdgeInwardNormal(position, new Vector3(next.X, next.Y, 0), Vector3.UnitZ);
            _skeleton.Add(new StraightSkeletonInset.Vertex(position,
                                                           StraightSkeletonInset.MiterVelocity(previousNormal, nextNormal),
                                                           previousNormal,
                                                           nextNormal,
                                                           i == 0 ? count - 1 : i - 1,
                                                           i + 1 == count ? 0 : i + 1));
        }
    }

    /// <summary>
    /// Smallest distance between outlines of different loops, so an inset can be capped before holes grow
    /// through their outer outline. Returns infinity with fewer than two loops.
    /// </summary>
    private static float MinDistanceBetweenLoops(List<List<Vector2>> loops)
    {
        if (loops.Count < 2)
            return float.MaxValue;

        var bounds = new (Vector2 Min, Vector2 Max)[loops.Count];
        for (var l = 0; l < loops.Count; l++)
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            foreach (var p in loops[l])
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            bounds[l] = (min, max);
        }

        var bestSq = float.MaxValue;
        for (var a = 0; a < loops.Count; a++)
        {
            for (var b = a + 1; b < loops.Count; b++)
            {
                if (BoundsGapSquared(bounds[a], bounds[b]) >= bestSq)
                    continue;

                var first = loops[a];
                var second = loops[b];
                for (var i = 0; i < first.Count; i++)
                {
                    var a0 = first[i];
                    var a1 = first[i + 1 == first.Count ? 0 : i + 1];
                    for (var j = 0; j < second.Count; j++)
                    {
                        var b0 = second[j];
                        var b1 = second[j + 1 == second.Count ? 0 : j + 1];
                        if (SegmentBoundsGapSquared(a0, a1, b0, b1) >= bestSq)
                            continue;

                        var distanceSq = SegmentDistanceSquared(a0, a1, b0, b1);
                        if (distanceSq < bestSq)
                            bestSq = distanceSq;
                    }
                }
            }
        }

        return bestSq == float.MaxValue ? float.MaxValue : MathF.Sqrt(bestSq);
    }

    private static float BoundsGapSquared((Vector2 Min, Vector2 Max) a, (Vector2 Min, Vector2 Max) b)
    {
        var dx = MathF.Max(0, MathF.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        var dy = MathF.Max(0, MathF.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        return dx * dx + dy * dy;
    }

    private static float SegmentBoundsGapSquared(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        var aMin = Vector2.Min(a0, a1);
        var aMax = Vector2.Max(a0, a1);
        var bMin = Vector2.Min(b0, b1);
        var bMax = Vector2.Max(b0, b1);
        return BoundsGapSquared((aMin, aMax), (bMin, bMax));
    }

    /** Squared distance between two segments, clamped to their ends. */
    private static float SegmentDistanceSquared(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector2.Dot(d1, d1);
        var e = Vector2.Dot(d2, d2);
        var f = Vector2.Dot(d2, r);
        float s, t;
        if (a <= 1e-12f && e <= 1e-12f)
            return Vector2.DistanceSquared(p1, p2);

        if (a <= 1e-12f)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            var c = Vector2.Dot(d1, r);
            if (e <= 1e-12f)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                var b = Vector2.Dot(d1, d2);
                var denominator = a * e - b * b;
                s = denominator > 1e-12f ? Math.Clamp((b * f - c * e) / denominator, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }

        return Vector2.DistanceSquared(p1 + d1 * s, p2 + d2 * t);
    }

    private void ResolveOverlaps(WindingRule windingRule)
    {
        var tess = new Tess();
        foreach (var loop in _loops)
        {
            var contour = new ContourVertex[loop.Count];
            for (var i = 0; i < loop.Count; i++)
            {
                contour[i] = new ContourVertex(new Vec3(loop[i].X, loop[i].Y, 0));
            }

            tess.AddContour(contour, ContourOrientation.Original);
        }

        tess.Tessellate(windingRule, ElementType.BoundaryContours, 0, null, new Vec3(0, 0, 1));
        _loops.Clear();
        var vertices = tess.Vertices;
        var elements = tess.Elements;
        for (var e = 0; e < tess.ElementCount; e++)
        {
            var start = elements[e * 2];
            var count = elements[e * 2 + 1];
            if (count < 3)
                continue;

            var loop = new List<Vector2>(count);
            for (var i = 0; i < count; i++)
            {
                var v = vertices[start + i].Position;
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

    /// <summary>Orients each loop so the solid is on its left: nesting depth even = outer (CCW), odd = hole (CW).</summary>
    private void OrientLoops()
    {
        for (var i = 0; i < _loops.Count; i++)
        {
            var loop = _loops[i];
            var probe = loop[0];
            var depth = 0;
            for (var j = 0; j < _loops.Count; j++)
            {
                if (j != i && Contains(_loops[j], probe))
                    depth++;
            }

            var ccw = SignedArea(loop) > 0;
            var wantCcw = depth % 2 == 0;
            if (ccw != wantCcw)
                loop.Reverse();
        }
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

    /// <summary>Loop vertex moved inwards (to the left of the travel direction) by the inset, mitered with a clamp.</summary>
    private static Vector2 InsetPoint(List<Vector2> loop, int index, float inset)
    {
        if (inset == 0)
            return loop[index];

        var count = loop.Count;
        var previous = loop[(index - 1 + count) % count];
        var current = loop[index];
        var next = loop[(index + 1) % count];
        var d0 = Vector2.Normalize(current - previous);
        var d1 = Vector2.Normalize(next - current);
        var n0 = new Vector2(-d0.Y, d0.X); // left normals point into the solid
        var n1 = new Vector2(-d1.Y, d1.X);
        var bisector = n0 + n1;
        var lengthSq = bisector.LengthSquared();
        if (lengthSq < 1e-8f)
            return current + n0 * inset;

        // Miter length grows at sharp corners; clamp to twice the inset to avoid spikes
        var miter = bisector / lengthSq * 2f;
        if (miter.LengthSquared() > 4f)
            miter = Vector2.Normalize(miter) * 2f;

        return current + miter * inset;
    }

    /// <summary>One point per loop vertex per profile ring; loop l, ring r, vertex i at _loopRings[l][r * count + i].</summary>
    private void BuildRings(List<(float Inset, float Z)> profile)
    {
        _loopRings.Clear();
        _ringPointLookup.Clear();

        // Inset strips already placed their inner ring points; reuse them so the cap shares the band's edge
        foreach (var (position, point) in _ringZeroSeed)
            _ringPointLookup[(0, (long)MathF.Round(position.X * 1e6f), (long)MathF.Round(position.Y * 1e6f))] = point;

        foreach (var loop in _loops)
        {
            var count = loop.Count;
            var ids = new int[profile.Count * count];
            for (var r = 0; r < profile.Count; r++)
            {
                var (inset, z) = profile[r];
                for (var i = 0; i < count; i++)
                {
                    var p = InsetPoint(loop, i, inset);
                    // Loops that touch at a vertex (even-odd, resolved overlaps) must share that point:
                    // the tessellator merges coincident vertices and would otherwise pick one loop's id
                    var key = (r, (long)MathF.Round(p.X * 1e6f), (long)MathF.Round(p.Y * 1e6f));
                    if (!_ringPointLookup.TryGetValue(key, out var pointId))
                    {
                        pointId = AddPoint(new Vector3(p.X, p.Y, z));
                        _ringPointLookup[key] = pointId;
                    }

                    ids[r * count + i] = pointId;
                }
            }

            _loopRings.Add(ids);
        }
    }

    private void AddCap(int ringIndex, WindingRule windingRule, bool flip)
    {
        if (_usesLattice && TryAddLatticeCap(ringIndex, flip))
            return;

        var tess = new Tess();
        for (var l = 0; l < _loops.Count; l++)
        {
            var loop = _loops[l];
            var ids = _loopRings[l];
            var contour = new ContourVertex[loop.Count];
            for (var i = 0; i < loop.Count; i++)
            {
                var pointId = ids[ringIndex * loop.Count + i];
                var p = _positions[pointId];
                contour[i] = new ContourVertex(new Vec3(p.X, p.Y, p.Z), pointId);
            }

            tess.AddContour(contour, ContourOrientation.Original);
        }

        tess.Tessellate(windingRule, ElementType.Polygons, 3, null, new Vec3(0, 0, 1));

        var vertices = tess.Vertices;
        var elements = tess.Elements;
        _capTriangles.Clear();
        for (var t = 0; t < tess.ElementCount; t++)
        {
            var i0 = elements[t * 3];
            var i1 = elements[t * 3 + 1];
            var i2 = elements[t * 3 + 2];
            if (i0 == Tess.Undef || i1 == Tess.Undef || i2 == Tess.Undef)
                continue;

            var p0 = TessPoint(vertices[i0]);
            var p1 = TessPoint(vertices[i1]);
            var p2 = TessPoint(vertices[i2]);
            _capTriangles.Add(CounterClockwise(p0, p1, p2));
        }

        RemoveSlivers();

        // Front cap counter-clockwise seen from +Z, back cap the other way
        var normal = flip ? -Vector3.UnitZ : Vector3.UnitZ;
        foreach (var (p0, p1, p2) in _capTriangles)
        {
            if (flip)
                AddFace(p0, p2, p1, normal, isSide: false);
            else
                AddFace(p0, p1, p2, normal, isSide: false);
        }
    }

    /// <summary>
    /// The tessellator leaves zero-area triangles where three outline points lie on one line (e.g. along a letter stem
    /// where its arms join). They are harmless when drawn but fold over as soon as the cap is subdivided and smoothed,
    /// so each one has its long edge flipped with the triangle across it, which splits that triangle at the middle
    /// point. A sliver on the outline has nothing across and is dropped; the outline still runs through its middle point.
    /// </summary>
    private void RemoveSlivers()
    {
        for (var pass = 0; pass < 4 * _capTriangles.Count + 4; pass++)
        {
            var sliver = -1;
            for (var t = 0; t < _capTriangles.Count; t++)
            {
                if (!IsSliver(_capTriangles[t]))
                    continue;

                sliver = t;
                break;
            }

            if (sliver < 0)
                return;

            // Rotate so that (u, v) is the longest edge and w the point on it
            var (u, v, w) = _capTriangles[sliver];
            var lengthUv = Vector3.DistanceSquared(_positions[u], _positions[v]);
            var lengthVw = Vector3.DistanceSquared(_positions[v], _positions[w]);
            var lengthWu = Vector3.DistanceSquared(_positions[w], _positions[u]);
            if (lengthVw >= lengthUv && lengthVw >= lengthWu)
                (u, v, w) = (v, w, u);
            else if (lengthWu >= lengthUv && lengthWu >= lengthVw)
                (u, v, w) = (w, u, v);

            // A sliver's winding is arbitrary, so the triangle across may share the edge in either direction
            var across = -1;
            var x = -1;
            for (var t = 0; t < _capTriangles.Count && across < 0; t++)
            {
                if (t == sliver)
                    continue;

                var (a, b, c) = _capTriangles[t];
                if ((a == u || b == u || c == u) && (a == v || b == v || c == v))
                {
                    across = t;
                    x = a != u && a != v ? a : b != u && b != v ? b : c;
                }
            }

            if (across < 0)
            {
                _capTriangles.RemoveAt(sliver);
                continue;
            }

            // The quad u, x, v, w gets the other diagonal; a result that is still a sliver is handled in a later pass
            _capTriangles[sliver] = CounterClockwise(u, x, w);
            _capTriangles[across] = CounterClockwise(x, v, w);
        }
    }

    private bool IsSliver((int A, int B, int C) triangle)
    {
        var a = _positions[triangle.A];
        var b = _positions[triangle.B];
        var c = _positions[triangle.C];
        var longest = MathF.Max(Vector3.DistanceSquared(a, b), MathF.Max(Vector3.DistanceSquared(b, c), Vector3.DistanceSquared(c, a)));
        return MathF.Abs(Cross(triangle.A, triangle.B, triangle.C)) <= 1e-6f * longest;
    }

    private (int, int, int) CounterClockwise(int p0, int p1, int p2) => Cross(p0, p1, p2) < 0 ? (p0, p2, p1) : (p0, p1, p2);

    /** Twice the signed area of the triangle in the XY plane. */
    private float Cross(int p0, int p1, int p2)
    {
        var a = _positions[p0];
        var b = _positions[p1];
        var c = _positions[p2];
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private readonly List<(int A, int B, int C)> _capTriangles = [];

    /// <summary>Splits every loop segment where it crosses a grid line, unless the crossing is within <paramref name="snap"/> of a point already there.</summary>
    private void InsertGridCrossings(float cell, float snap)
    {
        var snapSquared = snap * snap;
        for (var l = 0; l < _loops.Count; l++)
        {
            var loop = _loops[l];
            _crossingLoop.Clear();
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                _crossingLoop.Add(a);

                // Crossings with vertical and horizontal grid lines, by their position along the segment
                _crossings.Clear();
                AddCrossings(a.X, b.X, cell);
                AddCrossings(a.Y, b.Y, cell);
                _crossings.Sort();

                var last = a;
                foreach (var t in _crossings)
                {
                    var point = Vector2.Lerp(a, b, t);
                    if (Vector2.DistanceSquared(point, last) < snapSquared || Vector2.DistanceSquared(point, b) < snapSquared)
                        continue;

                    _crossingLoop.Add(point);
                    last = point;
                }
            }

            loop.Clear();
            loop.AddRange(_crossingLoop);
        }
    }

    /** Segment parameters where the coordinate passes a multiple of the cell size. */
    private void AddCrossings(float from, float to, float cell)
    {
        if (MathF.Abs(to - from) < 1e-9f)
            return;

        var min = MathF.Min(from, to);
        var max = MathF.Max(from, to);
        for (var line = MathF.Floor(min / cell) + 1; line * cell < max; line++)
            _crossings.Add((line * cell - from) / (to - from));
    }

    /// <summary>
    /// A cap made of the grid cells: cells fully inside become one quad, cells on the outline the part of the shape
    /// inside them (clipped by the tessellator). Where the cells meet the outline they use its ring points, and grid
    /// corners are shared between neighbouring cells, so the cap has no gaps and meets the walls.
    /// </summary>
    /// <returns>False when there would be too many cells; the caller falls back to the plain cap.</returns>
    private bool TryAddLatticeCap(int ringIndex, bool flip)
    {
        var cell = Lattice;
        var snap = cell * LatticeSnap;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var loop in _loops)
        {
            foreach (var p in loop)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }

        var cellMinX = (int)MathF.Floor(min.X / cell);
        var cellMinY = (int)MathF.Floor(min.Y / cell);
        var cellMaxX = (int)MathF.Floor(max.X / cell);
        var cellMaxY = (int)MathF.Floor(max.Y / cell);
        if ((long)(cellMaxX - cellMinX + 1) * (cellMaxY - cellMinY + 1) > MaxLatticeCells)
        {
            LatticeIssue = $"Lattice is ignored: more than {MaxLatticeCells} cells. Use a larger cell size.";
            return false;
        }

        var firstLoop = _loopRings.Count > 0 && _loops[0].Count > 0 ? _loopRings[0][ringIndex * _loops[0].Count] : -1;
        var z = firstLoop >= 0 ? _positions[firstLoop].Z : 0f;
        _capRingIndex = ringIndex;
        _capSnap = snap;

        _capPointLookup.Clear();
        var normal = flip ? -Vector3.UnitZ : Vector3.UnitZ;
        for (var cy = cellMinY; cy <= cellMaxY; cy++)
        {
            for (var cx = cellMinX; cx <= cellMaxX; cx++)
            {
                var cellMin = new Vector2(cx * cell, cy * cell);
                var cellMax = cellMin + new Vector2(cell);
                if (!CellTouchesOutline(cellMin, cellMax))
                {
                    if (IsInside((cellMin + cellMax) * 0.5f))
                    {
                        _polygon.Clear();
                        _polygon.Add(GetCapPoint(cellMin, z));
                        _polygon.Add(GetCapPoint(new Vector2(cellMax.X, cellMin.Y), z));
                        _polygon.Add(GetCapPoint(cellMax, z));
                        _polygon.Add(GetCapPoint(new Vector2(cellMin.X, cellMax.Y), z));
                        AddCapPolygon(normal, flip);
                    }

                    continue;
                }

                ClipCell(cellMin, cellMax, ringIndex, z, snap, normal, flip);
            }
        }

        return true;
    }

    /** The shape inside one cell: the loops and the cell's square, where both overlap (winding of 2 or more). */
    private void ClipCell(Vector2 cellMin, Vector2 cellMax, int ringIndex, float z, float snap, Vector3 normal, bool flip)
    {
        var tess = new Tess();
        for (var l = 0; l < _loops.Count; l++)
        {
            var loop = _loops[l];
            if (!BoundsOverlap(loop, cellMin, cellMax))
                continue;

            // Outline vertices carry their ring point, so every cell resolves them (and crossings next to them) alike
            var contour = new ContourVertex[loop.Count];
            for (var i = 0; i < loop.Count; i++)
                contour[i] = new ContourVertex(new Vec3(loop[i].X, loop[i].Y, 0), _loopRings[l][ringIndex * loop.Count + i]);

            tess.AddContour(contour, ContourOrientation.Original);
        }

        // Counter-clockwise like the outer loops (see OrientLoops), so inside both the winding adds up to 2
        tess.AddContour([
                            new ContourVertex(new Vec3(cellMin.X, cellMin.Y, 0)),
                            new ContourVertex(new Vec3(cellMax.X, cellMin.Y, 0)),
                            new ContourVertex(new Vec3(cellMax.X, cellMax.Y, 0)),
                            new ContourVertex(new Vec3(cellMin.X, cellMax.Y, 0)),
                        ], ContourOrientation.Original);
        _combineSnap = snap;
        tess.Tessellate(WindingRule.AbsGeqTwo, ElementType.Polygons, MaxCapPolygonSize, _combineCapVertex ??= CombineCapVertex, new Vec3(0, 0, 1));
        for (var element = 0; element < tess.ElementCount; element++)
        {
            _polygon.Clear();
            for (var i = 0; i < MaxCapPolygonSize; i++)
            {
                var index = tess.Elements[element * MaxCapPolygonSize + i];
                if (index == Tess.Undef)
                    break;

                var vertex = tess.Vertices[index];
                _polygon.Add(vertex.Data is int ringPoint ? ringPoint : GetCapPoint(new Vector2(vertex.Position.X, vertex.Position.Y), z));
            }

            AddCapPolygon(normal, flip);
        }
    }

    /// <summary>
    /// Where the cell's square crosses an outline segment: the segment's nearest ring point. Normally that is a
    /// crossing inserted into the outline within the snap distance; on an inset cap the crossing snaps to the
    /// nearest inset point, so the inset strip keeps its quads. Deciding by the segment's points rather than by
    /// distance alone keeps neighbouring cells from picking two different points that lie close together, which
    /// would make them overlap.
    /// </summary>
    private object? CombineCapVertex(Vec3 position, object[] data, float[] weights)
    {
        var best = -1;
        var bestDistance = _capBoundaryIsInset ? float.MaxValue : _combineSnap * _combineSnap;
        foreach (var entry in data)
        {
            if (entry is not int pointId)
                continue;

            var p = _positions[pointId];
            var distance = (p.X - position.X) * (p.X - position.X) + (p.Y - position.Y) * (p.Y - position.Y);
            if (distance > bestDistance || (distance == bestDistance && best >= 0 && pointId > best))
                continue;

            bestDistance = distance;
            best = pointId;
        }

        return best >= 0 ? best : null;
    }

    /// <summary>
    /// For a position on an outline segment: the segment's nearer ring point. Normally only within the snap
    /// distance (grid crossings were inserted into the outline or snapped onto such a point); on an inset cap the
    /// nearer inset point is always used, so the cap meets the strip without subdividing it. Depends only on the
    /// position, so all cells sharing a corner agree.
    /// </summary>
    private int FindRingPointOnOutline(Vector2 position)
    {
        var onLine = 1e-4f * Lattice;
        var best = -1;
        var bestDistance = _capBoundaryIsInset ? float.MaxValue : _capSnap * _capSnap;
        for (var l = 0; l < _loops.Count; l++)
        {
            var loop = _loops[l];
            var count = loop.Count;
            for (var i = 0; i < count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % count];
                if (position.X < MathF.Min(a.X, b.X) - onLine || position.X > MathF.Max(a.X, b.X) + onLine
                    || position.Y < MathF.Min(a.Y, b.Y) - onLine || position.Y > MathF.Max(a.Y, b.Y) + onLine)
                    continue;

                var segment = b - a;
                var lengthSquared = segment.LengthSquared();
                var u = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(position - a, segment) / lengthSquared, 0, 1) : 0;
                if (Vector2.DistanceSquared(a + segment * u, position) > onLine * onLine)
                    continue;

                var distanceA = Vector2.DistanceSquared(a, position);
                var distanceB = Vector2.DistanceSquared(b, position);
                var nearer = distanceA <= distanceB ? i : (i + 1) % count;
                var distance = MathF.Min(distanceA, distanceB);
                var pointId = _loopRings[l][_capRingIndex * count + nearer];
                if (distance > bestDistance || (distance == bestDistance && best >= 0 && pointId > best))
                    continue;

                bestDistance = distance;
                best = pointId;
            }
        }

        return best;
    }

    private int _capRingIndex;
    private float _capSnap;
    private CombineCallback? _combineCapVertex;
    private float _combineSnap;

    /// <summary>
    /// The cap point at a grid position, shared by all cells there. Corners are not welded onto nearby outline points:
    /// that would move them for the cells using them but not for a clipped neighbour following the outline, and the two
    /// would overlap. Crossings with the outline are resolved onto ring points by <see cref="CombineCapVertex"/>.
    /// </summary>
    private int GetCapPoint(Vector2 position, float z)
    {
        var key = ((long)MathF.Round(position.X * 1e5f), (long)MathF.Round(position.Y * 1e5f));
        if (_capPointLookup.TryGetValue(key, out var existing))
            return existing;

        // A corner lying on the outline (edges along grid lines) must be one of its ring points, or the cap would have a
        // point the wall lacks and open there
        var onOutline = FindRingPointOnOutline(position);
        if (onOutline >= 0)
        {
            _capPointLookup[key] = onOutline;
            return onOutline;
        }

        var added = AddPoint(new Vector3(position.X, position.Y, z));
        _capPointLookup[key] = added;
        if (_carriesAttributes)
            _interiorCapPoints.Add(added);
        return added;
    }

    /** Adds the collected polygon without repeated points, wound counter-clockwise from +Z for the front cap. */
    private void AddCapPolygon(Vector3 normal, bool flip)
    {
        for (var i = _polygon.Count - 1; i >= 0 && _polygon.Count > 0; i--)
        {
            var previous = _polygon[(i + _polygon.Count - 1) % _polygon.Count];
            if (_polygon[i] == previous)
                _polygon.RemoveAt(i);
        }

        if (_polygon.Count < 3)
            return;

        var area = 0f;
        for (var i = 0; i < _polygon.Count; i++)
        {
            var a = _positions[_polygon[i]];
            var b = _positions[_polygon[(i + 1) % _polygon.Count]];
            area += a.X * b.Y - b.X * a.Y;
        }

        if (MathF.Abs(area) < 1e-12f)
            return;

        if (area > 0 == flip)
            _polygon.Reverse();

        foreach (var point in _polygon)
        {
            _corners.Add(point);
            _normals.Add(normal);
        }

        _faceOffsets.Add(_corners.Count);
        _isSide.Add(0);
    }

    private bool CellTouchesOutline(Vector2 cellMin, Vector2 cellMax)
    {
        foreach (var loop in _loops)
        {
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                if (MathF.Max(a.X, b.X) >= cellMin.X && MathF.Min(a.X, b.X) <= cellMax.X
                    && MathF.Max(a.Y, b.Y) >= cellMin.Y && MathF.Min(a.Y, b.Y) <= cellMax.Y)
                    return true;
            }
        }

        return false;
    }

    /** Even-odd test over all loops; they no longer overlap after resolving, so this matches the fill. */
    private bool IsInside(Vector2 point)
    {
        var inside = false;
        foreach (var loop in _loops)
        {
            if (Contains(loop, point))
                inside = !inside;
        }

        return inside;
    }

    private static bool BoundsOverlap(List<Vector2> loop, Vector2 cellMin, Vector2 cellMax)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var p in loop)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        return max.X >= cellMin.X && min.X <= cellMax.X && max.Y >= cellMin.Y && min.Y <= cellMax.Y;
    }

    /// <summary>Tessellator vertices carry the point id they came from; new (Steiner) vertices are added on the fly.</summary>
    private int TessPoint(ContourVertex vertex)
    {
        if (vertex.Data is int pointId)
            return pointId;

        return AddPoint(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
    }

    private void AddWalls(List<(float Inset, float Z)> profile, bool flatShading, float smoothCos)
    {
        for (var l = 0; l < _loops.Count; l++)
        {
            var loop = _loops[l];
            var ids = _loopRings[l];
            var count = loop.Count;
            var ringCount = profile.Count;
            for (var r = 0; r < ringCount - 1; r++)
            {
                for (var i = 0; i < count; i++)
                {
                    var next = (i + 1) % count;
                    var a = ids[r * count + i];
                    var b = ids[r * count + next];
                    var c = ids[(r + 1) * count + next];
                    var d = ids[(r + 1) * count + i];

                    // The solid is on the left of the travel direction and z decreases along the
                    // profile, so the outward-facing winding is a -> d -> c -> b
                    var pa = _positions[a];
                    var pb = _positions[b];
                    var pd = _positions[d];
                    var normal = Vector3.Cross(pd - pa, pb - pa);
                    if (normal.LengthSquared() < 1e-14f)
                        continue;

                    normal = Vector3.Normalize(normal);
                    // Smooth shading follows the outline (hard at sharp corners) and the bevel profile
                    if (flatShading)
                    {
                        AddFace(a, d, c, b, normal, isSide: true);
                    }
                    else
                    {
                        AddQuadSmooth(a, d, c, b, loop, i, next, profile, r, ringCount, smoothCos);
                    }
                }
            }
        }
    }

    /// <summary>Quad a (ring r, i) -> d (ring r+1, i) -> c (ring r+1, next) -> b (ring r, next) with smooth normals from the profile slope.</summary>
    private void AddQuadSmooth(int a, int d, int c, int b, List<Vector2> loop, int i, int next, List<(float Inset, float Z)> profile, int r,
                               int ringCount, float smoothCos)
    {
        // Outline normals at both ends of segment i; a corner sharper than the smooth angle keeps the segment's own normal
        var startOutward = CornerOutward(loop, i, segment: i, smoothCos);
        var endOutward = CornerOutward(loop, next, segment: i, smoothCos);
        _corners.Add(a); _normals.Add(ProfileNormal(startOutward, profile, r, ringCount));
        _corners.Add(d); _normals.Add(ProfileNormal(startOutward, profile, r + 1, ringCount));
        _corners.Add(c); _normals.Add(ProfileNormal(endOutward, profile, r + 1, ringCount));
        _corners.Add(b); _normals.Add(ProfileNormal(endOutward, profile, r, ringCount));
        _faceOffsets.Add(_corners.Count);
        _isSide.Add(1);
    }

    /// <summary>Outward 2D normal of the outline at vertex index as seen from the given segment (index to index+1, or the one
    /// before). Averaged with the neighbour segment when the corner is flatter than the smooth angle, otherwise the segment's
    /// own normal so sharp corners shade hard.</summary>
    private static Vector2 CornerOutward(List<Vector2> loop, int index, int segment, float smoothCos)
    {
        var count = loop.Count;
        var previous = loop[(index - 1 + count) % count];
        var current = loop[index];
        var next = loop[(index + 1) % count];
        var d0 = Vector2.Normalize(current - previous);
        var d1 = Vector2.Normalize(next - current);
        var own = segment == index ? d1 : d0;
        var ownOutward = new Vector2(own.Y, -own.X);
        if (Vector2.Dot(d0, d1) < smoothCos)
            return ownOutward;

        var outward = Vector2.Normalize(new Vector2(d0.Y, -d0.X) + new Vector2(d1.Y, -d1.X));
        return float.IsNaN(outward.X) ? ownOutward : outward;
    }

    private static Vector3 ProfileNormal(Vector2 outward, List<(float Inset, float Z)> profile, int r, int ringCount)
    {
        // Slope of the profile around ring r: average of the neighbouring profile segments
        var before = Math.Max(r - 1, 0);
        var after = Math.Min(r + 1, ringCount - 1);
        var dInset = profile[after].Inset - profile[before].Inset;
        var dZ = profile[after].Z - profile[before].Z;
        // Tangent along the profile is (-dInset, dZ) in (outward, z); the normal is perpendicular, pointing outward/forward
        var tangent = Vector2.Normalize(new Vector2(-dInset, dZ));
        var normal2 = new Vector2(tangent.Y, -tangent.X);
        if (normal2.X < 0)
            normal2 = -normal2;

        return Vector3.Normalize(new Vector3(outward.X * normal2.X, outward.Y * normal2.X, normal2.Y));
    }

    private int AddPoint(Vector3 position)
    {
        _positions.Add(position);
        return _positions.Count - 1;
    }

    private void AddFace(int a, int b, int c, Vector3 normal, bool isSide)
    {
        _corners.Add(a); _normals.Add(normal);
        _corners.Add(b); _normals.Add(normal);
        _corners.Add(c); _normals.Add(normal);
        _faceOffsets.Add(_corners.Count);
        _isSide.Add(isSide ? 1 : 0);
    }

    private void AddFace(int a, int b, int c, int d, Vector3 normal, bool isSide)
    {
        _corners.Add(a); _normals.Add(normal);
        _corners.Add(b); _normals.Add(normal);
        _corners.Add(c); _normals.Add(normal);
        _corners.Add(d); _normals.Add(normal);
        _faceOffsets.Add(_corners.Count);
        _isSide.Add(isSide ? 1 : 0);
    }

    /** Grid crossings closer than this share of a cell to an existing point use that point. */
    private const float LatticeSnap = 0.1f;
    private const int MaxLatticeCells = 40000;
    private const int MaxCapPolygonSize = 16;

    /** An inset uses at most this share of the closest distance between two outlines. */
    private const float InsetOverlapSafety = 0.45f;

    /** Inset corners closer than this fraction of the width after the inset are welded. */
    private const float InsetCollapseWeldFactor = 1e-3f;

    private bool _usesLattice;
    private bool _capBoundaryIsInset;
    private readonly StraightSkeletonInset _skeleton = new();
    private readonly List<Vector2> _crossingLoop = [];
    private readonly List<float> _crossings = [];
    private readonly List<int> _polygon = [];
    private readonly Dictionary<(long, long), int> _capPointLookup = [];
    private readonly List<List<Vector2>> _loops = [];
    private readonly List<List<Vector2>> _innerLoops = [];
    private readonly List<List<int>> _innerAlignedIds = [];
    private readonly List<int> _innerLoopSources = [];
    private readonly List<(int[] OutlineIds, List<int> AlignedInnerIds)> _collapsedStrips = [];
    private readonly List<(Vector2 Position, int Point)> _ringZeroSeed = [];
    private readonly List<int> _innerLoopIds = [];
    private readonly List<int> _insetInnerIds = [];
    private int[] _insetSlotPointIds = [];
    private readonly List<Vector3> _scratch = [];
    private readonly List<Vector2> _scratch2 = [];
    private readonly List<(float Inset, float Z)> _profile = [];
    private readonly List<int[]> _loopRings = [];
    private readonly Dictionary<(int Ring, long X, long Y), int> _ringPointLookup = [];
    private readonly List<Vector3> _positions = [];
    private readonly List<int> _corners = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<int> _faceOffsets = [];
    private readonly List<float> _isSide = [];
    private readonly List<PrimitivePart> _parts = [];
    private readonly List<PrimitiveKinds> _kinds = [];
    private readonly List<bool> _closed = [];
    private readonly List<(int Point, Vector3 HandleIn, Vector3 HandleOut)> _curvePointHandles = [];
}

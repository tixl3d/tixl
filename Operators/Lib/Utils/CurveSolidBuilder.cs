#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
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
                         bool skipPartsWithPolygons = false)
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
                      flatShading: false, smoothAngle: 40, front: true, back: false, sides: false, windingRule, keepCurves, pivot, id, seed);
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

    public void Begin()
    {
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
    }

    public void Finish(Geometry target, Geometry source)
    {
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

        // Part attributes of the source (glyph indices etc.) apply 1:1 to the output parts and are shared as they are
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain == AttributeDomain.Part && attribute.Count == _parts.Count)
                target.Attributes.Add(attribute);
        }

        target.InvalidateTopologyCaches();
    }

    public void BuildPart(Geometry geometry, Geometry.CurveEnumerable curveViews, int primitiveStart, int primitiveEnd, float tolerance, float depth,
                          float bevel, int bevelSegments, float weight, bool flatShading, float smoothAngle, bool front, bool back, bool sides,
                          WindingRule windingRule, bool keepCurves, Vector3 pivot, int id, int seed)
    {
        var partStart = _faceOffsets.Count - 1;
        BuildFaces(geometry, curveViews, primitiveStart, primitiveEnd, tolerance, depth, bevel, bevelSegments, weight, flatShading, smoothAngle,
                   front, back, sides, windingRule);
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
                _curvePointHandles.Add((_positions.Count, curve.GetHandleInPosition(index) - position, curve.GetHandleOutPosition(index) - position));
                _corners.Add(_positions.Count);
                _normals.Add(Vector3.Zero);
                _positions.Add(position);
            }

            _faceOffsets.Add(_corners.Count);
            _isSide.Add(0);
            _kinds.Add(PrimitiveKinds.Curve);
            _closed.Add(curve.IsClosed);
        }
    }

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
                            WindingRule windingRule)
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

        var hasWalls = sides && depth > 0;
        var profile = BuildProfile(depth, bevel, bevelSegments, hasWalls);
        BuildRings(profile);

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

        var normal = flip ? -Vector3.UnitZ : Vector3.UnitZ;
        var vertices = tess.Vertices;
        var elements = tess.Elements;
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

            // Front cap counter-clockwise seen from +Z, back cap the other way
            var a = _positions[p0];
            var b = _positions[p1];
            var c = _positions[p2];
            var ccw = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 0;
            if (ccw == flip)
                (p1, p2) = (p2, p1);

            AddFace(p0, p1, p2, normal, isSide: false);
        }
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

    private readonly List<List<Vector2>> _loops = [];
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

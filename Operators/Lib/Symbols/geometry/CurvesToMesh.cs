#nullable enable
using System;
using System.Collections.Generic;
using LibTessDotNet;

namespace Lib.geometry;

/// <summary>
/// Fills closed curves into a mesh and optionally extrudes them: front cap, back cap
/// and side walls with a rounded or chamfered bevel, in one op because the bevel
/// needs the cap boundary and the walls to agree. One mesh part per curve part
/// (glyph), attributes of the curve parts carried over, so per-character selection
/// and coloring keep working after the fill.
/// </summary>
[Guid("b6e3f8d1-4a27-4c95-9d0e-7f2c5a1b8e63")]
[ExportDependencies("LibTessDotNet.dll")]
internal sealed class CurvesToMesh : Instance<CurvesToMesh>
{
    [Output(Guid = "1c9a5e7d-3b62-4f08-a4d1-8e6b0c2f7a95")]
    public readonly Slot<MeshGeometry?> Result = new();

    public CurvesToMesh()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var curves = Curves.GetValue(context);
        var depth = MathF.Max(Depth.GetValue(context), 0);
        var bevel = MathF.Max(Bevel.GetValue(context), 0);
        var bevelSegments = Math.Clamp(BevelSegments.GetValue(context), 1, 16);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-5f);
        var front = Front.GetValue(context);
        var back = Back.GetValue(context);
        var sides = Sides.GetValue(context);
        var windingRule = EvenOdd.GetValue(context) ? WindingRule.EvenOdd : WindingRule.NonZero;

        if (curves == null || curves.ContourCount == 0)
        {
            Result.Value = null;
            return;
        }

        // A bevel that doesn't fit into the depth is halved from both ends
        bevel = depth > 0 ? MathF.Min(bevel, depth * 0.5f) : 0;
        _builder.Begin();

        var parts = curves.Parts;
        var partCount = parts.Length > 0 ? parts.Length : 1;
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            var contourStart = parts.Length > 0 ? parts[partIndex].ContourStart : 0;
            var contourEnd = parts.Length > 0 ? contourStart + parts[partIndex].ContourCount : curves.ContourCount;
            var pivot = parts.Length > 0 ? parts[partIndex].Pivot : Vector3.Zero;
            var id = parts.Length > 0 ? parts[partIndex].Id : 0;
            var seed = parts.Length > 0 ? parts[partIndex].SeedIndex : 0;
            _builder.BuildPart(curves, contourStart, contourEnd, tolerance, depth, bevel, bevelSegments, front, back, sides, windingRule,
                               pivot, id, seed);
        }

        _builder.Finish(_output, curves);
        Result.Value = _output;
    }

    private readonly MeshBuilder _builder = new();
    private readonly MeshGeometry _output = new();

    /// <summary>
    /// Accumulates faces over all parts. Per part: contours are flattened and oriented
    /// so the solid lies on their left (outers counter-clockwise, holes clockwise, by
    /// nesting parity), inset by the bevel for the caps, tessellated with the fill
    /// rule, and connected by profile rings for bevel and wall faces.
    /// </summary>
    private sealed class MeshBuilder
    {
        public void Begin()
        {
            _positions.Clear();
            _corners.Clear();
            _normals.Clear();
            _faceOffsets.Clear();
            _faceOffsets.Add(0);
            _isSide.Clear();
            _parts.Clear();
        }

        public void Finish(MeshGeometry target, CurveGeometry source)
        {
            target.Positions = _positions.ToArray();
            target.CornerPointIndices = _corners.ToArray();
            target.FaceCornerOffsets = _faceOffsets.ToArray();
            target.Parts = _parts.ToArray();
            target.Attributes.Clear();

            var normals = target.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, _normals.Count);
            _normals.CopyTo(normals.Values);
            var isSide = target.Attributes.GetOrCreate<float>(GeometryAttributeNames.IsSide, AttributeDomain.Face, _isSide.Count);
            _isSide.CopyTo(isSide.Values);

            // Part attributes of the curves (glyph indices etc.) apply 1:1 to the mesh parts
            foreach (var attribute in source.Attributes)
            {
                if (attribute.Domain != AttributeDomain.Part || attribute.Count != _parts.Count)
                    continue;

                switch (attribute)
                {
                    case GeometryAttribute<float> a:
                        a.Values.CopyTo(target.Attributes.GetOrCreate<float>(a.Name, AttributeDomain.Part, a.Count).Values, 0);
                        break;
                    case GeometryAttribute<int> a:
                        a.Values.CopyTo(target.Attributes.GetOrCreate<int>(a.Name, AttributeDomain.Part, a.Count).Values, 0);
                        break;
                    case GeometryAttribute<Vector4> a:
                        a.Values.CopyTo(target.Attributes.GetOrCreate<Vector4>(a.Name, AttributeDomain.Part, a.Count).Values, 0);
                        break;
                }
            }

            target.InvalidateTopologyCaches();
        }

        public void BuildPart(CurveGeometry curves, int contourStart, int contourEnd, float tolerance, float depth, float bevel, int bevelSegments,
                              bool front, bool back, bool sides, WindingRule windingRule, Vector3 pivot, int id, int seed)
        {
            var faceStart = _faceOffsets.Count - 1;

            // Flatten the part's contours into 2D loops
            _loops.Clear();
            for (var contourIndex = contourStart; contourIndex < contourEnd; contourIndex++)
            {
                if (!curves.ContourClosed[contourIndex])
                    continue;

                var loop = new List<Vector2>();
                _scratch.Clear();
                curves.Flatten(contourIndex, tolerance, _scratch);
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

            var hasWalls = sides && depth > 0;
            var profile = BuildProfile(depth, bevel, bevelSegments, hasWalls);
            BuildRings(profile);

            // Caps and walls share the ring points, so the solid closes without a weld
            if (front)
                AddCap(ringIndex: 0, windingRule, flip: false);

            if (back && depth > 0)
                AddCap(ringIndex: profile.Count - 1, windingRule, flip: true);

            if (hasWalls)
                AddWalls(profile);

            var faceCount = _faceOffsets.Count - 1 - faceStart;
            if (faceCount > 0)
                _parts.Add(new GeometryPart(faceStart, faceCount, pivot, id, seed));
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
            if (inset <= 0)
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

        private void AddWalls(List<(float Inset, float Z)> profile)
        {
            for (var l = 0; l < _loops.Count; l++)
            {
                var loop = _loops[l];
                var ids = _loopRings[l];
                var count = loop.Count;
                var ringCount = profile.Count;
                for (var r = 0; r < ringCount - 1; r++)
                {
                    var isBevel = MathF.Abs(profile[r].Inset - profile[r + 1].Inset) > 1e-7f;
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
                        // Bevel rings share smooth normals across the profile; straight walls stay flat
                        if (isBevel)
                        {
                            AddQuadSmooth(a, d, c, b, loop, i, next, profile, r, ringCount);
                        }
                        else
                        {
                            AddFace(a, d, c, b, normal, isSide: true);
                        }
                    }
                }
            }
        }

        /// <summary>Quad a (ring r, i) -> d (ring r+1, i) -> c (ring r+1, next) -> b (ring r, next) with smooth normals from the profile slope.</summary>
        private void AddQuadSmooth(int a, int d, int c, int b, List<Vector2> loop, int i, int next, List<(float Inset, float Z)> profile, int r,
                                   int ringCount)
        {
            _corners.Add(a); _normals.Add(ProfileNormal(loop, i, profile, r, ringCount));
            _corners.Add(d); _normals.Add(ProfileNormal(loop, i, profile, r + 1, ringCount));
            _corners.Add(c); _normals.Add(ProfileNormal(loop, next, profile, r + 1, ringCount));
            _corners.Add(b); _normals.Add(ProfileNormal(loop, next, profile, r, ringCount));
            _faceOffsets.Add(_corners.Count);
            _isSide.Add(1);
        }

        private static Vector3 ProfileNormal(List<Vector2> loop, int index, List<(float Inset, float Z)> profile, int r, int ringCount)
        {
            var count = loop.Count;
            var previous = loop[(index - 1 + count) % count];
            var current = loop[index];
            var next = loop[(index + 1) % count];
            var d0 = Vector2.Normalize(current - previous);
            var d1 = Vector2.Normalize(next - current);
            var outward = Vector2.Normalize(new Vector2(d0.Y, -d0.X) + new Vector2(d1.Y, -d1.X));
            if (float.IsNaN(outward.X))
                outward = new Vector2(d0.Y, -d0.X);

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
        private readonly List<(float Inset, float Z)> _profile = [];
        private readonly List<int[]> _loopRings = [];
        private readonly Dictionary<(int Ring, long X, long Y), int> _ringPointLookup = [];
        private readonly List<Vector3> _positions = [];
        private readonly List<int> _corners = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<int> _faceOffsets = [];
        private readonly List<float> _isSide = [];
        private readonly List<GeometryPart> _parts = [];
    }

    [Input(Guid = "4a7e2c9f-6d15-4b83-a0e6-9c3f8b1d5e27")]
    public readonly InputSlot<CurveGeometry> Curves = new();

    [Input(Guid = "c1f5b8e3-2a94-4d67-8e0b-7d3a9f6c2b14")]
    public readonly InputSlot<float> Depth = new();

    [Input(Guid = "9e3a7d1c-5b48-4f26-b9c0-1a6e4d8f3c72")]
    public readonly InputSlot<float> Bevel = new();

    [Input(Guid = "2b8d5f7a-e319-4c60-a5d8-6f0c3e9a1b48")]
    public readonly InputSlot<int> BevelSegments = new();

    [Input(Guid = "7d4c1a9e-8f52-4b37-9e6a-3c1b5d7f2a80")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "e5b2d8c4-1a76-4f93-b0d5-8c4e7a2f9b16")]
    public readonly InputSlot<bool> Front = new();

    [Input(Guid = "3f9c6e2b-d485-4a10-8b7e-5d2a1c8f4e63")]
    public readonly InputSlot<bool> Back = new();

    [Input(Guid = "a8e1c4f7-2b93-4d58-9a6c-0e5d3b7f1c29")]
    public readonly InputSlot<bool> Sides = new();

    [Input(Guid = "6c2f9b5e-7d31-4a84-b1e0-4f8c2a6d9e75")]
    public readonly InputSlot<bool> EvenOdd = new();
}

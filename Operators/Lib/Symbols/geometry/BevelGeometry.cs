using System;
using System.Collections.Generic;

namespace Lib.geometry;

/// <summary>
/// Uniform-width edge bevel for MeshGeometry (v1): every face is inset by the bevel
/// width, every edge becomes a profile strip between its two inset faces, and every
/// vertex becomes a fan patch closing the corner. Width is clamped against the
/// shortest edge. General miters/colliding bevels are out of scope.
/// </summary>
[Guid("dbaeebea-8d46-416f-9624-867af10d9c07")]
internal sealed class BevelGeometry : Instance<BevelGeometry>
{
    [Output(Guid = "48342188-0139-4ea4-943d-a5b5d59b7a4a")]
    public readonly Slot<MeshGeometry> Result = new();

    public BevelGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var width = Width.GetValue(context);
        var segments = Math.Clamp(Segments.GetValue(context), 1, 16);
        var roundness = Math.Clamp(Roundness.GetValue(context), 0f, 1f);
        var flatShading = FlatShading.GetValue(context);
        var roundCorners = RoundCorners.GetValue(context);

        if (source == null || source.FaceCount == 0)
        {
            Result.Value = source;
            return;
        }

        // Clamp against the shortest edge so opposite bevels can't cross
        var edges = source.Edges;
        var maxWidth = float.MaxValue;
        foreach (var edge in edges.Edges)
        {
            var length = Vector3.Distance(source.Positions[edge.PointA], source.Positions[edge.PointB]);
            maxWidth = MathF.Min(maxWidth, length * 0.35f);
        }

        width = MathF.Min(width, maxWidth);
        if (width <= 0)
        {
            Result.Value = source;
            return;
        }

        Build(source, width, segments, roundness, flatShading, roundCorners);
        Result.Value = _output;
    }

    private void Build(MeshGeometry source, float width, int segments, float roundness, bool flatShading, bool roundCorners)
    {
        var positions = source.Positions;
        var offsets = source.FaceCornerOffsets;
        var cornerPoints = source.CornerPointIndices;
        var edges = source.Edges;

        _outPositions.Clear();
        _outNormals.Clear();
        _outFaceOffsets.Clear();
        _outCorners.Clear();
        _outFaceOffsets.Add(0);

        // Source face normals (Newell), reused for inset points and strip blending
        if (_faceNormals.Length != source.FaceCount)
            _faceNormals = new Vector3[source.FaceCount];
        for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
        {
            var normal = Vector3.Zero;
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var p0 = positions[cornerPoints[c]];
                var p1 = positions[cornerPoints[next]];
                normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                      (p0.Z - p1.Z) * (p0.X + p1.X),
                                      (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            _faceNormals[faceIndex] = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitY;
        }

        // --- A: inset point per input corner --------------------------------
        var insetPointIds = _insetPointIds;
        if (insetPointIds.Length != source.CornerCount)
            insetPointIds = _insetPointIds = new int[source.CornerCount];

        for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var prev = c == start ? end - 1 : c - 1;
                var p = positions[cornerPoints[c]];
                var toNext = Vector3.Normalize(positions[cornerPoints[next]] - p);
                var toPrev = Vector3.Normalize(positions[cornerPoints[prev]] - p);

                var cosAngle = Math.Clamp(Vector3.Dot(toNext, toPrev), -1f, 1f);
                var sinHalf = MathF.Sqrt(MathF.Max((1 - cosAngle) * 0.5f, 1e-6f));
                var bisector = toNext + toPrev;
                var bisectorLength = bisector.Length();
                var inward = bisectorLength > 1e-6f ? bisector / bisectorLength : Vector3.Zero;

                insetPointIds[c] = AddPoint(p + inward * (width / sinHalf), _faceNormals[faceIndex]);
            }
        }

        // --- B: inset faces (same winding as the source) --------------------
        for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var c = start; c < end; c++)
            {
                _outCorners.Add(insetPointIds[c]);
            }

            _outFaceOffsets.Add(_outCorners.Count);
        }

        // --- C: edge strips --------------------------------------------------
        // ring point ids per edge and end vertex, ordered from Face0's inset to Face1's
        var edgeRings = new Dictionary<(int EdgeIndex, int PointId), int[]>();

        for (var edgeIndex = 0; edgeIndex < edges.Edges.Length; edgeIndex++)
        {
            var edge = edges.Edges[edgeIndex];
            if (edge.Face1 < 0)
                continue; // boundary edge - nothing to connect

            var ringA = BuildEndRing(source, edge, edge.PointA, segments, roundness, edgeRings, edgeIndex);
            var ringB = BuildEndRing(source, edge, edge.PointB, segments, roundness, edgeRings, edgeIndex);

            // Face0 traverses the edge either A->B or B->A; the strip must run opposite
            // to Face0's inset edge to face outward.
            var startRing = TraversesInOrder(source, edge.Face0, edge.PointA, edge.PointB) ? ringA : ringB;
            var endRing = startRing == ringA ? ringB : ringA;

            for (var s = 0; s < segments; s++)
            {
                AddQuad(endRing[s], startRing[s], startRing[s + 1], endRing[s + 1]);
            }
        }

        // --- D: corner fans --------------------------------------------------
        BuildCornerFans(source, edgeRings, segments, roundness, roundCorners);

        // --- E: publish ------------------------------------------------------
        _output.Positions = _outPositions.ToArray();
        _output.FaceCornerOffsets = _outFaceOffsets.ToArray();
        _output.CornerPointIndices = _outCorners.ToArray();
        _output.Parts = [];
        _output.Attributes.Clear();
        if (!flatShading)
        {
            // Without the attribute, the compile step falls back to per-face normals - the hard/faceted look.
            var normals = _output.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, _outCorners.Count);
            for (var c = 0; c < _outCorners.Count; c++)
            {
                normals.Values[c] = _outNormals[_outCorners[c]];
            }
        }
        _output.InvalidateTopologyCaches();
    }

    /// <summary>Points along the bevel profile at one edge end, from Face0's inset point to Face1's.</summary>
    private int[] BuildEndRing(MeshGeometry source, EdgeTopology.Edge edge, int endPointId,
                               int segments, float roundness,
                               Dictionary<(int, int), int[]> edgeRings, int edgeIndex)
    {
        var key = (edgeIndex, endPointId);
        if (edgeRings.TryGetValue(key, out var existing))
            return existing;

        var q0Id = _insetPointIds[FindCornerInFace(source, edge.Face0, endPointId)];
        var q1Id = _insetPointIds[FindCornerInFace(source, edge.Face1, endPointId)];
        var q0 = _outPositions[q0Id];
        var q1 = _outPositions[q1Id];

        // Control point: the original edge, inset along its own direction - the profile
        // bends around it. Projection of the midpoint onto the edge line.
        var edgeStart = source.Positions[edge.PointA];
        var edgeDir = source.Positions[edge.PointB] - edgeStart;
        var edgeLengthSq = edgeDir.LengthSquared();
        var mid = (q0 + q1) * 0.5f;
        var t = edgeLengthSq > 1e-10f ? Vector3.Dot(mid - edgeStart, edgeDir) / edgeLengthSq : 0;
        var control = edgeStart + edgeDir * t;

        var n0 = _faceNormals[edge.Face0];
        var n1 = _faceNormals[edge.Face1];
        var ring = new int[segments + 1];
        ring[0] = q0Id;
        ring[segments] = q1Id;
        for (var s = 1; s < segments; s++)
        {
            var f = (float)s / segments;
            var straight = Vector3.Lerp(q0, q1, f);
            var curvedA = Vector3.Lerp(q0, control, f);
            var curvedB = Vector3.Lerp(control, q1, f);
            var curved = Vector3.Lerp(curvedA, curvedB, f);
            var blended = Vector3.Lerp(n0, n1, f);
            var normal = blended.LengthSquared() > 1e-10f ? Vector3.Normalize(blended) : n0;
            ring[s] = AddPoint(Vector3.Lerp(straight, curved, roundness), normal);
        }

        edgeRings.Add(key, ring);
        return ring;
    }

    /// <summary>One fan patch per source point, closing the hole between the adjacent edge rings.</summary>
    private void BuildCornerFans(MeshGeometry source, Dictionary<(int, int), int[]> edgeRings, int segments, float roundness, bool roundCorners)
    {
        var edges = source.Edges;

        // adjacency: point -> adjacent interior edges
        var pointEdges = new Dictionary<int, List<int>>();
        for (var edgeIndex = 0; edgeIndex < edges.Edges.Length; edgeIndex++)
        {
            var edge = edges.Edges[edgeIndex];
            if (edge.Face1 < 0)
                continue;

            (pointEdges.TryGetValue(edge.PointA, out var listA) ? listA : pointEdges[edge.PointA] = []).Add(edgeIndex);
            (pointEdges.TryGetValue(edge.PointB, out var listB) ? listB : pointEdges[edge.PointB] = []).Add(edgeIndex);
        }

        var loop = new List<int>();
        foreach (var (pointId, adjacentEdges) in pointEdges)
        {
            if (adjacentEdges.Count < 3)
                continue;

            // Walk the fan of faces around the vertex: edge -> its arc -> the far face -> that face's other edge at the vertex.
            loop.Clear();
            var currentEdgeIndex = adjacentEdges[0];
            var currentFace = edges.Edges[currentEdgeIndex].Face0;
            var valid = true;
            for (var i = 0; i < adjacentEdges.Count; i++)
            {
                var edge = edges.Edges[currentEdgeIndex];
                var ring = edgeRings[(currentEdgeIndex, pointId)];

                // The arc runs Face0 -> Face1; append it so we leave on the far side of currentFace.
                var forward = edge.Face0 == currentFace;
                for (var s = 0; s < segments; s++)
                {
                    loop.Add(forward ? ring[s] : ring[segments - s]);
                }

                var nextFace = forward ? edge.Face1 : edge.Face0;
                if (!TryFindOtherEdgeAtVertex(source, nextFace, pointId, currentEdgeIndex, out currentEdgeIndex))
                {
                    valid = false;
                    break;
                }

                currentFace = nextFace;
            }

            if (!valid || loop.Count < 3)
                continue;

            // Fan center: the loop centroid pulled back toward the original vertex, so the
            // patch bulges outward (the centroid itself lies inside the rounded corner).
            var vertex = source.Positions[pointId];
            var centroid = Vector3.Zero;
            foreach (var id in loop)
            {
                centroid += _outPositions[id];
            }

            centroid /= loop.Count;

            // The face walk can go either way around the vertex - orient the loop so its
            // polygon normal (Newell) points outward, along vertex-minus-centroid.
            var loopNormal = Vector3.Zero;
            for (var i = 0; i < loop.Count; i++)
            {
                var p0 = _outPositions[loop[i]];
                var p1 = _outPositions[loop[(i + 1) % loop.Count]];
                loopNormal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                          (p0.Z - p1.Z) * (p0.X + p1.X),
                                          (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            if (Vector3.Dot(loopNormal, vertex - centroid) < 0)
                loop.Reverse();

            var outwardDir = vertex - centroid;
            var centerNormal = outwardDir.LengthSquared() > 1e-10f ? Vector3.Normalize(outwardDir) : Vector3.UnitY;

            if (!roundCorners || segments == 1)
            {
                var bulge = 1f - 0.25f * roundness;
                var centerId = AddPoint(vertex + (centroid - vertex) * bulge, centerNormal);

                for (var i = 0; i < loop.Count; i++)
                {
                    var next = (i + 1) % loop.Count;
                    AddTriangle(loop[i], loop[next], centerId);
                }

                continue;
            }

            BuildCornerRings(loop, vertex, centerNormal, segments, roundness);
        }
    }

    /// <summary>
    /// Closes a corner with concentric rings carrying the edge profile through the
    /// corner, instead of a single-point fan. The rings are pulled onto a sphere
    /// fitted through the loop points (blended in by roundness), so a fully round
    /// bevel gets the classic rounded-cube corner.
    /// </summary>
    private void BuildCornerRings(List<int> loop, Vector3 vertex, Vector3 outwardNormal, int segments, float roundness)
    {
        // Fit a sphere with its center on the inward axis through the vertex:
        // C = vertex - outwardNormal * d, least-squares over the loop points.
        var sumA = 0f;
        var sumB = 0f;
        var sumAb = 0f;
        var sumBb = 0f;
        foreach (var id in loop)
        {
            var delta = _outPositions[id] - vertex;
            var a = delta.LengthSquared();
            var b = -Vector3.Dot(outwardNormal, delta);
            sumA += a;
            sumB += b;
            sumAb += a * b;
            sumBb += b * b;
        }

        var n = loop.Count;
        var varB = sumBb - sumB * sumB / n;
        var d = varB > 1e-10f ? (sumAb - sumA * sumB / n) / (2f * varB) : 0f;
        var center = vertex - outwardNormal * d;

        var radius = 0f;
        foreach (var id in loop)
        {
            radius += Vector3.Distance(_outPositions[id], center);
        }

        radius /= n;

        // At extreme widths the loop is far from spherical and the fit can overshoot -
        // keep the sphere inside the original vertex so neither pole nor rings spike out.
        if (d > 0)
            radius = MathF.Min(radius, d);

        var pole = center + outwardNormal * radius;
        var poleId = AddPoint(Vector3.Lerp(vertex, pole, roundness), outwardNormal);

        // Rings between the loop (t=0) and the pole (t=1): planar shrink toward the
        // pole, pushed out to the sphere by roundness.
        var previousRing = new int[n];
        var currentRing = new int[n];
        loop.CopyTo(previousRing);
        var polePosition = _outPositions[poleId];

        for (var ringIndex = 1; ringIndex < segments; ringIndex++)
        {
            var t = (float)ringIndex / segments;
            for (var i = 0; i < n; i++)
            {
                var flat = Vector3.Lerp(_outPositions[loop[i]], polePosition, t);
                var fromCenter = flat - center;
                var length = fromCenter.Length();
                var sphereNormal = length > 1e-10f ? fromCenter / length : outwardNormal;
                var position = Vector3.Lerp(flat, center + sphereNormal * radius, roundness);
                var flatNormal = Vector3.Lerp(_outNormals[loop[i]], outwardNormal, t);
                if (flatNormal.LengthSquared() > 1e-10f)
                    flatNormal = Vector3.Normalize(flatNormal);
                var normal = Vector3.Lerp(flatNormal, sphereNormal, roundness);
                if (normal.LengthSquared() > 1e-10f)
                    normal = Vector3.Normalize(normal);
                currentRing[i] = AddPoint(position, normal);
            }

            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                AddQuad(previousRing[i], previousRing[next], currentRing[next], currentRing[i]);
            }

            (previousRing, currentRing) = (currentRing, previousRing);
        }

        for (var i = 0; i < n; i++)
        {
            var next = (i + 1) % n;
            AddTriangle(previousRing[i], previousRing[next], poleId);
        }
    }

    private static bool TraversesInOrder(MeshGeometry source, int faceIndex, int pointA, int pointB)
    {
        var start = source.FaceCornerOffsets[faceIndex];
        var end = source.FaceCornerOffsets[faceIndex + 1];
        for (var c = start; c < end; c++)
        {
            var next = c + 1 == end ? start : c + 1;
            if (source.CornerPointIndices[c] == pointA && source.CornerPointIndices[next] == pointB)
                return true;
        }

        return false;
    }

    private static int FindCornerInFace(MeshGeometry source, int faceIndex, int pointId)
    {
        var start = source.FaceCornerOffsets[faceIndex];
        var end = source.FaceCornerOffsets[faceIndex + 1];
        for (var c = start; c < end; c++)
        {
            if (source.CornerPointIndices[c] == pointId)
                return c;
        }

        return start;
    }

    /// <summary>Finds the face's second edge touching the vertex (each vertex has exactly two per face).</summary>
    private static bool TryFindOtherEdgeAtVertex(MeshGeometry source, int faceIndex, int pointId, int excludeEdgeIndex, out int edgeIndex)
    {
        var edges = source.Edges;
        var start = source.FaceCornerOffsets[faceIndex];
        var end = source.FaceCornerOffsets[faceIndex + 1];
        for (var c = start; c < end; c++)
        {
            var next = c + 1 == end ? start : c + 1;
            if (source.CornerPointIndices[c] != pointId && source.CornerPointIndices[next] != pointId)
                continue;

            var candidate = edges.CornerEdgeIndices[c];
            if (candidate == excludeEdgeIndex)
                continue;

            var edge = edges.Edges[candidate];
            if (edge.PointA != pointId && edge.PointB != pointId)
                continue;

            edgeIndex = candidate;
            return edge.Face1 >= 0;
        }

        edgeIndex = -1;
        return false;
    }

    private int AddPoint(Vector3 position, Vector3 normal)
    {
        _outPositions.Add(position);
        _outNormals.Add(normal);
        return _outPositions.Count - 1;
    }

    private void AddQuad(int a, int b, int c, int d)
    {
        _outCorners.Add(a);
        _outCorners.Add(b);
        _outCorners.Add(c);
        _outCorners.Add(d);
        _outFaceOffsets.Add(_outCorners.Count);
    }

    private void AddTriangle(int a, int b, int c)
    {
        _outCorners.Add(a);
        _outCorners.Add(b);
        _outCorners.Add(c);
        _outFaceOffsets.Add(_outCorners.Count);
    }

    private readonly MeshGeometry _output = new();
    private readonly List<Vector3> _outPositions = [];
    private readonly List<Vector3> _outNormals = [];
    private readonly List<int> _outFaceOffsets = [];
    private readonly List<int> _outCorners = [];
    private int[] _insetPointIds = [];
    private Vector3[] _faceNormals = [];

    [Input(Guid = "46a2c9e2-7b5d-45ce-9955-b1c0b1bb01db")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "ce40061f-b853-41be-bbef-0db172bbad7f")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "e9085eac-dc15-4ecf-862d-869e66981418")]
    public readonly InputSlot<int> Segments = new();

    [Input(Guid = "091c932c-a7bb-487e-97c5-093e9838a700")]
    public readonly InputSlot<float> Roundness = new();

    [Input(Guid = "c86a32c2-feeb-4c05-b2c0-cf54076247bb")]
    public readonly InputSlot<bool> FlatShading = new();

    [Input(Guid = "3f8b1c4d-92e6-4a17-b5d0-6a4c8e2f7d19")]
    public readonly InputSlot<bool> RoundCorners = new();
}

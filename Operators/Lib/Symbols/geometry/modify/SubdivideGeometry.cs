#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry.modify;

/// <summary>
/// Splits every polygon into quads around a new point per edge and per face. Smoothness blends the point positions
/// from linear (0, the shape is kept) to Catmull-Clark (1), scaled per point by an optional attribute; open boundaries
/// stay creases. Point and vertex attributes are averaged like the new points,
/// primitive attributes copy to the child quads, parts keep their ranges and curves pass through unchanged.
/// </summary>
[Guid("98d9ae64-3784-4141-a509-489ae5775bd6")]
internal sealed class SubdivideGeometry : Instance<SubdivideGeometry>, IStatusProvider, ICustomDropdownHolder
{
    private enum NormalModes
    {
        Auto,
        Interpolate,
        Smooth,
        Flat,
    }

    [Output(Guid = "6fcf5ce7-5841-45e9-aca3-33b9b85855fb")]
    public readonly Slot<Geometry?> Result = new();

    public SubdivideGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var smoothness = Smoothness.GetValue(context);
        var attributeName = SmoothnessAttribute.GetValue(context);
        var hasAttribute = !string.IsNullOrWhiteSpace(attributeName);
        var iterations = Iterations.GetValue(context).Clamp(0, MaxIterations);
        var normalMode = (NormalModes)Normals.GetValue(context).Clamp(0, 3);
        var levelName = LevelAttribute.GetValue(context);
        var maxEdgeLength = MaxEdgeLength.GetValue(context);
        var smooths = smoothness != 0;
        if (normalMode == NormalModes.Auto)
            normalMode = smooths ? NormalModes.Smooth : NormalModes.Interpolate;

        _statusMessage = string.Empty;
        _lastSource = source;
        if (source == null || iterations == 0)
        {
            Result.Value = source;
            return;
        }

        var current = source;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (!CollectSplits(current, iteration, iterations, string.IsNullOrWhiteSpace(levelName) ? null : levelName, maxEdgeLength))
                break;

            if (GetSubdividedVertexCount(current) > MaxVertexCount)
            {
                _statusMessage = $"Stopped after {iteration} iterations: more would exceed {MaxVertexCount / 1000000}M vertices.";
                break;
            }

            // Ping-pong between two outputs so each iteration reads the previous one
            var target = iteration % 2 == 0 ? _outputA : _outputB;
            var weights = smooths ? CollectSmoothness(current, smoothness, hasAttribute ? attributeName : null) : null;
            Subdivide(current, target, weights, normalMode != NormalModes.Interpolate);
            current = target;
        }

        if (normalMode == NormalModes.Smooth && !ReferenceEquals(current, source))
            WriteSmoothNormals(current, SmoothAngle.GetValue(context));

        Result.Value = current;
    }

    /// <summary>Smoothness per source point: the parameter times the attribute, re-read each iteration since it flows along.</summary>
    private float[] CollectSmoothness(Geometry geometry, float smoothness, string? attributeName)
    {
        var pointCount = geometry.PointCount;
        EnsureLength(ref _smoothness, pointCount);
        EnsureLength(ref _attributeScratch, pointCount);
        if (attributeName == null || !GeometryElements.TryGetWeights(geometry, AttributeDomain.Point, attributeName, _smoothness, _attributeScratch))
        {
            if (attributeName != null)
                _statusMessage = $"No float attribute '{attributeName}'; Smoothness applies everywhere.";

            Array.Fill(_smoothness, 1f, 0, pointCount);
        }

        for (var point = 0; point < pointCount; point++)
            _smoothness[point] *= smoothness;

        return _smoothness;
    }

    /// <summary>
    /// Which polygons split in this iteration. With LevelAttribute, a face splits while its value times Iterations
    /// exceeds the iteration (values flow to the child faces, so a face with 0.5 of 4 iterations splits twice). With
    /// MaxEdgeLength, only faces with a longer edge split. Without either, every polygon splits.
    /// </summary>
    /// <returns>False when no face splits, so further iterations would change nothing.</returns>
    private bool CollectSplits(Geometry geometry, int iteration, int iterations, string? levelName, float maxEdgeLength)
    {
        var primitiveCount = geometry.PrimitiveCount;
        EnsureLength(ref _splits, primitiveCount);
        var hasLevels = false;
        if (levelName != null)
        {
            EnsureLength(ref _levels, primitiveCount);
            EnsureLength(ref _attributeScratch, geometry.PointCount);
            hasLevels = GeometryElements.TryGetWeights(geometry, AttributeDomain.Primitive, levelName, _levels, _attributeScratch);
            if (!hasLevels)
                _statusMessage = $"No float attribute '{levelName}'; every face is subdivided.";
        }

        var maxEdgeLengthSquared = maxEdgeLength * maxEdgeLength;
        var anySplit = false;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var start = geometry.PrimitiveVertexOffsets[primitive];
            var vertexCount = geometry.PrimitiveVertexOffsets[primitive + 1] - start;
            var splits = geometry.PrimitiveKind[primitive] == PrimitiveKinds.Polygon && vertexCount >= 3;
            if (splits && hasLevels)
                splits = _levels[primitive] * iterations > iteration;

            if (splits && maxEdgeLength > 0)
                splits = GetLongestEdgeSquared(geometry, start, vertexCount) > maxEdgeLengthSquared;

            _splits[primitive] = splits;
            anySplit |= splits;
        }

        return anySplit;
    }

    private static float GetLongestEdgeSquared(Geometry geometry, int start, int vertexCount)
    {
        var longest = 0f;
        for (var i = 0; i < vertexCount; i++)
        {
            var a = geometry.Positions[geometry.VertexPoints[start + i]];
            var b = geometry.Positions[geometry.VertexPoints[start + (i + 1) % vertexCount]];
            longest = MathF.Max(longest, Vector3.DistanceSquared(a, b));
        }

        return longest;
    }

    private int GetSubdividedVertexCount(Geometry geometry)
    {
        var count = 0;
        for (var primitive = 0; primitive < geometry.PrimitiveCount; primitive++)
        {
            var vertexCount = geometry.PrimitiveVertexOffsets[primitive + 1] - geometry.PrimitiveVertexOffsets[primitive];
            count += _splits[primitive] ? vertexCount * 4 : vertexCount;
        }

        return count;
    }

    /// <summary>
    /// Output points: the source points first (so curves keep their indices), then one per edge of a splitting face,
    /// then one per splitting face. Each split N-gon becomes N quads: its vertex, the next edge point, the face point,
    /// the previous edge point. A face that doesn't split gets the edge points of split neighbours inserted into its
    /// outline, so the surface stays closed; those border edges count as open for the smoothing, which keeps the
    /// border points on the edge and the coarse face flat.
    /// </summary>
    /// <param name="smoothness">Per source point: 0 keeps the linear position, 1 the Catmull-Clark one; null is linear.</param>
    private void Subdivide(Geometry source, Geometry target, float[]? smoothness, bool dropNormals)
    {
        var positions = source.Positions;
        var offsets = source.PrimitiveVertexOffsets;
        var vertexPoints = source.VertexPoints;
        var topology = source.Edges;
        var edges = topology.Edges;
        var cornerEdges = topology.CornerEdgeIndices;
        var pointCount = source.PointCount;
        var edgeCount = edges.Length;
        var primitiveCount = source.PrimitiveCount;

        // How many splitting polygons use each edge; output primitive counts
        EnsureLength(ref _facePoints, primitiveCount);
        EnsureLength(ref _primitiveStarts, primitiveCount);
        EnsureLength(ref _edgeUse, edgeCount);
        EnsureLength(ref _edgePoints, edgeCount);
        Array.Clear(_edgeUse, 0, edgeCount);
        var outputPrimitiveCount = 0;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var start = offsets[primitive];
            var vertexCount = offsets[primitive + 1] - start;
            _primitiveStarts[primitive] = outputPrimitiveCount;
            if (!_splits[primitive])
            {
                outputPrimitiveCount++;
                continue;
            }

            outputPrimitiveCount += vertexCount;
            for (var vertex = start; vertex < start + vertexCount; vertex++)
                _edgeUse[cornerEdges[vertex]]++;
        }

        // Edge points only where a splitting face needs one, then the face points
        var nextPoint = pointCount;
        for (var edge = 0; edge < edgeCount; edge++)
            _edgePoints[edge] = _edgeUse[edge] > 0 ? nextPoint++ : -1;

        var outputVertexCount = 0;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var start = offsets[primitive];
            var vertexCount = offsets[primitive + 1] - start;
            if (_splits[primitive])
            {
                _facePoints[primitive] = nextPoint++;
                outputVertexCount += vertexCount * 4;
                continue;
            }

            _facePoints[primitive] = -1;
            outputVertexCount += vertexCount;
            for (var vertex = start; vertex < start + vertexCount; vertex++)
            {
                if (cornerEdges[vertex] >= 0 && _edgePoints[cornerEdges[vertex]] >= 0)
                    outputVertexCount++;
            }
        }

        var outputPointCount = nextPoint;
        if (target.Positions.Length != outputPointCount)
            target.Positions = new Vector3[outputPointCount];
        if (target.PrimitiveVertexOffsets.Length != outputPrimitiveCount + 1)
            target.PrimitiveVertexOffsets = new int[outputPrimitiveCount + 1];
        if (target.VertexPoints.Length != outputVertexCount)
            target.VertexPoints = new int[outputVertexCount];
        if (target.PrimitiveKind.Length != outputPrimitiveCount)
            target.PrimitiveKind = new PrimitiveKinds[outputPrimitiveCount];
        if (target.PrimitiveClosed.Length != outputPrimitiveCount)
            target.PrimitiveClosed = new bool[outputPrimitiveCount];

        var outputPositions = target.Positions;

        // Face points: the mean of their polygon
        _pointStencil.Clear();
        for (var point = 0; point < pointCount; point++)
            _pointStencil.AddCopy(point);

        for (var edge = 0; edge < edgeCount; edge++)
        {
            if (_edgePoints[edge] >= 0)
                _pointStencil.AddMidpoint(edges[edge].PointA, edges[edge].PointB);
        }

        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var facePoint = _facePoints[primitive];
            if (facePoint < 0)
                continue;

            var start = offsets[primitive];
            var end = offsets[primitive + 1];
            var weight = 1f / (end - start);
            var sum = Vector3.Zero;
            _pointStencil.Begin();
            for (var vertex = start; vertex < end; vertex++)
            {
                sum += positions[vertexPoints[vertex]];
                _pointStencil.Push(vertexPoints[vertex], weight);
            }

            outputPositions[facePoint] = sum * weight;
        }

        // Edge points: the midpoint, pulled toward both face points for smooth interior edges by the mean smoothness of its ends
        for (var edge = 0; edge < edgeCount; edge++)
        {
            var edgePoint = _edgePoints[edge];
            if (edgePoint < 0)
                continue;

            var e = edges[edge];
            var midpoint = (positions[e.PointA] + positions[e.PointB]) * 0.5f;
            if (smoothness == null || _edgeUse[edge] != 2 || e.Face1 < 0)
            {
                outputPositions[edgePoint] = midpoint;
                continue;
            }

            var smoothed = (midpoint * 2 + outputPositions[_facePoints[e.Face0]] + outputPositions[_facePoints[e.Face1]]) * 0.25f;
            var weight = (smoothness[e.PointA] + smoothness[e.PointB]) * 0.5f;
            outputPositions[edgePoint] = Vector3.Lerp(midpoint, smoothed, weight);
        }

        Array.Copy(positions, outputPositions, pointCount);
        if (smoothness != null)
            SmoothSourcePoints(source, outputPositions, smoothness);

        // Topology and the vertex and primitive stencils
        _vertexStencil.Clear();
        _primitiveStencil.Clear();
        var outputOffsets = target.PrimitiveVertexOffsets;
        var outputVertexPoints = target.VertexPoints;
        var outputPrimitive = 0;
        var outputVertex = 0;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var start = offsets[primitive];
            var end = offsets[primitive + 1];
            var vertexCount = end - start;
            var facePoint = _facePoints[primitive];
            if (facePoint < 0)
            {
                outputOffsets[outputPrimitive] = outputVertex;
                target.PrimitiveKind[outputPrimitive] = source.PrimitiveKind[primitive];
                target.PrimitiveClosed[outputPrimitive] = source.PrimitiveClosed[primitive];
                _primitiveStencil.AddCopy(primitive);
                outputPrimitive++;
                for (var vertex = start; vertex < end; vertex++)
                {
                    outputVertexPoints[outputVertex++] = vertexPoints[vertex];
                    _vertexStencil.AddCopy(vertex);

                    // A split neighbour's edge point joins the outline, so no gap opens along the shared edge
                    var edge = cornerEdges[vertex];
                    if (edge < 0 || _edgePoints[edge] < 0)
                        continue;

                    outputVertexPoints[outputVertex++] = _edgePoints[edge];
                    _vertexStencil.AddMidpoint(vertex, vertex + 1 == end ? start : vertex + 1);
                }

                continue;
            }

            var faceWeight = 1f / vertexCount;
            for (var i = 0; i < vertexCount; i++)
            {
                var vertex = start + i;
                var next = start + (i + 1) % vertexCount;
                var previous = start + (i + vertexCount - 1) % vertexCount;

                outputOffsets[outputPrimitive] = outputVertex;
                target.PrimitiveKind[outputPrimitive] = PrimitiveKinds.Polygon;
                target.PrimitiveClosed[outputPrimitive] = true;
                _primitiveStencil.AddCopy(primitive);
                outputPrimitive++;

                outputVertexPoints[outputVertex++] = vertexPoints[vertex];
                outputVertexPoints[outputVertex++] = _edgePoints[cornerEdges[vertex]];
                outputVertexPoints[outputVertex++] = facePoint;
                outputVertexPoints[outputVertex++] = _edgePoints[cornerEdges[previous]];

                _vertexStencil.AddCopy(vertex);
                _vertexStencil.AddMidpoint(vertex, next);
                _vertexStencil.Begin();
                for (var faceVertex = start; faceVertex < end; faceVertex++)
                    _vertexStencil.Push(faceVertex, faceWeight);

                _vertexStencil.AddMidpoint(previous, vertex);
            }
        }

        outputOffsets[outputPrimitiveCount] = outputVertexCount;
        if (smoothness != null)
            PreventFoldOvers(source, target);

        // Parts: primitive ranges become ranges of their children
        var parts = source.Parts;
        if (target.Parts.Length != parts.Length)
            target.Parts = new PrimitivePart[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var end = part.PrimitiveStart + part.PrimitiveCount;
            var outputStart = part.PrimitiveStart < primitiveCount ? _primitiveStarts[part.PrimitiveStart] : outputPrimitiveCount;
            var outputEnd = end < primitiveCount ? _primitiveStarts[end] : outputPrimitiveCount;
            target.Parts[index] = part with { PrimitiveStart = outputStart, PrimitiveCount = outputEnd - outputStart };
        }

        // Attributes: points and vertices averaged, primitives copied, parts shared; edges no longer match and are dropped
        target.Attributes.Clear();
        _pointStencil.Apply(source.Attributes, target.Attributes, AttributeDomain.Point);
        _vertexStencil.Apply(source.Attributes, target.Attributes, AttributeDomain.Vertex, dropNormals ? _normalNames : null);
        _primitiveStencil.Apply(source.Attributes, target.Attributes, AttributeDomain.Primitive);
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            if (source.Attributes[index].Domain == AttributeDomain.Part)
                target.Attributes.Add(source.Attributes[index]);
        }

        target.InvalidateTopologyCaches();
    }

    /// <summary>
    /// Smoothing coarse meshes with long thin faces (e.g. triangulated letter caps) can pull new points past each other,
    /// so faces turn over. Each output face is compared with its source face: where it points the other way although
    /// its linear version would not, its points are moved halfway back to their linear positions, repeated until no
    /// face is turned over, and finally set fully linear. Only the affected points lose smoothing.
    /// </summary>
    private void PreventFoldOvers(Geometry source, Geometry target)
    {
        var positions = source.Positions;
        var offsets = source.PrimitiveVertexOffsets;
        var vertexPoints = source.VertexPoints;
        var edges = source.Edges.Edges;
        var outputPositions = target.Positions;
        var outputOffsets = target.PrimitiveVertexOffsets;
        var outputVertexPoints = target.VertexPoints;
        var outputPointCount = outputPositions.Length;
        EnsureLength(ref _linearPositions, outputPointCount);
        EnsureLength(ref _foldedPoints, outputPointCount);

        // Linear positions: source points stay, edge points are midpoints, face points are not smoothed anyway
        Array.Copy(outputPositions, _linearPositions, outputPointCount);
        Array.Copy(positions, _linearPositions, source.PointCount);
        for (var edge = 0; edge < edges.Length; edge++)
        {
            if (_edgePoints[edge] >= 0)
                _linearPositions[_edgePoints[edge]] = (positions[edges[edge].PointA] + positions[edges[edge].PointB]) * 0.5f;
        }

        for (var round = 0; round < MaxFoldOverRounds; round++)
        {
            Array.Clear(_foldedPoints, 0, outputPointCount);
            var anyFolded = false;
            for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
            {
                var start = offsets[primitive];
                var vertexCount = offsets[primitive + 1] - start;
                if (source.PrimitiveKind[primitive] != PrimitiveKinds.Polygon || vertexCount < 3)
                    continue;

                var reference = Vector3.Zero;
                for (var i = 0; i < vertexCount; i++)
                {
                    var a = positions[vertexPoints[start + i]];
                    var b = positions[vertexPoints[start + (i + 1) % vertexCount]];
                    reference += Vector3.Cross(a, b);
                }

                var firstOutput = _primitiveStarts[primitive];
                var outputCount = _facePoints[primitive] >= 0 ? vertexCount : 1;
                for (var output = firstOutput; output < firstOutput + outputCount; output++)
                {
                    var outputStart = outputOffsets[output];
                    var outputEnd = outputOffsets[output + 1];
                    // Every corner must turn the way of the source face, so a face smoothing made concave is caught before the
                    // next iteration splits it into turned-over faces. Where even the linear face fails that (a concave
                    // source face), only a face turned over as a whole counts.
                    var linearIsConvex = TurnsConsistently(_linearPositions, outputVertexPoints, outputStart, outputEnd, reference);
                    var isFolded = linearIsConvex
                                       ? !TurnsConsistently(outputPositions, outputVertexPoints, outputStart, outputEnd, reference)
                                       : Vector3.Dot(GetNormal(outputPositions, outputVertexPoints, outputStart, outputEnd), reference) <= 0
                                         && Vector3.Dot(GetNormal(_linearPositions, outputVertexPoints, outputStart, outputEnd), reference) > 0;
                    if (!isFolded)
                        continue;

                    anyFolded = true;
                    for (var vertex = outputStart; vertex < outputEnd; vertex++)
                        _foldedPoints[outputVertexPoints[vertex]] = true;
                }
            }

            if (!anyFolded)
                return;

            var isLast = round == MaxFoldOverRounds - 1;
            for (var point = 0; point < outputPointCount; point++)
            {
                if (_foldedPoints[point])
                    outputPositions[point] = isLast ? _linearPositions[point] : Vector3.Lerp(_linearPositions[point], outputPositions[point], 0.5f);
            }
        }
    }

    /** Whether every corner of the polygon turns the way of <paramref name="reference"/>; corners without a turn are ignored. */
    private static bool TurnsConsistently(Vector3[] positions, int[] vertexPoints, int start, int end, Vector3 reference)
    {
        var count = end - start;
        for (var i = 0; i < count; i++)
        {
            var previous = positions[vertexPoints[start + (i + count - 1) % count]];
            var current = positions[vertexPoints[start + i]];
            var next = positions[vertexPoints[start + (i + 1) % count]];
            var incoming = current - previous;
            var outgoing = next - current;
            var turn = Vector3.Dot(Vector3.Cross(incoming, outgoing), reference);
            if (turn < -1e-6f * incoming.Length() * outgoing.Length() * reference.Length())
                return false;
        }

        return true;
    }

    /** Newell normal of a polygon, unnormalized. */
    private static Vector3 GetNormal(Vector3[] positions, int[] vertexPoints, int start, int end)
    {
        var normal = Vector3.Zero;
        for (var vertex = start; vertex < end; vertex++)
            normal += Vector3.Cross(positions[vertexPoints[vertex]], positions[vertexPoints[vertex + 1 < end ? vertex + 1 : start]]);

        return normal;
    }

    /// <summary>
    /// Catmull-Clark rule for the source points. Interior: (Q + 2R + (n - 3) P) / n with Q the mean face point, R the
    /// mean edge midpoint and n the valence. On a boundary with two open edges: 3/4 P + 1/8 of both neighbors.
    /// Corners, non-manifold points and points without polygons stay where they are.
    /// </summary>
    private void SmoothSourcePoints(Geometry source, Vector3[] outputPositions, float[] smoothness)
    {
        var positions = source.Positions;
        var pointCount = source.PointCount;
        var edges = source.Edges.Edges;
        EnsureLength(ref _faceSums, pointCount);
        EnsureLength(ref _faceCounts, pointCount);
        EnsureLength(ref _midpointSums, pointCount);
        EnsureLength(ref _valences, pointCount);
        EnsureLength(ref _boundaryCounts, pointCount);
        EnsureLength(ref _boundarySums, pointCount);
        Array.Clear(_faceSums, 0, pointCount);
        Array.Clear(_faceCounts, 0, pointCount);
        Array.Clear(_midpointSums, 0, pointCount);
        Array.Clear(_valences, 0, pointCount);
        Array.Clear(_boundaryCounts, 0, pointCount);
        Array.Clear(_boundarySums, 0, pointCount);

        for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
        {
            var facePoint = _facePoints[primitive];
            if (facePoint < 0)
                continue;

            for (var vertex = source.PrimitiveVertexOffsets[primitive]; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
            {
                var point = source.VertexPoints[vertex];
                _faceSums[point] += outputPositions[facePoint];
                _faceCounts[point]++;
            }
        }

        for (var edge = 0; edge < edges.Length; edge++)
        {
            var use = _edgeUse[edge];
            if (use == 0)
                continue;

            var a = edges[edge].PointA;
            var b = edges[edge].PointB;
            var midpoint = (positions[a] + positions[b]) * 0.5f;
            _midpointSums[a] += midpoint;
            _midpointSums[b] += midpoint;
            _valences[a]++;
            _valences[b]++;
            if (use == 2)
                continue;

            _boundaryCounts[a]++;
            _boundaryCounts[b]++;
            _boundarySums[a] += positions[b];
            _boundarySums[b] += positions[a];
        }

        for (var point = 0; point < pointCount; point++)
        {
            var valence = _valences[point];
            var p = positions[point];
            if (_boundaryCounts[point] == 2)
            {
                outputPositions[point] = Vector3.Lerp(p, p * 0.75f + _boundarySums[point] * 0.125f, smoothness[point]);
            }
            else if (_boundaryCounts[point] == 0 && valence >= 3 && _faceCounts[point] > 0)
            {
                var q = _faceSums[point] / _faceCounts[point];
                var r = _midpointSums[point] / valence;
                outputPositions[point] = Vector3.Lerp(p, (q + 2 * r + (valence - 3) * p) / valence, smoothness[point]);
            }
        }
    }

    /// <summary>
    /// Smooth vertex normals: each vertex averages the area-weighted normals of the polygons around its point, but only
    /// those within <paramref name="smoothAngle"/> degrees of its own polygon, so edges sharper than that stay hard (e.g.
    /// the corners of a cube next to a smoothed region). 180 smooths everything.
    /// </summary>
    private void WriteSmoothNormals(Geometry geometry, float smoothAngle)
    {
        var positions = geometry.Positions;
        var offsets = geometry.PrimitiveVertexOffsets;
        var vertexPoints = geometry.VertexPoints;
        var pointCount = geometry.PointCount;
        var primitiveCount = geometry.PrimitiveCount;

        // Area-weighted (Newell) and unit normal per polygon
        EnsureLength(ref _faceNormals, primitiveCount);
        EnsureLength(ref _faceDirections, primitiveCount);
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var normal = Vector3.Zero;
            if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Polygon)
            {
                var start = offsets[primitive];
                var end = offsets[primitive + 1];
                for (var vertex = start; vertex < end; vertex++)
                {
                    var p0 = positions[vertexPoints[vertex]];
                    var p1 = positions[vertexPoints[vertex + 1 == end ? start : vertex + 1]];
                    normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                          (p0.Z - p1.Z) * (p0.X + p1.X),
                                          (p0.X - p1.X) * (p0.Y + p1.Y));
                }
            }

            _faceNormals[primitive] = normal;
            _faceDirections[primitive] = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.Zero;
        }

        // Polygons around each point (CSR)
        EnsureLength(ref _pointFaceStarts, pointCount + 1);
        Array.Clear(_pointFaceStarts, 0, pointCount + 1);
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            if (geometry.PrimitiveKind[primitive] != PrimitiveKinds.Polygon)
                continue;

            for (var vertex = offsets[primitive]; vertex < offsets[primitive + 1]; vertex++)
                _pointFaceStarts[vertexPoints[vertex] + 1]++;
        }

        for (var point = 0; point < pointCount; point++)
            _pointFaceStarts[point + 1] += _pointFaceStarts[point];

        EnsureLength(ref _pointFaces, _pointFaceStarts[pointCount]);
        EnsureLength(ref _pointFaceFill, pointCount);
        Array.Copy(_pointFaceStarts, _pointFaceFill, pointCount);
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            if (geometry.PrimitiveKind[primitive] != PrimitiveKinds.Polygon)
                continue;

            for (var vertex = offsets[primitive]; vertex < offsets[primitive + 1]; vertex++)
                _pointFaces[_pointFaceFill[vertexPoints[vertex]]++] = primitive;
        }

        var minCos = smoothAngle >= 180 ? -2f : MathF.Cos(Math.Clamp(smoothAngle, 0, 180) * MathF.PI / 180f);
        var normals = geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Vertex, geometry.VertexCount).Values;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            var ownDirection = _faceDirections[primitive];
            for (var vertex = offsets[primitive]; vertex < offsets[primitive + 1]; vertex++)
            {
                var point = vertexPoints[vertex];
                var sum = Vector3.Zero;
                for (var index = _pointFaceStarts[point]; index < _pointFaceStarts[point + 1]; index++)
                {
                    var face = _pointFaces[index];
                    if (Vector3.Dot(_faceDirections[face], ownDirection) >= minCos)
                        sum += _faceNormals[face];
                }

                normals[vertex] = sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum)
                                  : ownDirection.LengthSquared() > 0 ? ownDirection : Vector3.UnitY;
            }
        }
    }

    #region ICustomDropdownHolder - float attributes of the last input
    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => inputId == LevelAttribute.Input.InputDefinition.Id ? LevelAttribute.Value : SmoothnessAttribute.Value;

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) => GeometryAttributeChoices.GetAttributeNames(_lastSource, null, floatsOnly: true);

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (selected == null)
            return;

        if (inputId == SmoothnessAttribute.Input.InputDefinition.Id)
            SmoothnessAttribute.SetTypedInputValue(selected);
        else if (inputId == LevelAttribute.Input.InputDefinition.Id)
            LevelAttribute.SetTypedInputValue(selected);
    }
    #endregion

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

    private const int MaxIterations = 6;
    private const int MaxFoldOverRounds = 8;
    private const int MaxVertexCount = 4000000;
    private static readonly string[] _normalNames = [GeometryAttributeNames.Normal];

    private readonly Geometry _outputA = new();
    private readonly Geometry _outputB = new();
    private readonly GeometryAttributeStencil _pointStencil = new();
    private readonly GeometryAttributeStencil _vertexStencil = new();
    private readonly GeometryAttributeStencil _primitiveStencil = new();
    private Geometry? _lastSource;
    private float[] _smoothness = [];
    private Vector3[] _linearPositions = [];
    private bool[] _foldedPoints = [];
    private float[] _attributeScratch = [];
    private int[] _facePoints = [];
    private int[] _edgePoints = [];
    private bool[] _splits = [];
    private float[] _levels = [];
    private int[] _primitiveStarts = [];
    private int[] _edgeUse = [];
    private Vector3[] _faceSums = [];
    private int[] _faceCounts = [];
    private Vector3[] _midpointSums = [];
    private int[] _valences = [];
    private int[] _boundaryCounts = [];
    private Vector3[] _boundarySums = [];
    private Vector3[] _faceNormals = [];
    private Vector3[] _faceDirections = [];
    private int[] _pointFaceStarts = [];
    private int[] _pointFaceFill = [];
    private int[] _pointFaces = [];
    private string _statusMessage = string.Empty;

    [Input(Guid = "bef4d855-bedd-45ea-9d28-0f45b9fc3b7a")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "070c122c-2fea-425d-bf5c-62910f393676")]
    public readonly InputSlot<float> Smoothness = new();

    [Input(Guid = "951c95ee-c446-41be-ba9e-7211dc78ff2b")]
    public readonly InputSlot<string> SmoothnessAttribute = new();

    [Input(Guid = "26a66856-a38f-4dab-a274-3ef133ed43df")]
    public readonly InputSlot<string> LevelAttribute = new();

    [Input(Guid = "6ff3ad95-376e-4de3-ba02-875a3f620bee")]
    public readonly InputSlot<float> MaxEdgeLength = new();

    [Input(Guid = "09b5442d-9eff-43e1-b6e8-79eaed455b74")]
    public readonly InputSlot<float> SmoothAngle = new();

    [Input(Guid = "f361912f-b190-4e4f-a192-ef77e8ce5eac")]
    public readonly InputSlot<int> Iterations = new();

    [Input(Guid = "07a29562-d391-47cc-a344-fc447dbcd4da", MappedType = typeof(NormalModes))]
    public readonly InputSlot<int> Normals = new();
}

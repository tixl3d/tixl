using System.Threading;
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Angle-based UV unwrap for the polygons of a Geometry: primitives are grouped into
/// charts while their normals stay within a threshold, each chart is unfolded flat,
/// and the resulting islands are packed into 0..1. Curve primitives pass through
/// unchanged.
/// </summary>
[Guid("f2bfa791-4d84-4a9c-921b-e88b9cf52c1f")]
internal sealed class UnwrapGeometry : Instance<UnwrapGeometry>, IProgressProvider
{
    private enum UnwrapModes
    {
        Default,
        Text,
    }

    private sealed class Chart
    {
        public Vector2 Min;
        public Vector2 Max;
        public Vector2 PackOffset;
        public Vector2 PackMin;
        public float PackScale = 1f;

        public float Width => Max.X - Min.X;
        public float Height => Max.Y - Min.Y;
    }

    /// <summary>
    /// Flattens one chart by walking its faces and unfolding each one across the
    /// shared edge. A point touched by several faces is averaged, so non-developable
    /// charts still produce a single, gap-free island.
    /// </summary>
    private sealed class ChartLayout
    {
        public ChartLayout(Geometry source)
        {
            _source = source;
            _pointPositions = new Vector2[source.PointCount];
            _pointSums = new Vector2[source.PointCount];
            _pointCounts = new int[source.PointCount];
            _pointStamps = new int[source.PointCount];
        }

        public int Stamp { get; private set; }

        public int BeginChart() => ++Stamp;

        /// <summary>
        /// Cap charts are flat and must keep their glyph upright in UV space, so they
        /// are projected onto a fixed text basis instead of the seed face's longest edge.
        /// Front caps use +X, back caps use -X (as seen from behind); both use +Y up.
        /// </summary>
        public void BeginTextCapChart(bool isBack)
        {
            _planarXAxis = isBack ? -Vector3.UnitX : Vector3.UnitX;
            _planarYAxis = Vector3.UnitY;
        }

        public void ProjectPlanarFace(int faceIndex)
        {
            var start = _source.PrimitiveVertexOffsets[faceIndex];
            var end = _source.PrimitiveVertexOffsets[faceIndex + 1];
            for (var c = start; c < end; c++)
            {
                var pointId = _source.VertexPoints[c];
                var position = _source.Positions[pointId];
                AddSample(pointId, new Vector2(Vector3.Dot(position, _planarXAxis), Vector3.Dot(position, _planarYAxis)));
            }
        }

        public Vector2 GetPosition(int pointId) => _pointPositions[pointId];

        public bool TryPlaceSeed(int faceIndex)
        {
            if (!TryComputeFaceLocal2D(faceIndex))
                return false;

            var start = _source.PrimitiveVertexOffsets[faceIndex];
            for (var i = 0; i < _localFace.Count; i++)
            {
                AddSample(_source.VertexPoints[start + i], _localFace[i]);
            }

            return true;
        }

        public bool TryPlaceFace(int parentFace, int faceIndex, EdgeTopology.Edge edge)
        {
            if (_pointStamps[edge.PointA] != Stamp || _pointStamps[edge.PointB] != Stamp)
                return false;
            if (!TryComputeFaceLocal2D(faceIndex))
                return false;

            var localAIndex = FindCornerIndex(faceIndex, edge.PointA);
            var localBIndex = FindCornerIndex(faceIndex, edge.PointB);
            if (localAIndex < 0 || localBIndex < 0)
                return false;

            var localA = _localFace[localAIndex];
            var localB = _localFace[localBIndex];
            var localEdge = localB - localA;
            var localEdgeLengthSq = localEdge.LengthSquared();
            if (localEdgeLengthSq < 1e-12f)
                return false;

            var targetA = _pointPositions[edge.PointA];
            var targetB = _pointPositions[edge.PointB];
            var targetEdge = targetB - targetA;
            var targetEdgeLengthSq = targetEdge.LengthSquared();
            if (targetEdgeLengthSq < 1e-12f)
                return false;

            var localEdgeLength = MathF.Sqrt(localEdgeLengthSq);
            var targetEdgeLength = MathF.Sqrt(targetEdgeLengthSq);
            var alongAxis = localEdge / localEdgeLength;
            var localLeftAxis = new Vector2(-alongAxis.Y, alongAxis.X);
            var targetAxis = targetEdge / targetEdgeLength;
            var targetLeftAxis = new Vector2(-targetAxis.Y, targetAxis.X);
            var scale = targetEdgeLength / localEdgeLength;

            // The candidate face defaults to the left of its directed shared edge.
            // In a consistently wound mesh that is already opposite to the parent,
            // so mirroring only corrects genuinely flipped neighbours.
            var mirror = 1f;
            var hasCandidateInterior = false;
            var candidateInteriorLocal = Vector2.Zero;
            for (var i = 0; i < _localFace.Count; i++)
            {
                if (i == localAIndex || i == localBIndex)
                    continue;

                candidateInteriorLocal = _localFace[i];
                hasCandidateInterior = true;
                break;
            }

            if (TryFindInteriorPoint(parentFace, edge.PointA, edge.PointB, out var parentInterior))
            {
                var parentSide = Vector2.Dot(parentInterior - targetA, targetLeftAxis);
                if (hasCandidateInterior)
                {
                    // Compare the untransformed candidate side with the parent's side
                    // in chart space; the edge direction is not tied to face winding.
                    var relative = candidateInteriorLocal - localA;
                    var candidateOnLeft = targetA
                                          + targetAxis * (Vector2.Dot(relative, alongAxis) * scale)
                                          + targetLeftAxis * (Vector2.Dot(relative, localLeftAxis) * scale);
                    var candidateSide = Vector2.Dot(candidateOnLeft - targetA, targetLeftAxis);
                    if (candidateSide * parentSide > 0f)
                        mirror = -1f;
                }
                else if (parentSide > 0f)
                {
                    mirror = -1f;
                }
            }

            var start = _source.PrimitiveVertexOffsets[faceIndex];
            for (var i = 0; i < _localFace.Count; i++)
            {
                var relative = _localFace[i] - localA;
                var along = Vector2.Dot(relative, alongAxis);
                var across = Vector2.Dot(relative, localLeftAxis);
                var position = targetA
                               + targetAxis * (along * scale)
                               + targetLeftAxis * (across * scale * mirror);
                AddSample(_source.VertexPoints[start + i], position);
            }

            return true;
        }

        private bool TryFindInteriorPoint(int faceIndex, int pointA, int pointB, out Vector2 interior)
        {
            var start = _source.PrimitiveVertexOffsets[faceIndex];
            var end = _source.PrimitiveVertexOffsets[faceIndex + 1];
            for (var c = start; c < end; c++)
            {
                var pointId = _source.VertexPoints[c];
                if (pointId == pointA || pointId == pointB || _pointStamps[pointId] != Stamp)
                    continue;

                interior = _pointPositions[pointId];
                return true;
            }

            interior = Vector2.Zero;
            return false;
        }

        private void AddSample(int pointId, Vector2 position)
        {
            if (_pointStamps[pointId] != Stamp)
            {
                _pointStamps[pointId] = Stamp;
                _pointSums[pointId] = position;
                _pointCounts[pointId] = 1;
                _pointPositions[pointId] = position;
                return;
            }

            _pointSums[pointId] += position;
            _pointCounts[pointId]++;
            _pointPositions[pointId] = _pointSums[pointId] / _pointCounts[pointId];
        }

        private int FindCornerIndex(int faceIndex, int pointId)
        {
            var start = _source.PrimitiveVertexOffsets[faceIndex];
            var end = _source.PrimitiveVertexOffsets[faceIndex + 1];
            for (var c = start; c < end; c++)
            {
                if (_source.VertexPoints[c] == pointId)
                    return c - start;
            }

            return -1;
        }

        /// <summary>
        /// Rigidly projects the face onto its own plane, using its longest edge as
        /// the x axis. Planar faces are exact; non-planar ones lose their warp.
        /// </summary>
        private bool TryComputeFaceLocal2D(int faceIndex)
        {
            _localFace.Clear();

            var start = _source.PrimitiveVertexOffsets[faceIndex];
            var end = _source.PrimitiveVertexOffsets[faceIndex + 1];
            if (end - start < 3)
                return false;

            var normal = Vector3.Zero;
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var p0 = _source.Positions[_source.VertexPoints[c]];
                var p1 = _source.Positions[_source.VertexPoints[next]];
                normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                      (p0.Z - p1.Z) * (p0.X + p1.X),
                                      (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            var normalLengthSq = normal.LengthSquared();
            if (normalLengthSq < 1e-12f)
                return false;

            var bestEdgeLengthSq = 0f;
            var bestEdgeCorner = start;
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var edge = _source.Positions[_source.VertexPoints[next]] - _source.Positions[_source.VertexPoints[c]];
                var edgeLengthSq = edge.LengthSquared();
                if (edgeLengthSq <= bestEdgeLengthSq)
                    continue;

                bestEdgeLengthSq = edgeLengthSq;
                bestEdgeCorner = c;
            }

            if (bestEdgeLengthSq < 1e-12f)
                return false;

            var normal3D = normal / MathF.Sqrt(normalLengthSq);
            var nextBestCorner = bestEdgeCorner + 1 == end ? start : bestEdgeCorner + 1;
            var origin = _source.Positions[_source.VertexPoints[bestEdgeCorner]];
            var xAxis = (_source.Positions[_source.VertexPoints[nextBestCorner]] - origin) / MathF.Sqrt(bestEdgeLengthSq);
            var yAxis = Vector3.Cross(normal3D, xAxis);
            var yAxisLengthSq = yAxis.LengthSquared();
            if (yAxisLengthSq < 1e-12f)
                return false;

            yAxis /= MathF.Sqrt(yAxisLengthSq);

            for (var c = start; c < end; c++)
            {
                var delta = _source.Positions[_source.VertexPoints[c]] - origin;
                _localFace.Add(new Vector2(Vector3.Dot(delta, xAxis), Vector3.Dot(delta, yAxis)));
            }

            return true;
        }

        private readonly Geometry _source;
        private readonly Vector2[] _pointPositions;
        private readonly Vector2[] _pointSums;
        private readonly int[] _pointCounts;
        private readonly int[] _pointStamps;
        private readonly List<Vector2> _localFace = [];
        private Vector3 _planarXAxis;
        private Vector3 _planarYAxis;
    }

    [Output(Guid = "62b96028-0b4e-4040-a3c5-931d7bb747a7")]
    public readonly Slot<Geometry> Result = new();

    public UnwrapGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var original = Geometry.GetValue(context);

        // The unwrap only works on polygons; curves are appended to the result unchanged.
        var source = original != null ? _polygons.GetPolygons(original) : null;
        var angleDegrees = Math.Clamp(AngleThreshold.GetValue(context), 0f, 90f);
        var padding = Math.Clamp(Padding.GetValue(context), 0f, 0.05f);
        var toTexCoord2 = ToTexCoord2.GetValue(context);
        var mode = (UnwrapModes)Math.Clamp(Mode.GetValue(context), 0, 1);

        if (source == null || source.PrimitiveCount == 0 || source.VertexCount == 0)
        {
            Result.Value = original;
            return;
        }

        if (Async.GetValue(context))
        {
            // Key on the input geometry rather than the filtered copy: for geometry with
            // curves the filter hands out a new instance every frame, which would restart
            // the background job before it could ever finish.
            var inputsVersion = HashCode.Combine(original.Version, original.GetHashCode(), angleDegrees, padding, toTexCoord2, (int)mode);
            var result = _asyncComputation.Update(context, Result, inputsVersion,
                                                  token =>
                                                  {
                                                      var target = new Geometry();
                                                      Build(target, source, angleDegrees, padding, toTexCoord2, mode, token);
                                                      return target;
                                                  });
            Result.Value = _curves.Combine(result, original) ?? original;
            return;
        }

        _asyncComputation.WaitForPending(Result);
        Build(_output, source, angleDegrees, padding, toTexCoord2, mode, CancellationToken.None);
        Result.Value = _curves.Combine(_output, original) ?? original;
    }

    private void Build(Geometry target, Geometry source, float angleDegrees, float padding, bool toTexCoord2, UnwrapModes mode,
                       CancellationToken token)
    {
        var faceCount = source.PrimitiveCount;
        var cornerCount = source.VertexCount;
        var positions = source.Positions;
        var offsets = source.PrimitiveVertexOffsets;
        var cornerPoints = source.VertexPoints;
        var edges = source.Edges;
        var targetUvName = toTexCoord2 ? GeometryAttributeNames.TexCoord2 : GeometryAttributeNames.TexCoord;

        // IsSide (from [ExtrudeCurves]) separates flat caps from walls and bevels.
        // Without it, Text mode falls back to Default.
        source.Attributes.TryGet<float>(GeometryAttributeNames.IsSide, AttributeDomain.Primitive, out var isSide);
        var hasSideAttribute = mode == UnwrapModes.Text && isSide != null && isSide.Count == faceCount;

        var faceNormals = new Vector3[faceCount];
        var faceAreas = new float[faceCount];
        for (var faceIndex = 0; faceIndex < faceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            var normal = Vector3.Zero;
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var p0 = positions[cornerPoints[c]];
                var p1 = positions[cornerPoints[next]];
                normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                      (p0.Z - p1.Z) * (p0.X + p1.X),
                                      (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            var normalLength = normal.Length();
            faceAreas[faceIndex] = normalLength * 0.5f;
            faceNormals[faceIndex] = normalLength > 1e-10f ? normal / normalLength : Vector3.UnitY;
        }

        // Large faces seed the charts, so a flat face claims its bevel fringe before
        // a small strip or corner patch gets a chance to become its own island.
        var seedOrder = new int[faceCount];
        for (var i = 0; i < faceCount; i++)
        {
            seedOrder[i] = i;
        }

        Array.Sort(seedOrder, (a, b) =>
                              {
                                  var areaComparison = faceAreas[b].CompareTo(faceAreas[a]);
                                  return areaComparison != 0 ? areaComparison : a.CompareTo(b);
                              });

        var cosThreshold = MathF.Cos(angleDegrees * MathUtils.ToRad);
        var faceChart = new int[faceCount];
        Array.Fill(faceChart, -1);
        var facePlaced = new int[faceCount];
        var cornerChart = new int[cornerCount];
        Array.Fill(cornerChart, -1);
        var localUvs = new Vector2[cornerCount];
        var charts = new List<Chart>();
        var frontCapCharts = new List<int>();
        var backCapCharts = new List<int>();
        var packedCharts = new List<int>();
        var layout = new ChartLayout(source);
        var queue = new Queue<int>();
        var chartFaces = new List<int>();
        var chartId = 0;

        if (hasSideAttribute)
        {
            BuildSideStripeCharts(source, isSide, charts, packedCharts, localUvs, cornerChart, faceChart, token);
        }

        for (var seedIndex = 0; seedIndex < faceCount; seedIndex++)
        {
            token.ThrowIfCancellationRequested();
            if ((seedIndex & 63) == 0)
                _asyncComputation.ReportProgress(0.1f + 0.6f * seedIndex / faceCount);

            var seed = seedOrder[seedIndex];
            if (faceChart[seed] >= 0)
                continue;

            // --- chart growth ---
            chartFaces.Clear();
            queue.Clear();
            queue.Enqueue(seed);
            faceChart[seed] = chartId;
            var seedNormal = faceNormals[seed];
            while (queue.Count > 0)
            {
                var face = queue.Dequeue();
                chartFaces.Add(face);
                var start = offsets[face];
                var end = offsets[face + 1];
                for (var c = start; c < end; c++)
                {
                    var edge = edges.Edges[edges.CornerEdgeIndices[c]];
                    var other = edge.Face0 == face ? edge.Face1 : edge.Face0;
                    if (other < 0 || other == face || faceChart[other] >= 0)
                        continue;

                    // Caps and walls never share an island in text mode, so the caps
                    // stay flat and the glyph does not merge into its bevel.
                    if (hasSideAttribute
                        && (isSide.Values[other] > 0.5f) != (isSide.Values[face] > 0.5f))
                    {
                        continue;
                    }

                    // Bound both the local fold and the total spread from the seed,
                    // so charts stay flattenable even across curved bevels.
                    if (Vector3.Dot(faceNormals[other], seedNormal) < cosThreshold)
                        continue;
                    if (Vector3.Dot(faceNormals[other], faceNormals[face]) < cosThreshold)
                        continue;

                    faceChart[other] = chartId;
                    queue.Enqueue(other);
                }
            }

            // --- chart layout ---
            var stamp = layout.BeginChart();
            var planarCapChart = hasSideAttribute && isSide.Values[seed] < 0.5f;
            if (planarCapChart)
            {
                layout.BeginTextCapChart(faceNormals[seed].Z < 0);
                foreach (var face in chartFaces)
                {
                    layout.ProjectPlanarFace(face);
                    facePlaced[face] = stamp;
                }
            }
            else
            {
                queue.Clear();
                queue.Enqueue(seed);
                if (layout.TryPlaceSeed(seed))
                {
                    facePlaced[seed] = stamp;
                    while (queue.Count > 0)
                    {
                        var face = queue.Dequeue();
                        var start = offsets[face];
                        var end = offsets[face + 1];
                        for (var c = start; c < end; c++)
                        {
                            var edge = edges.Edges[edges.CornerEdgeIndices[c]];
                            var other = edge.Face0 == face ? edge.Face1 : edge.Face0;
                            if (other < 0 || other == face)
                                continue;
                            if (faceChart[other] != chartId || facePlaced[other] == stamp)
                                continue;
                            if (!layout.TryPlaceFace(face, other, edge))
                                continue;

                            facePlaced[other] = stamp;
                            queue.Enqueue(other);
                        }
                    }
                }
            }

            // --- chart bounds ---
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            var placedPrimitiveCount = 0;
            foreach (var face in chartFaces)
            {
                if (facePlaced[face] != stamp)
                    continue;

                placedPrimitiveCount++;
                var start = offsets[face];
                var end = offsets[face + 1];
                for (var c = start; c < end; c++)
                {
                    var uv = layout.GetPosition(cornerPoints[c]);
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
            }

            if (placedPrimitiveCount > 0)
            {
                var chartIndex = charts.Count;
                charts.Add(new Chart { Min = min, Max = max });

                if (!hasSideAttribute || isSide.Values[seed] >= 0.5f)
                {
                    packedCharts.Add(chartIndex);
                }
                else if (faceNormals[seed].Z < 0)
                {
                    backCapCharts.Add(chartIndex);
                }
                else
                {
                    frontCapCharts.Add(chartIndex);
                }

                // A face that failed to unfold keeps cornerChart = -1 and gets zero UVs;
                // that only happens for degenerate geometry.
                foreach (var face in chartFaces)
                {
                    if (facePlaced[face] != stamp)
                        continue;

                    var start = offsets[face];
                    var end = offsets[face + 1];
                    for (var c = start; c < end; c++)
                    {
                        localUvs[c] = layout.GetPosition(cornerPoints[c]);
                        cornerChart[c] = chartIndex;
                    }
                }
            }

            chartId++;
        }

        // --- pack ---
        if (hasSideAttribute)
        {
            // Text: back caps in the top third, front caps in the middle third,
            // walls and bevels in the bottom third.
            FitCapChartsInBand(charts, backCapCharts, padding, 2f / 3f, 1f);
            FitCapChartsInBand(charts, frontCapCharts, padding, 1f / 3f, 2f / 3f);
            PackChartsInBand(charts, packedCharts, padding, 0f, 1f / 3f);
        }
        else
        {
            PackChartsInBand(charts, packedCharts, padding, 0f, 1f);
        }

        var uvs = new Vector2[cornerCount];
        for (var c = 0; c < cornerCount; c++)
        {
            var chartIndex = cornerChart[c];
            if (chartIndex < 0)
            {
                uvs[c] = Vector2.Zero;
                continue;
            }

            var chart = charts[chartIndex];
            var uv = chart.PackOffset + (localUvs[c] - chart.PackMin) * chart.PackScale;
            uvs[c] = new Vector2(Math.Clamp(uv.X, 0f, 1f), Math.Clamp(uv.Y, 0f, 1f));
        }

        _asyncComputation.ReportProgress(0.95f);

        // --- publish ---
        // Topology and untouched attributes are shared; only the target UV channel
        // is replaced. This mirrors how TransformGeometry forwards its input.
        target.Positions = positions;
        target.PrimitiveVertexOffsets = offsets;
        target.VertexPoints = cornerPoints;
        target.PrimitiveKind = source.PrimitiveKind;
        target.PrimitiveClosed = source.PrimitiveClosed;
        target.Parts = source.Parts;
        target.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            if (attribute.Domain == AttributeDomain.Vertex
                && string.Equals(attribute.Name, targetUvName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Attributes.Add(attribute);
        }

        var uvAttribute = target.Attributes.GetOrCreate<Vector2>(targetUvName, AttributeDomain.Vertex, cornerCount);
        uvAttribute.Values = uvs;
        target.InvalidateTopologyCaches();
        _asyncComputation.ReportProgress(1f);
    }

    /// <summary>
    /// Text side walls are closed tubes. Each tube is cut at one vertical edge and
    /// unrolled into a vertical stripe: V follows the perimeter, U follows the depth.
    /// Corners at the seam use V = length on one side, so a loop needs a single seam.
    /// Components that don't form a clean tube are left to the generic chart path.
    /// </summary>
    private static void BuildSideStripeCharts(Geometry source, GeometryAttribute<float> isSide,
                                              List<Chart> charts, List<int> packedCharts,
                                              Vector2[] localUvs, int[] cornerChart, int[] faceChart,
                                              CancellationToken token)
    {
        var faceCount = source.PrimitiveCount;
        var positions = source.Positions;
        var offsets = source.PrimitiveVertexOffsets;
        var cornerPoints = source.VertexPoints;
        var edgeTopology = source.Edges;

        var visited = new bool[faceCount];
        var componentFaces = new List<int>();
        var points = new HashSet<int>();
        var edgeKeys = new HashSet<(int, int)>();
        var frontNeighbors = new Dictionary<int, List<int>>();
        var verticalNeighbors = new Dictionary<int, List<int>>();
        var pointPerimeter = new Dictionary<int, int>();
        var componentCorners = new List<int>();
        var componentUvs = new List<Vector2>();
        var faceUvStarts = new List<int>();
        var queue = new Queue<int>();

        for (var seed = 0; seed < faceCount; seed++)
        {
            if (visited[seed] || isSide.Values[seed] < 0.5f)
                continue;

            token.ThrowIfCancellationRequested();

            componentFaces.Clear();
            visited[seed] = true;
            queue.Clear();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var face = queue.Dequeue();
                componentFaces.Add(face);
                var start = offsets[face];
                var end = offsets[face + 1];
                for (var c = start; c < end; c++)
                {
                    var edge = edgeTopology.Edges[edgeTopology.CornerEdgeIndices[c]];
                    var other = edge.Face0 == face ? edge.Face1 : edge.Face0;
                    if (other < 0 || other == face || visited[other] || isSide.Values[other] < 0.5f)
                        continue;

                    visited[other] = true;
                    queue.Enqueue(other);
                }
            }

            points.Clear();
            edgeKeys.Clear();
            var frontZ = float.MinValue;
            var backZ = float.MaxValue;
            foreach (var face in componentFaces)
            {
                var start = offsets[face];
                var end = offsets[face + 1];
                for (var c = start; c < end; c++)
                {
                    var pointId = cornerPoints[c];
                    points.Add(pointId);
                    var z = positions[pointId].Z;
                    frontZ = MathF.Max(frontZ, z);
                    backZ = MathF.Min(backZ, z);

                    var next = c + 1 == end ? start : c + 1;
                    var otherId = cornerPoints[next];
                    edgeKeys.Add(pointId < otherId ? (pointId, otherId) : (otherId, pointId));
                }
            }

            if (points.Count < 4 || frontZ - backZ < 1e-5f)
                continue;

            frontNeighbors.Clear();
            verticalNeighbors.Clear();
            var epsilon = MathF.Max(1e-5f, (frontZ - backZ) * 1e-4f);
            foreach (var (a, b) in edgeKeys)
            {
                var za = positions[a].Z;
                var zb = positions[b].Z;
                if (MathF.Abs(za - frontZ) < epsilon && MathF.Abs(zb - frontZ) < epsilon)
                {
                    AddNeighbor(frontNeighbors, a, b);
                }
                else if (MathF.Abs(za - zb) > epsilon)
                {
                    AddNeighbor(verticalNeighbors, a, b);
                }
            }

            if (frontNeighbors.Count < 3)
                continue;

            var loop = new List<int>(frontNeighbors.Count);
            var startPoint = frontNeighbors.Keys.First();
            var previous = -1;
            var current = startPoint;
            var validLoop = true;
            while (true)
            {
                if (!frontNeighbors.TryGetValue(current, out var neighbors) || neighbors.Count != 2)
                {
                    validLoop = false;
                    break;
                }

                loop.Add(current);
                var next = neighbors[0] == previous ? neighbors[1] : neighbors[0];
                previous = current;
                current = next;
                if (current == startPoint)
                    break;
                if (loop.Count > frontNeighbors.Count)
                {
                    validLoop = false;
                    break;
                }
            }

            if (!validLoop || loop.Count != frontNeighbors.Count || loop.Count < 3)
                continue;

            // Cumulative perimeter length around the front ring.
            var loopS = new float[loop.Count];
            for (var i = 1; i < loop.Count; i++)
                loopS[i] = loopS[i - 1] + Vector3.Distance(positions[loop[i - 1]], positions[loop[i]]);
            var perimeterLength = loopS[^1] + Vector3.Distance(positions[loop[^1]], positions[loop[0]]);
            if (perimeterLength < 1e-5f)
                continue;

            pointPerimeter.Clear();
            for (var i = 0; i < loop.Count; i++)
                pointPerimeter[loop[i]] = i;

            // Vertical edges connect the same perimeter position across profile rings.
            queue.Clear();
            foreach (var pointId in loop)
                queue.Enqueue(pointId);
            while (queue.Count > 0)
            {
                var pointId = queue.Dequeue();
                var index = pointPerimeter[pointId];
                if (!verticalNeighbors.TryGetValue(pointId, out var neighbors))
                    continue;

                foreach (var other in neighbors)
                {
                    if (pointPerimeter.ContainsKey(other))
                        continue;

                    pointPerimeter[other] = index;
                    queue.Enqueue(other);
                }
            }

            if (pointPerimeter.Count != points.Count)
                continue;

            componentCorners.Clear();
            componentUvs.Clear();
            faceUvStarts.Clear();
            var depth = frontZ - backZ;
            foreach (var face in componentFaces)
            {
                faceUvStarts.Add(componentUvs.Count);
                var start = offsets[face];
                var end = offsets[face + 1];
                var minIndex = int.MaxValue;
                var maxIndex = int.MinValue;
                for (var c = start; c < end; c++)
                {
                    var index = pointPerimeter[cornerPoints[c]];
                    minIndex = Math.Min(minIndex, index);
                    maxIndex = Math.Max(maxIndex, index);
                }

                // Only the face crossing the seam duplicates the start/end perimeter
                // position; every other loop edge stays continuous.
                var crossesSeam = minIndex == 0 && maxIndex == loop.Count - 1;
                for (var c = start; c < end; c++)
                {
                    var pointId = cornerPoints[c];
                    var index = pointPerimeter[pointId];
                    var s = loopS[index];
                    if (crossesSeam && index == 0)
                        s = perimeterLength;

                    var t = frontZ - positions[pointId].Z;
                    componentCorners.Add(c);
                    componentUvs.Add(new Vector2(t, s));
                }
            }

            // Keep UV triangles front-facing; flipping the depth axis preserves the
            // single perimeter seam.
            var flipU = false;
            for (var faceIndex = 0; faceIndex < componentFaces.Count && !flipU; faceIndex++)
            {
                var start = faceUvStarts[faceIndex];
                var end = faceIndex + 1 < faceUvStarts.Count ? faceUvStarts[faceIndex + 1] : componentUvs.Count;
                if (end - start < 3)
                    continue;

                var area = 0f;
                for (var i = start; i < end; i++)
                {
                    var a = componentUvs[i];
                    var b = componentUvs[i + 1 == end ? start : i + 1];
                    area += a.X * b.Y - b.X * a.Y;
                }

                if (area < 0f)
                    flipU = true;
            }

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            for (var i = 0; i < componentUvs.Count; i++)
            {
                var uv = componentUvs[i];
                if (flipU)
                    uv.X = depth - uv.X;

                componentUvs[i] = uv;
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            var chartIndex = charts.Count;
            charts.Add(new Chart { Min = min, Max = max });
            packedCharts.Add(chartIndex);
            for (var i = 0; i < componentCorners.Count; i++)
            {
                var corner = componentCorners[i];
                localUvs[corner] = componentUvs[i];
                cornerChart[corner] = chartIndex;
            }

            foreach (var face in componentFaces)
                faceChart[face] = int.MaxValue;
        }
    }

    private static void AddNeighbor(Dictionary<int, List<int>> adjacency, int a, int b)
    {
        AddDirectedNeighbor(adjacency, a, b);
        AddDirectedNeighbor(adjacency, b, a);
    }

    private static void AddDirectedNeighbor(Dictionary<int, List<int>> adjacency, int a, int b)
    {
        if (!adjacency.TryGetValue(a, out var list))
        {
            list = [];
            adjacency[a] = list;
        }

        if (!list.Contains(b))
            list.Add(b);
    }

    /// <summary>
    /// Fits one cap group into its horizontal band. All cap charts share the same
    /// text-space projection, so this keeps glyph order and line layout intact.
    /// </summary>
    private static void FitCapChartsInBand(List<Chart> charts, List<int> chartIndices, float padding, float bandMinY, float bandMaxY)
    {
        if (chartIndices.Count == 0)
            return;

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var chartIndex in chartIndices)
        {
            min = Vector2.Min(min, charts[chartIndex].Min);
            max = Vector2.Max(max, charts[chartIndex].Max);
        }

        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var bandWidth = 1f - 2f * padding;
        var bandHeight = bandMaxY - bandMinY - 2f * padding;
        var scale = MathF.Min(bandWidth / MathF.Max(width, 1e-6f),
                              bandHeight / MathF.Max(height, 1e-6f));
        if (!float.IsFinite(scale) || scale <= 0f)
            scale = 1f;

        var groupCenter = (min + max) * 0.5f;
        var bandCenter = new Vector2(0.5f, (bandMinY + bandMaxY) * 0.5f);
        var offset = bandCenter - groupCenter * scale;
        foreach (var chartIndex in chartIndices)
        {
            var chart = charts[chartIndex];
            chart.PackMin = Vector2.Zero;
            chart.PackScale = scale;
            chart.PackOffset = offset;
        }
    }

    /// <summary>
    /// Uniformly scales the listed islands and shelf-packs them into one horizontal band.
    /// </summary>
    private static void PackChartsInBand(List<Chart> charts, List<int> chartIndices, float padding, float bandMinY, float bandMaxY)
    {
        if (chartIndices.Count == 0)
            return;

        var order = new int[chartIndices.Count];
        var totalArea = 0f;
        var maxWidth = 0f;
        var maxHeight = 0f;
        for (var i = 0; i < chartIndices.Count; i++)
        {
            var chart = charts[chartIndices[i]];
            order[i] = chartIndices[i];
            var width = MathF.Max(chart.Width, 0f);
            var height = MathF.Max(chart.Height, 0f);
            totalArea += width * height;
            maxWidth = MathF.Max(maxWidth, width);
            maxHeight = MathF.Max(maxHeight, height);
        }

        Array.Sort(order, (a, b) =>
                          {
                              var heightComparison = charts[b].Height.CompareTo(charts[a].Height);
                              return heightComparison != 0 ? heightComparison : a.CompareTo(b);
                          });

        var bandWidth = 1f - 2f * padding;
        var bandHeight = bandMaxY - bandMinY - 2f * padding;
        var scale = MathF.Min(MathF.Min(bandWidth / MathF.Max(maxWidth, 1e-6f), bandHeight / MathF.Max(maxHeight, 1e-6f)),
                              MathF.Sqrt(0.75f * bandWidth * bandHeight / MathF.Max(totalArea, 1e-6f)));
        if (!float.IsFinite(scale) || scale <= 0f)
            scale = 1f;

        var packPadding = padding;
        var packed = false;
        for (var attempt = 0; attempt < 60 && !packed; attempt++)
        {
            packed = TryPackChartsInBand(charts, order, scale, packPadding, bandMinY, bandMaxY);
            if (packed)
                break;

            scale *= 0.9f;
            if (attempt % 10 == 9)
                packPadding *= 0.5f;
        }

        if (!packed)
        {
            // A shelf pack can always fit with enough rows once padding is gone.
            packPadding = 0f;
            for (var attempt = 0; attempt < 60 && !packed; attempt++)
            {
                packed = TryPackChartsInBand(charts, order, scale, packPadding, bandMinY, bandMaxY);
                if (!packed)
                    scale *= 0.75f;
            }
        }

        foreach (var chartIndex in order)
        {
            var chart = charts[chartIndex];
            chart.PackMin = chart.Min;
            chart.PackScale = scale;
        }
    }

    private static bool TryPackChartsInBand(List<Chart> charts, int[] order, float scale, float padding, float bandMinY, float bandMaxY)
    {
        var x = padding;
        var y = bandMinY + padding;
        var rowHeight = 0f;
        var maxX = 1f - padding;
        var maxY = bandMaxY - padding;
        foreach (var chartIndex in order)
        {
            var chart = charts[chartIndex];
            var width = chart.Width * scale;
            var height = chart.Height * scale;
            if (width > maxX - padding + 1e-5f)
                return false;

            if (x + width > maxX + 1e-5f)
            {
                x = padding;
                y += rowHeight + padding;
                rowHeight = 0f;
            }

            if (y + height > maxY + 1e-5f)
                return false;

            chart.PackOffset = new Vector2(x, y);
            x += width + padding;
            rowHeight = MathF.Max(rowHeight, height);
        }

        return true;
    }

    public bool TryGetProgress(out float progress) => _asyncComputation.TryGetUiProgress(out progress);

    private readonly Geometry _output = new();
    private readonly AsyncComputation<Geometry> _asyncComputation = new();

    /** The op works on the polygons; the curves are appended to its result unchanged. */
    private readonly GeometryPolygonFilter _polygons = new();
    private readonly GeometryCurvePassThrough _curves = new();

    [Input(Guid = "257dfc13-8dba-4f90-bcc0-39e140a82fb8")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "1941f12c-d792-4cd8-8372-e58e20b489e2", MappedType = typeof(UnwrapModes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "10b6e1ae-8f18-4c8a-a1fb-e10cbb64e7c7")]
    public readonly InputSlot<float> AngleThreshold = new();

    [Input(Guid = "865a12a4-e5ac-4e9c-98ed-b4bfe3d615c9")]
    public readonly InputSlot<float> Padding = new();

    [Input(Guid = "0d02f9db-b6c4-452c-8bea-76924f303495")]
    public readonly InputSlot<bool> ToTexCoord2 = new();

    [Input(Guid = "cf58c803-e7d0-4867-8ccf-38e1968de225")]
    public readonly InputSlot<bool> Async = new();
}

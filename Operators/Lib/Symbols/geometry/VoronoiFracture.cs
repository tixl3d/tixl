using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lib.Utils;
using LibTessDotNet;
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Fractures a MeshGeometry into Voronoi cells around seed points: one part per
/// cell, built by clipping the mesh against the bisector planes between seeds and
/// capping the cuts. Surface corners keep their interpolated normals; cap faces
/// are flat and marked with IsCut = 1 (mirrored into Selection) for downstream styling.
/// Cells are computed in parallel; per cell only the planes that can actually reach
/// the cell are applied (nearest seeds first, stop once the bisector lies beyond the
/// cell's bounding radius).
/// </summary>
[Guid("70d8f2b5-3a41-4c96-8e2d-b09c6f5e1a73")]
[ExportDependencies("LibTessDotNet.dll")]
internal sealed class VoronoiFracture : Instance<VoronoiFracture>, IProgressProvider
{
    [Output(Guid = "48e5a9c1-d637-4b80-92f4-5c1e8b0d7a26")]
    public readonly Slot<MeshGeometry> Result = new();

    public VoronoiFracture()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        if (source == null || source.FaceCount == 0)
        {
            Result.Value = source;
            return;
        }

        // Seed snapshot (separator-aware)
        _seeds.Clear();
        if (Points.GetValue(context) is StructuredList<Point> pointList)
        {
            var elements = pointList.TypedElements;
            for (var i = 0; i < pointList.NumElements; i++)
            {
                if (!Point.IsSeparator(elements[i]))
                    _seeds.Add(elements[i].Position);
            }
        }

        if (_seeds.Count < 2)
        {
            Result.Value = source;
            return;
        }

        var sourceIsClosed = UpdateSourceStats(source);

        var fillInterior = FillInterior.GetValue(context);
        if (Async.GetValue(context))
        {
            var hash = new HashCode();
            hash.Add(source.Version);
            hash.Add(source.GetHashCode());
            hash.Add(fillInterior);
            hash.Add(sourceIsClosed);
            foreach (var seed in _seeds)
            {
                hash.Add(seed);
            }

            // The worker gets its own seed copy - _seeds is refilled on the next Update
            var seeds = _seeds.ToArray();
            var result = _asyncComputation.Update(context, Result, hash.ToHashCode(),
                                                  token =>
                                                  {
                                                      var target = new MeshGeometry();
                                                      Build(target, source, seeds, fillInterior, sourceIsClosed, token);
                                                      return target;
                                                  });
            Result.Value = result ?? source;
            return;
        }

        _asyncComputation.WaitForPending(Result);
        Build(_output, source, _seeds.ToArray(), fillInterior, sourceIsClosed, CancellationToken.None);
        Result.Value = _output;
    }

    /// <summary>
    /// Cutting a cell out of a solid needs a closed surface to tell inside from outside.
    /// Many scanned or sculpted meshes are open shells, and the fracture then produces
    /// unclosed chunks that are hard to tell from a bug in this operator - so say it once
    /// per input version instead. The answer also decides how far the chunks may be patched:
    /// out of a closed solid every chunk must come out closed, so any gap left is this
    /// operator's own doing and gets filled; out of an open shell a gap may be the input's,
    /// and filling it would invent surface that was never there.
    /// </summary>
    private bool UpdateSourceStats(MeshGeometry source)
    {
        var changed = _sourceStats.UpdateIfChanged(source);
        var isClosed = _sourceStats.BoundaryEdges == 0 && _sourceStats.NonManifoldEdges == 0;
        if (changed && !isClosed)
        {
            Log.Warning($"VoronoiFracture: the input mesh is not a closed solid "
                        + $"({_sourceStats.BoundaryEdges} open edges, {_sourceStats.NonManifoldEdges} non-manifold). "
                        + "Chunks will have holes where the surface is missing.", this);
        }

        return isClosed;
    }

    private void Build(MeshGeometry target, MeshGeometry source, Vector3[] seeds, bool fillInterior, bool sourceIsClosed,
                       CancellationToken token)
    {
        var sourceHasNormals = source.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var sourceNormals);

        // Source faces as clip-ready polygons plus a uniform grid, shared read-only by all cells
        var sourceIndex = new SourceIndex(source, sourceHasNormals ? sourceNormals : null);

        // Decides whether a cell that no surface crosses is solid interior (emit its hull) or empty space
        var insideTester = new MeshInsideTester(source);
        // One cell per seed, computed in parallel with per-thread scratch
        var cells = new CellResult[seeds.Length];
        var completed = 0;
        var options = new ParallelOptions { CancellationToken = token };
        Parallel.For(0, seeds.Length, options,
                     () => new CellBuilder(),
                     (seedIndex, _, builder) =>
                     {
                         cells[seedIndex] = builder.BuildCell(sourceIndex, insideTester, seeds, seedIndex, sourceHasNormals, fillInterior,
                                                             sourceIsClosed);
                         var done = Interlocked.Increment(ref completed);
                         _asyncComputation.ReportProgress(done / (float)seeds.Length);
                         return builder;
                     },
                     _ => { });

        token.ThrowIfCancellationRequested();
        // Concatenate in seed order so the result is deterministic
        var totalPoints = 0;
        var totalCorners = 0;
        var totalFaces = 0;
        foreach (var cell in cells)
        {
            totalPoints += cell.Positions.Length;
            totalCorners += cell.Corners.Length;
            totalFaces += cell.FaceOffsets.Length - 1;
        }

        var positions = new Vector3[totalPoints];
        var cornerIndices = new int[totalCorners];
        var faceOffsets = new int[totalFaces + 1];
        var cornerNormals = new Vector3[totalCorners];
        var isCut = new float[totalFaces];
        var parts = new List<GeometryPart>(seeds.Length);

        var pointBase = 0;
        var cornerBase = 0;
        var faceBase = 0;
        for (var seedIndex = 0; seedIndex < cells.Length; seedIndex++)
        {
            var cell = cells[seedIndex];
            var cellFaces = cell.FaceOffsets.Length - 1;
            if (cellFaces > 0)
                parts.Add(new GeometryPart(faceBase, cellFaces, seeds[seedIndex], seedIndex, seedIndex));

            Array.Copy(cell.Positions, 0, positions, pointBase, cell.Positions.Length);
            for (var c = 0; c < cell.Corners.Length; c++)
            {
                cornerIndices[cornerBase + c] = cell.Corners[c] + pointBase;
                cornerNormals[cornerBase + c] = cell.Normals[c];
            }

            for (var f = 0; f < cellFaces; f++)
            {
                faceOffsets[faceBase + f + 1] = cell.FaceOffsets[f + 1] + cornerBase;
                isCut[faceBase + f] = cell.IsCap[f] ? 1f : 0f;
            }

            pointBase += cell.Positions.Length;
            cornerBase += cell.Corners.Length;
            faceBase += cellFaces;
        }

        target.Positions = positions;
        target.FaceCornerOffsets = faceOffsets;
        target.CornerPointIndices = cornerIndices;
        target.Parts = parts.ToArray();
        target.Attributes.Clear();

        if (sourceHasNormals)
        {
            var normals = target.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, totalCorners);
            Array.Copy(cornerNormals, normals.Values, totalCorners);
        }

        // IsCut is the lasting mark; Selection mirrors it so downstream ops act on the cuts by default
        var isCutAttribute = target.Attributes.GetOrCreate<float>(GeometryAttributeNames.IsCut, AttributeDomain.Face, totalFaces);
        var selection = target.Attributes.GetOrCreate<float>(GeometryAttributeNames.Selection, AttributeDomain.Face, totalFaces);
        Array.Copy(isCut, isCutAttribute.Values, totalFaces);
        Array.Copy(isCut, selection.Values, totalFaces);

        target.InvalidateTopologyCaches();
    }

    /// <summary>Output of one cell, in cell-local indices.</summary>
    private readonly record struct CellResult(Vector3[] Positions, int[] Corners, Vector3[] Normals, int[] FaceOffsets, bool[] IsCap);

    /// <summary>
    /// Per-thread clipping state. Everything a cell needs lives here, so cells can be
    /// built concurrently without touching op-level buffers.
    /// </summary>
    private sealed class CellBuilder
    {
        public CellResult BuildCell(SourceIndex source, MeshInsideTester insideTester, Vector3[] seeds, int seedIndex, bool withNormals,
                                   bool sourceIsClosed,
                                    bool fillInterior)
        {
            var seed = seeds[seedIndex];
            // All tolerances scale with the mesh: welding, chaining and hull tests must agree,
            // otherwise a point can be "on the hull" for one step and a separate vertex for the next.
            _weldEpsilon = source.Extent * WeldToleranceFactor;
            _weldEpsilonSq = _weldEpsilon * _weldEpsilon;
            _weldGridScale = 1f / _weldEpsilon;
            // Plane tolerances stay at float-noise scale (welding is three orders coarser and
            // would reclassify real geometry), but they scale with the mesh so a large model
            // does not fall below them. The collector is the looser of the two so that a point
            // the clipper placed on the plane is still recognised as lying in it.
            _planeEpsilon = source.Extent * ClipToleranceFactor;
            _onPlaneEpsilon = source.Extent * OnPlaneToleranceFactor;

            // Nearest seeds first: once a bisector is farther than the cell's bounding
            // radius, no remaining plane can touch the cell.
            if (_order.Length < seeds.Length)
            {
                _order = new int[seeds.Length];
                _distances = new float[seeds.Length];
            }

            for (var i = 0; i < seeds.Length; i++)
            {
                _order[i] = i;
                _distances[i] = Vector3.DistanceSquared(seeds[i], seed);
            }

            Array.Sort(_distances, _order, 0, seeds.Length);

            // Pass 1: the cell as a convex hull, starting from the mesh bounding box (6 quads).
            // Cheap, and it yields the planes that matter plus a tight AABB for the cell.
            _polygonPool.ReturnAll(_polygons);
            source.AddBoundsBox(_polygons, _polygonPool);
            _planes.Clear();
            var boundingRadius = ComputeBoundingRadius(seed);
            for (var rank = 0; rank < seeds.Length && _polygons.Count > 0; rank++)
            {
                var otherIndex = _order[rank];
                if (otherIndex == seedIndex)
                    continue;

                var length = MathF.Sqrt(_distances[rank]);
                if (length < 1e-8f)
                    continue;

                if (length * 0.5f > boundingRadius)
                    break;

                var planeNormal = (seeds[otherIndex] - seed) / length;
                var planeOffset = Vector3.Dot(planeNormal, (seed + seeds[otherIndex]) * 0.5f);
                if (ClipByPlane(planeNormal, planeOffset))
                {
                    // A plane that doesn't touch the hull can't touch the mesh cell inside it.
                    // Its exact face keeps the hull closed (bounds and radius stay right).
                    _planes.Add((planeNormal, planeOffset));
                    var face = BuildHullFace(source, _planes.Count - 1);
                    if (face != null)
                        _polygons.Add(face);

                    boundingRadius = ComputeBoundingRadius(seed);
                }
            }

            ComputeBounds(out var cellMin, out var cellMax);

            // The hull faces on cell planes are the boundary the surface cuts get closed against
            _polygonPool.ReturnAll(_hull);
            for (var i = _polygons.Count - 1; i >= 0; i--)
            {
                var polygon = _polygons[i];
                if (polygon.PlaneIndex >= 0 && polygon.Vertices.Count >= 3)
                {
                    _hull.Add(polygon);
                    _polygons.RemoveAt(i);
                }
            }

            _polygonPool.ReturnAll(_polygons);

            // Pass 2: source polygons in the hull region. Fully inside -> kept by reference,
            // straddling -> cloned and clipped, outside -> dropped before any copy is made.
            _kept.Clear();
            source.CollectCandidates(cellMin, cellMax, _planes, _planeEpsilon, _kept, _polygons, _polygonPool, ref _stamp, ref _stamps);

            // Clip the surface by all planes first - caps are derived afterwards from the
            // final cut edges, so their construction doesn't depend on the plane order.
            for (var planeIndex = 0; planeIndex < _planes.Count; planeIndex++)
            {
                var (planeNormal, planeOffset) = _planes[planeIndex];
                ClipByPlane(planeNormal, planeOffset);
            }

            var surfaceCount = _polygons.Count;
            if (_planeCapped.Length < _planes.Count)
                _planeCapped = new bool[_planes.Count];
            Array.Clear(_planeCapped, 0, _planes.Count);

            // All cuts first: where a cut ends on the edge two planes share, both planes must
            // subdivide that edge at the same point, or their caps meet in a T-junction.
            while (_planeSegments.Count < _planes.Count)
                _planeSegments.Add([]);

            _cellEdgePoints.Clear();
            for (var planeIndex = 0; planeIndex < _planes.Count; planeIndex++)
            {
                var (planeNormal, planeOffset) = _planes[planeIndex];
                CollectPlaneSegments(planeNormal, planeOffset, surfaceCount);
                var stored = _planeSegments[planeIndex];
                stored.Clear();
                stored.AddRange(_cutSegments);
                foreach (var (from, to) in _cutSegments)
                {
                    AddCellEdgePoint(from.Position);
                    AddCellEdgePoint(to.Position);
                }
            }

            for (var planeIndex = 0; planeIndex < _planes.Count; planeIndex++)
            {
                var (planeNormal, planeOffset) = _planes[planeIndex];
                Polygon? hullFace = null;
                foreach (var candidate in _hull)
                {
                    if (candidate.PlaneIndex == planeIndex && candidate.Vertices.Count >= 3)
                    {
                        hullFace = candidate;
                        break;
                    }
                }

                _cutSegments.Clear();
                _cutSegments.AddRange(_planeSegments[planeIndex]);
                BuildCapsForPlane(planeNormal, planeOffset, planeIndex, hullFace, fillInterior, insideTester);
            }

            CloseFacesBorderingCaps(surfaceCount);
            _polygonPool.ReturnAll(_hull);

            // Emit with per-cell point dedup so each chunk is watertight
            _pointLookup.Clear();
            _nextInBucket.Clear();
            _positions.Clear();
            _corners.Clear();
            _normals.Clear();
            _faceOffsets.Clear();
            _isCap.Clear();
            _faceOffsets.Add(0);
            EmitPolygons(_kept, withNormals);
            EmitPolygons(_polygons, withNormals);
            RemoveDuplicateFaces();
            FillCapHoles(withNormals, sourceIsClosed);

            return new CellResult(_positions.ToArray(), _corners.ToArray(), _normals.ToArray(), _faceOffsets.ToArray(), _isCap.ToArray());
        }

        /// <summary>
        /// The cell's face on one plane: a rectangle spanning the mesh bounds, clipped by the
        /// bounding box and all other cell planes. Null if nothing remains.
        /// </summary>
        private Polygon? BuildHullFace(SourceIndex source, int planeIndex)
        {
            var (normal, offset) = _planes[planeIndex];
            var center = normal * offset;
            var tangent = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var bitangent = Vector3.Cross(normal, tangent);
            var extent = Vector3.Distance(source.Min, source.Max) + Vector3.Distance(center, (source.Min + source.Max) * 0.5f);

            var face = _polygonPool.Rent();
            face.IsCap = true;
            face.PlaneIndex = planeIndex;
            face.Vertices.Add(new Vertex(center - tangent * extent - bitangent * extent, normal));
            face.Vertices.Add(new Vertex(center + tangent * extent - bitangent * extent, normal));
            face.Vertices.Add(new Vertex(center + tangent * extent + bitangent * extent, normal));
            face.Vertices.Add(new Vertex(center - tangent * extent + bitangent * extent, normal));
            if (Vector3.Dot(NewellNormal(face), normal) < 0)
                face.Vertices.Reverse();

            // Bounding box as six half-spaces, then the other planes
            var min = source.Min;
            var max = source.Max;
            var ok = ClipPolygon(face, -Vector3.UnitX, -min.X) && ClipPolygon(face, Vector3.UnitX, max.X)
                     && ClipPolygon(face, -Vector3.UnitY, -min.Y) && ClipPolygon(face, Vector3.UnitY, max.Y)
                     && ClipPolygon(face, -Vector3.UnitZ, -min.Z) && ClipPolygon(face, Vector3.UnitZ, max.Z);
            for (var other = 0; ok && other < _planes.Count; other++)
            {
                if (other == planeIndex)
                    continue;

                var (n, d) = _planes[other];
                ok = ClipPolygon(face, n, d);
            }

            if (ok && face.Vertices.Count >= 3)
                return face;

            _polygonPool.Return(face);
            return null;
        }

        /// <summary>Sutherland-Hodgman of a single polygon against dot(n, x) &lt;= offset. False if nothing remains.</summary>
        private bool ClipPolygon(Polygon polygon, Vector3 planeNormal, float planeOffset)
        {
            var vertices = polygon.Vertices;
            _clipScratch.Clear();
            for (var i = 0; i < vertices.Count; i++)
            {
                var current = vertices[i];
                var next = vertices[(i + 1) % vertices.Count];
                var currentDistance = Vector3.Dot(planeNormal, current.Position) - planeOffset;
                var nextDistance = Vector3.Dot(planeNormal, next.Position) - planeOffset;
                if (currentDistance <= _planeEpsilon)
                    _clipScratch.Add(current);

                if (StrictlyCrosses(currentDistance, nextDistance, _planeEpsilon))
                    _clipScratch.Add(Vertex.Lerp(current, next, CrossingFraction(currentDistance, nextDistance)));
            }

            vertices.Clear();
            vertices.AddRange(_clipScratch);
            return vertices.Count >= 3;
        }

        /// <summary>True only when the edge passes from one side of the plane to the other without
        /// either end lying within the tolerance of it.</summary>
        private static bool StrictlyCrosses(float currentDistance, float nextDistance, float epsilon)
        {
            return (currentDistance < -epsilon && nextDistance > epsilon)
                   || (currentDistance > epsilon && nextDistance < -epsilon);
        }

        private static float CrossingFraction(float currentDistance, float nextDistance)
        {
            return Math.Clamp(currentDistance / (currentDistance - nextDistance), 0f, 1f);
        }

        private static Vector3 Centroid(Polygon polygon)
        {
            var sum = Vector3.Zero;
            foreach (var vertex in polygon.Vertices)
            {
                sum += vertex.Position;
            }

            return sum / polygon.Vertices.Count;
        }

        private void EmitPolygons(List<Polygon> polygons, bool withNormals)
        {
            foreach (var polygon in polygons)
            {
                if (polygon.Vertices.Count < 3)
                    continue;

                // Welding can collapse neighbouring corners onto one point; a repeated corner
                // would leave a degenerate fan triangle (half the face missing), so skip it.
                var cornerStart = _corners.Count;
                foreach (var vertex in polygon.Vertices)
                {
                    var pointId = GetOrAddPoint(vertex.Position);
                    if (_corners.Count > cornerStart && _corners[^1] == pointId)
                        continue;

                    _corners.Add(pointId);
                    _normals.Add(withNormals ? vertex.Normal : Vector3.Zero);
                }

                while (_corners.Count - cornerStart > 1 && _corners[^1] == _corners[cornerStart])
                {
                    _corners.RemoveAt(_corners.Count - 1);
                    _normals.RemoveAt(_normals.Count - 1);
                }

                if (_corners.Count - cornerStart < 3)
                {
                    _corners.RemoveRange(cornerStart, _corners.Count - cornerStart);
                    _normals.RemoveRange(cornerStart, _normals.Count - cornerStart);
                    continue;
                }

                _faceOffsets.Add(_corners.Count);
                _isCap.Add(polygon.IsCap);
            }
        }

        /// <summary>
        /// Two caps on one plane can end up with the same corners (a chain that failed to
        /// link and walked the hull on its own). A second face over the same corners is never
        /// legitimate in a cell, so it is dropped here.
        /// </summary>
        private void RemoveDuplicateFaces()
        {
            var faceCount = _faceOffsets.Count - 1;
            _faceKeys.Clear();
            _keepFace.Clear();
            var anyDuplicate = false;
            for (var f = 0; f < faceCount; f++)
            {
                var start = _faceOffsets[f];
                var end = _faceOffsets[f + 1];
                var key = 0L;
                var sum = 0L;
                for (var c = start; c < end; c++)
                {
                    var id = _corners[c];
                    key ^= (id + 1L) * unchecked((long)0x9E3779B97F4A7C15UL); // order-independent
                    sum += id;
                }

                key ^= sum << 20;
                key ^= (long)(end - start) << 56;
                var keep = _faceKeys.Add(key);
                _keepFace.Add(keep);
                anyDuplicate |= !keep;
            }

            if (!anyDuplicate)
                return;

            var writeCorner = 0;
            var writeFace = 0;
            for (var f = 0; f < faceCount; f++)
            {
                var start = _faceOffsets[f];
                var end = _faceOffsets[f + 1];
                if (!_keepFace[f])
                    continue;

                for (var c = start; c < end; c++)
                {
                    _corners[writeCorner] = _corners[c];
                    _normals[writeCorner] = _normals[c];
                    writeCorner++;
                }

                _isCap[writeFace] = _isCap[f];
                _faceOffsets[writeFace + 1] = writeCorner;
                writeFace++;
            }

            _corners.RemoveRange(writeCorner, _corners.Count - writeCorner);
            _normals.RemoveRange(writeCorner, _normals.Count - writeCorner);
            _faceOffsets.RemoveRange(writeFace + 1, _faceOffsets.Count - writeFace - 1);
            _isCap.RemoveRange(writeFace, _isCap.Count - writeFace);
        }

        /// <summary>
        /// Closes the holes left between caps: a boundary loop whose edges all belong to cap
        /// faces is a missing piece of cut surface (a sliver where several planes nearly meet,
        /// or a cap whose chain was too degenerate to build). Loops that touch surface edges
        /// are the real boundary of an open input mesh and stay open - unless every edge is
        /// tiny, which is a degenerate corner at the surface, not a mesh border.
        /// </summary>
        private void FillCapHoles(bool withNormals, bool sourceIsClosed)
        {
            var faceCount = _faceOffsets.Count - 1;
            _edgeUse.Clear();
            for (var f = 0; f < faceCount; f++)
            {
                var start = _faceOffsets[f];
                var end = _faceOffsets[f + 1];
                for (var c = start; c < end; c++)
                {
                    var a = _corners[c];
                    var b = _corners[c + 1 == end ? start : c + 1];
                    var key = a < b ? (a, b) : (b, a);
                    _edgeUse[key] = _edgeUse.GetValueOrDefault(key) + 1;
                }
            }

            // Directed: the hole runs opposite to the face edge, so it inherits a consistent winding
            _holeEdges.Clear();
            _surfaceHoleEdges.Clear();
            for (var f = 0; f < faceCount; f++)
            {
                var start = _faceOffsets[f];
                var end = _faceOffsets[f + 1];
                for (var c = start; c < end; c++)
                {
                    var a = _corners[c];
                    var b = _corners[c + 1 == end ? start : c + 1];
                    var key = a < b ? (a, b) : (b, a);
                    if (_edgeUse[key] != 1)
                        continue;

                    _holeEdges[b] = a;
                    if (!_isCap[f])
                        _surfaceHoleEdges.Add(b);
                }
            }

            if (_holeEdges.Count == 0)
                return;

            _holeUsed.Clear();
            foreach (var loopStart in _holeEdges.Keys)
            {
                if (_holeUsed.Contains(loopStart))
                    continue;

                _holeLoop.Clear();
                var current = loopStart;
                var closed = false;
                for (var guard = 0; guard <= _holeEdges.Count; guard++)
                {
                    _holeLoop.Add(current);
                    _holeUsed.Add(current);
                    if (!_holeEdges.TryGetValue(current, out var next))
                        break; // continues on a surface edge: not a cap hole

                    if (next == loopStart)
                    {
                        closed = true;
                        break;
                    }

                    if (_holeUsed.Contains(next))
                        break;

                    current = next;
                }

                if (!closed || _holeLoop.Count < 3)
                    continue;

                var usesSurfaceEdge = false;
                var isTiny = true;
                var tinyLimitSq = _weldEpsilonSq * 64;
                for (var i = 0; i < _holeLoop.Count; i++)
                {
                    usesSurfaceEdge |= _surfaceHoleEdges.Contains(_holeLoop[i]);
                    var p0 = _positions[_holeLoop[i]];
                    var p1 = _positions[_holeLoop[(i + 1) % _holeLoop.Count]];
                    isTiny &= Vector3.DistanceSquared(p0, p1) < tinyLimitSq;
                }

                // A loop running along surface edges is normally left alone: it may be a hole the
                // input already had, and a flat patch there would invent surface. Out of a closed
                // solid there is no such hole, so the gap is ours to close.
                if (usesSurfaceEdge && !isTiny && !sourceIsClosed)
                    continue;

                var normal = Vector3.Zero;
                for (var i = 0; i < _holeLoop.Count; i++)
                {
                    var p0 = _positions[_holeLoop[i]];
                    var p1 = _positions[_holeLoop[(i + 1) % _holeLoop.Count]];
                    normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                          (p0.Z - p1.Z) * (p0.X + p1.X),
                                          (p0.X - p1.X) * (p0.Y + p1.Y));
                }

                if (normal.LengthSquared() > 1e-20f)
                    normal = Vector3.Normalize(normal);

                foreach (var pointId in _holeLoop)
                {
                    _corners.Add(pointId);
                    _normals.Add(withNormals ? normal : Vector3.Zero);
                }

                _faceOffsets.Add(_corners.Count);
                _isCap.Add(true);
            }
        }

        private void ComputeBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            foreach (var polygon in _polygons)
            {
                foreach (var vertex in polygon.Vertices)
                {
                    min = Vector3.Min(min, vertex.Position);
                    max = Vector3.Max(max, vertex.Position);
                }
            }
        }

        private float ComputeBoundingRadius(Vector3 seed)
        {
            var maxSq = 0f;
            foreach (var polygon in _polygons)
            {
                foreach (var vertex in polygon.Vertices)
                {
                    var d = Vector3.DistanceSquared(vertex.Position, seed);
                    if (d > maxSq)
                        maxSq = d;
                }
            }

            return MathF.Sqrt(maxSq);
        }

        /// <summary>
        /// Clips every polygon in _polygons to the half-space dot(n, x) &lt;= offset. Returns
        /// whether anything changed. Caps are not built here - they're derived afterwards
        /// from the final cut edges (see BuildCapsForPlane).
        /// </summary>
        private bool ClipByPlane(Vector3 planeNormal, float planeOffset)
        {
            _cutSegments.Clear();
            var anyClipped = false;

            for (var polygonIndex = _polygons.Count - 1; polygonIndex >= 0; polygonIndex--)
            {
                var polygon = _polygons[polygonIndex];
                var vertices = polygon.Vertices;
                var allInside = true;
                var allOutside = true;
                foreach (var vertex in vertices)
                {
                    if (Vector3.Dot(planeNormal, vertex.Position) - planeOffset > _planeEpsilon)
                        allInside = false;
                    else
                        allOutside = false;
                }

                if (allInside)
                    continue;

                anyClipped = true;
                if (allOutside)
                {
                    _polygonPool.Return(polygon);
                    _polygons.RemoveAt(polygonIndex);
                    continue;
                }

                _clipScratch.Clear();
                Vertex? firstCut = null;
                var hasPendingEntry = false;
                var pendingEntry = default(Vertex);
                for (var i = 0; i < vertices.Count; i++)
                {
                    var current = vertices[i];
                    var next = vertices[(i + 1) % vertices.Count];
                    var currentDistance = Vector3.Dot(planeNormal, current.Position) - planeOffset;
                    var nextDistance = Vector3.Dot(planeNormal, next.Position) - planeOffset;
                    var currentInside = currentDistance <= _planeEpsilon;

                    if (currentInside)
                        _clipScratch.Add(current);

                    // A vertex on the plane is the crossing point itself; adding a lerped one
                    // beside it is what used to leave slivers, and with both distances inside
                    // the epsilon the fraction could even fall outside the edge entirely.
                    if (!StrictlyCrosses(currentDistance, nextDistance, _planeEpsilon))
                        continue;

                    var cut = Vertex.Lerp(current, next, CrossingFraction(currentDistance, nextDistance));
                    _clipScratch.Add(cut);
                    if (currentInside)
                    {
                        firstCut = cut; // leaving the kept side: segment starts here
                    }
                    else if (firstCut.HasValue)
                    {
                        _cutSegments.Add((firstCut.Value, cut));
                        firstCut = null;
                    }
                    else
                    {
                        firstCut = cut; // loop started outside; pair up at the wrap-around exit
                        pendingEntry = cut;
                        hasPendingEntry = true;
                    }
                }

                // A polygon that started outside pairs its first entry with the last exit
                if (hasPendingEntry && firstCut.HasValue && !firstCut.Value.Equals(pendingEntry))
                    _cutSegments.Add((firstCut.Value, pendingEntry));

                if (_clipScratch.Count < 3)
                {
                    _polygonPool.Return(polygon);
                    _polygons.RemoveAt(polygonIndex);
                }
                else
                {
                    vertices.Clear();
                    vertices.AddRange(_clipScratch);
                }
            }

            return anyClipped;
        }

        /// <summary>
        /// Gathers the edges of the surface polygons that lie in the plane, reversed so they
        /// run in the cap's winding, into _cutSegments. An edge shared by two surface
        /// polygons (the plane runs exactly along a mesh edge) is interior to the surface,
        /// not a cut boundary, so such pairs cancel out - otherwise both sides would grow
        /// their own chain and the plane would get two identical caps.
        /// </summary>
        private void CollectPlaneSegments(Vector3 planeNormal, float planeOffset, int surfaceCount)
        {
            _cutSegments.Clear();
            for (var polygonIndex = 0; polygonIndex < surfaceCount; polygonIndex++)
            {
                CollectPlaneSegments(_polygons[polygonIndex], planeNormal, planeOffset);
            }

            // Fully kept polygons can still have an edge on the plane; they are the "other side" of such an edge
            foreach (var polygon in _kept)
            {
                CollectPlaneSegments(polygon, planeNormal, planeOffset);
            }

            for (var i = _cutSegments.Count - 1; i >= 0; i--)
            {
                var (from, to) = _cutSegments[i];
                for (var j = i - 1; j >= 0; j--)
                {
                    var (otherFrom, otherTo) = _cutSegments[j];
                    var same = Vector3.DistanceSquared(from.Position, otherFrom.Position) < _weldEpsilonSq
                               && Vector3.DistanceSquared(to.Position, otherTo.Position) < _weldEpsilonSq;
                    var reversed = Vector3.DistanceSquared(from.Position, otherTo.Position) < _weldEpsilonSq
                                   && Vector3.DistanceSquared(to.Position, otherFrom.Position) < _weldEpsilonSq;
                    if (!same && !reversed)
                        continue;

                    _cutSegments.RemoveAt(i);
                    _cutSegments.RemoveAt(j);
                    i--;
                    break;
                }
            }
        }

        private void CollectPlaneSegments(Polygon polygon, Vector3 planeNormal, float planeOffset)
        {
            var vertices = polygon.Vertices;
            var count = vertices.Count;
            for (var i = 0; i < count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % count];
                if (MathF.Abs(Vector3.Dot(planeNormal, a.Position) - planeOffset) > _onPlaneEpsilon
                    || MathF.Abs(Vector3.Dot(planeNormal, b.Position) - planeOffset) > _onPlaneEpsilon)
                    continue;

                if (Vector3.DistanceSquared(a.Position, b.Position) < DegenerateEpsilonSq)
                    continue;

                _cutSegments.Add((b, a));
            }
        }

        /// <summary>
        /// Builds the cap(s) of one plane: the part of the cell's face on that plane that lies
        /// inside the solid. The face is tessellated with every cut segment as a constraint (a
        /// degenerate there-and-back contour; the tessellator never lets an output edge cross
        /// an input edge), which splits it into regions bounded by cuts and hull edges. Each
        /// region is kept or dropped by probing the solid just inside the cell at its centroid.
        /// No chaining, no walking the hull, no assumption about the winding of the cuts: a
        /// concave cross section that meets the hull in several places yields several regions,
        /// and a region enclosed by a cut loop becomes a hole by the same probe.
        /// </summary>
        private void BuildCapsForPlane(Vector3 planeNormal, float planeOffset, int planeIndex, Polygon? hullFace,
                                       bool fillInterior, MeshInsideTester insideTester)
        {
            if (hullFace == null || hullFace.Vertices.Count < 3)
                return;

            // A face no surface crosses is solid through or empty; the interior fill decides
            // whether cells lying entirely inside the solid are wanted at all.
            if (_cutSegments.Count == 0 && !fillInterior)
                return;

            _hullLoop.Clear();
            foreach (var vertex in hullFace.Vertices)
            {
                _hullLoop.Add(vertex.Position);
            }

            if (Vector3.Dot(NewellNormal(_hullLoop), planeNormal) < 0)
                _hullLoop.Reverse();

            // Surface polygons lying in the plane close the cell there themselves. The segment
            // collector cancels their outline (a neighbour shares every edge, both on the plane),
            // so those outlines are added as constraints here, and the regions they cover are
            // recognised and skipped below.
            _coplanar.Clear();
            foreach (var polygon in _polygons)
            {
                if (!polygon.IsCap && LiesInPlane(polygon, planeNormal, planeOffset))
                    _coplanar.Add(polygon);
            }

            foreach (var polygon in _kept)
            {
                if (LiesInPlane(polygon, planeNormal, planeOffset))
                    _coplanar.Add(polygon);
            }

            // Every cut endpoint of any plane that lies on one of this face's edges becomes a
            // contour vertex, so neighbouring faces split their common edge the same way
            _hullContour.Clear();
            for (var i = 0; i < _hullLoop.Count; i++)
            {
                var a = _hullLoop[i];
                var b = _hullLoop[(i + 1) % _hullLoop.Count];
                _hullContour.Add(new ContourVertex(ToVec3(a)));
                var ab = b - a;
                var lengthSq = ab.LengthSquared();
                if (lengthSq < _weldEpsilonSq)
                    continue;

                // Inserted exactly on the edge line: a point a hair off it (a source vertex the
                // neighbouring plane's cut ends at) would bend the contour into a needle region.
                // Cut endpoints near a hull edge get the same projection below, so they agree.
                _edgePoints.Clear();
                foreach (var point in _cellEdgePoints)
                {
                    var t = Vector3.Dot(point - a, ab) / lengthSq;
                    if (t <= 0 || t >= 1)
                        continue;

                    var onEdge = a + ab * t;
                    if (Vector3.DistanceSquared(point, onEdge) > _weldEpsilonSq)
                        continue;

                    if (Vector3.DistanceSquared(onEdge, a) < _weldEpsilonSq || Vector3.DistanceSquared(onEdge, b) < _weldEpsilonSq)
                        continue;

                    _edgePoints.Add((t, onEdge));
                }

                _edgePoints.Sort((x, y) => x.T.CompareTo(y.T));
                foreach (var (_, point) in _edgePoints)
                {
                    _hullContour.Add(new ContourVertex(ToVec3(point)));
                }
            }

            var tess = new Tess();
            tess.AddContour(_hullContour.ToArray(), ContourOrientation.Original);
            // A two-vertex contour is discarded as degenerate, so each cut becomes a hair-thin
            // quad instead: its long edge is honoured by the tessellation, and the quad's own
            // region is recognised and dropped below. Endpoints on the hull are contour
            // vertices of the hull (inserted above with identical coordinates), so the quad
            // seals against it exactly; reaching past the end instead would cross a shallow
            // hull edge far from the endpoint and leave a stray vertex there.
            var constraintWidth = _weldEpsilon * 0.1f;
            _constraints.Clear();
            foreach (var (from, to) in _cutSegments)
            {
                AddConstraint(tess, SnapToHullEdge(from.Position), SnapToHullEdge(to.Position), planeNormal, constraintWidth);
            }

            foreach (var polygon in _coplanar)
            {
                var outline = polygon.Vertices;
                for (var i = 0; i < outline.Count; i++)
                {
                    AddConstraint(tess, outline[i].Position, outline[(i + 1) % outline.Count].Position, planeNormal, constraintWidth);
                }
            }

            tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, CapPolygonSize, null, ToVec3(planeNormal));

            // Triangles that touch across an edge that is not a cut belong to the same region
            // and share one verdict. Probing every triangle on its own fails for the thin ones
            // hugging a cut: nudged a little into the cell, their centroid is already outside.
            var vertices = tess.Vertices;
            var elements = tess.Elements;
            var triangleCount = tess.ElementCount;
            _regionOf.Clear();
            _regionArea.Clear();
            _regionBest.Clear();
            _regionBestPerimeter.Clear();
            for (var t = 0; t < triangleCount; t++)
            {
                _regionOf.Add(t);
                _regionArea.Add(0f);
                _regionBest.Add(Vector3.Zero);
                _regionBestPerimeter.Add(0f);
            }

            _edgeOwner.Clear();
            for (var t = 0; t < triangleCount; t++)
            {
                for (var k = 0; k < 3; k++)
                {
                    var a = elements[t * 3 + k];
                    var b = elements[t * 3 + (k + 1) % 3];
                    if (a == Tess.Undef || b == Tess.Undef)
                        continue;

                    var key = a < b ? (a, b) : (b, a);
                    if (!_edgeOwner.TryGetValue(key, out var other))
                    {
                        _edgeOwner[key] = t;
                        continue;
                    }

                    if (!IsOnCut(vertices[a].Position, vertices[b].Position, constraintWidth * 2))
                        Union(t, other);
                }
            }

            // Each region probes the solid at its largest triangle, a little into the cell
            // (the cell lies on the negative side of its planes)
            for (var t = 0; t < triangleCount; t++)
            {
                if (!TryGetTriangle(vertices, elements, t, out var p0, out var p1, out var p2))
                    continue;

                var area = Vector3.Cross(p1 - p0, p2 - p0).Length();
                var region = Find(t);
                if (area > _regionArea[region])
                {
                    _regionArea[region] = area;
                    _regionBest[region] = (p0 + p1 + p2) / 3f;
                    _regionBestPerimeter[region] = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p0);
                }
            }

            var probeOffset = planeNormal * (_weldEpsilon * 2);
            PlaneBasis(planeNormal, out var u, out var v);
            _regionInside.Clear();
            for (var t = 0; t < triangleCount; t++)
            {
                if (!TryGetTriangle(vertices, elements, t, out var p0, out var p1, out var p2))
                    continue;

                // Cut endpoints on one line (a plane meeting a flat face along it) give the
                // tessellator needle triangles with garbage normals; their height is float noise.
                // Real thin cap triangles are far taller than the constraint width.
                var normal = Vector3.Cross(p1 - p0, p2 - p0);
                var perimeter = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p0);
                if (normal.Length() < perimeter * constraintWidth || IsInsideConstraintQuad(p0, p1, p2, constraintWidth * 2))
                    continue;

                var region = Find(t);
                if (!_regionInside.TryGetValue(region, out var inside))
                {
                    // A region whose largest triangle is thinner than the weld tolerance is a
                    // sliver between a hull corner and the surface: its probe lands inside the
                    // solid by a hair and would claim a face that welding then folds onto the
                    // surface. Thin triangles inside real regions are unaffected.
                    var probe = _regionBest[region];
                    inside = _regionArea[region] >= _regionBestPerimeter[region] * _weldEpsilon * 0.5f
                             && !IsCoveredByCoplanar(probe, u, v)
                             && insideTester.IsInside(probe - probeOffset);
                    _regionInside[region] = inside;
                }

                if (!inside)
                    continue;

                if (!_regionTriangles.TryGetValue(region, out var list))
                {
                    list = [];
                    _regionTriangles[region] = list;
                }

                list.Add(t);
            }

            // A region is one planar face: emit it as a single polygon so downstream ops
            // that work per face (bevel, chunk pivots) see the cap the way the old builder
            // made it, not as a fan of triangles. Regions with holes or pinched outlines
            // fall back to their triangles.
            foreach (var (_, triangles) in _regionTriangles)
            {
                if (!TryEmitRegionPolygon(vertices, elements, triangles, planeNormal, planeIndex))
                {
                    foreach (var t in triangles)
                    {
                        TryGetTriangle(vertices, elements, t, out var p0, out var p1, out var p2);
                        EmitCap(planeNormal, planeIndex, p0, p1, p2);
                    }
                }
            }

            _regionTriangles.Clear();
        }

        private void EmitCap(Vector3 planeNormal, int planeIndex, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            var cap = _polygonPool.Rent();
            cap.IsCap = true;
            cap.PlaneIndex = planeIndex;
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), planeNormal) < 0)
                (p1, p2) = (p2, p1);

            cap.Vertices.Add(new Vertex(p0, planeNormal));
            cap.Vertices.Add(new Vertex(p1, planeNormal));
            cap.Vertices.Add(new Vertex(p2, planeNormal));
            _polygons.Add(cap);
            _planeCapped[planeIndex] = true;
        }

        /// <summary>
        /// Merges a region's triangles into one polygon by chaining its outer edges (edges
        /// used by exactly one triangle, directed with the cap's winding). Returns false
        /// when the outline isn't one simple loop - a hole, or a pinch point - so the caller
        /// keeps the triangles instead.
        /// </summary>
        private bool TryEmitRegionPolygon(ContourVertex[] vertices, int[] elements, List<int> triangles, Vector3 planeNormal, int planeIndex)
        {
            if (triangles.Count == 1)
            {
                TryGetTriangle(vertices, elements, triangles[0], out var q0, out var q1, out var q2);
                EmitCap(planeNormal, planeIndex, q0, q1, q2);
                return true;
            }

            // Directed edges of every triangle, wound with the plane normal; an interior edge
            // appears once in each direction, an outline edge only once.
            _directedEdges.Clear();
            foreach (var t in triangles)
            {
                var a = elements[t * 3];
                var b = elements[t * 3 + 1];
                var c = elements[t * 3 + 2];
                var pa = ToVector(vertices[a].Position);
                var pb = ToVector(vertices[b].Position);
                var pc = ToVector(vertices[c].Position);
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), planeNormal) < 0)
                    (b, c) = (c, b);

                _directedEdges.Add((a, b));
                _directedEdges.Add((b, c));
                _directedEdges.Add((c, a));
            }

            _nextOnOutline.Clear();
            foreach (var (a, b) in _directedEdges)
            {
                if (_directedEdges.Contains((b, a)))
                    continue;

                if (!_nextOnOutline.TryAdd(a, b))
                    return false; // two outline edges leave one vertex: pinched outline
            }

            if (_nextOnOutline.Count < 3)
                return false;

            // Walk the outline; one loop must visit every outline vertex, otherwise there are holes
            var start = -1;
            foreach (var key in _nextOnOutline.Keys)
            {
                start = key;
                break;
            }

            var cap = _polygonPool.Rent();
            cap.IsCap = true;
            cap.PlaneIndex = planeIndex;
            var current = start;
            for (var guard = 0; guard <= _nextOnOutline.Count; guard++)
            {
                cap.Vertices.Add(new Vertex(ToVector(vertices[current].Position), planeNormal));
                current = _nextOnOutline[current];
                if (current == start)
                    break;
            }

            if (cap.Vertices.Count != _nextOnOutline.Count)
            {
                _polygonPool.Return(cap);
                return false;
            }

            _polygons.Add(cap);
            _planeCapped[planeIndex] = true;
            return true;
        }

        private static Vector3 ToVector(Vec3 v) => new(v.X, v.Y, v.Z);

        /// <summary>A point within the weld tolerance of a hull edge, projected onto it (same arithmetic as the contour insertion).</summary>
        private Vector3 SnapToHullEdge(Vector3 point)
        {
            var count = _hullLoop.Count;
            for (var i = 0; i < count; i++)
            {
                var a = _hullLoop[i];
                var b = _hullLoop[(i + 1) % count];
                var ab = b - a;
                var lengthSq = ab.LengthSquared();
                if (lengthSq < _weldEpsilonSq)
                    continue;

                var t = Vector3.Dot(point - a, ab) / lengthSq;
                if (t <= 0 || t >= 1)
                    continue;

                var onEdge = a + ab * t;
                if (Vector3.DistanceSquared(point, onEdge) <= _weldEpsilonSq)
                    return onEdge;
            }

            return point;
        }

        /// <summary>One cut as a hair-thin quad; see BuildCapsForPlane.</summary>
        private void AddConstraint(Tess tess, Vector3 from, Vector3 to, Vector3 planeNormal, float width)
        {
            var along = to - from;
            if (along.LengthSquared() < DegenerateEpsilonSq)
                return;

            var direction = Vector3.Normalize(along);
            _constraints.Add((from, to));
            // Chamfered at 45 degrees: a perpendicular side edge would cross a hull edge or the
            // next cut, when those run at a shallow angle, far from the corner - beyond what
            // welding merges. With the chamfer every such crossing stays within a width or two
            // of the corner, and the offset corners themselves weld onto the base corners.
            var side = Vector3.Normalize(Vector3.Cross(planeNormal, direction)) * width;
            var inset = direction * MathF.Min(width, along.Length() * 0.25f);
            tess.AddContour([
                                new ContourVertex(ToVec3(from)),
                                new ContourVertex(ToVec3(to)),
                                new ContourVertex(ToVec3(to + side - inset)),
                                new ContourVertex(ToVec3(from + side + inset)),
                            ], ContourOrientation.Original);
        }

        private static bool TryGetTriangle(ContourVertex[] vertices, int[] elements, int t, out Vector3 p0, out Vector3 p1, out Vector3 p2)
        {
            var i0 = elements[t * 3];
            var i1 = elements[t * 3 + 1];
            var i2 = elements[t * 3 + 2];
            if (i0 == Tess.Undef || i1 == Tess.Undef || i2 == Tess.Undef)
            {
                p0 = p1 = p2 = default;
                return false;
            }

            p0 = ToVector3(vertices[i0].Position);
            p1 = ToVector3(vertices[i1].Position);
            p2 = ToVector3(vertices[i2].Position);
            return true;
        }

        /// <summary>A triangle whose three corners all lie within one constraint quad is the quad itself, not cap.</summary>
        private bool IsInsideConstraintQuad(Vector3 p0, Vector3 p1, Vector3 p2, float reach)
        {
            var reachSq = reach * reach;
            foreach (var (start, end) in _constraints)
            {
                if (DistanceToSegmentSq(p0, start, end) < reachSq
                    && DistanceToSegmentSq(p1, start, end) < reachSq
                    && DistanceToSegmentSq(p2, start, end) < reachSq)
                    return true;
            }

            return false;
        }

        /// <summary>True if both ends of an edge lie on the same cut segment - the edge then separates regions.</summary>
        private bool IsOnCut(Vec3 a, Vec3 b, float reach)
        {
            var pa = ToVector3(a);
            var pb = ToVector3(b);
            var reachSq = reach * reach;
            foreach (var (start, end) in _constraints)
            {
                if (DistanceToSegmentSq(pa, start, end) < reachSq
                    && DistanceToSegmentSq(pb, start, end) < reachSq)
                    return true;
            }

            return false;
        }

        private static float DistanceToSegmentSq(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var lengthSq = ab.LengthSquared();
            var t = lengthSq > 1e-20f ? Math.Clamp(Vector3.Dot(p - a, ab) / lengthSq, 0f, 1f) : 0f;
            return Vector3.DistanceSquared(p, a + ab * t);
        }

        private int Find(int t)
        {
            while (_regionOf[t] != t)
            {
                _regionOf[t] = _regionOf[_regionOf[t]];
                t = _regionOf[t];
            }

            return t;
        }

        private void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
                _regionOf[a] = b;
        }

        private void AddCellEdgePoint(Vector3 position)
        {
            foreach (var known in _cellEdgePoints)
            {
                if (Vector3.DistanceSquared(known, position) < _weldEpsilonSq)
                    return;
            }

            _cellEdgePoints.Add(position);
        }

        private bool LiesInPlane(Polygon polygon, Vector3 planeNormal, float planeOffset)
        {
            foreach (var vertex in polygon.Vertices)
            {
                if (MathF.Abs(Vector3.Dot(planeNormal, vertex.Position) - planeOffset) > _onPlaneEpsilon)
                    return false;
            }

            return polygon.Vertices.Count >= 3;
        }

        /// <summary>Crossing-number test of a point against the surface polygons lying in the plane.</summary>
        private bool IsCoveredByCoplanar(Vector3 point, Vector3 u, Vector3 v)
        {
            if (_coplanar.Count == 0)
                return false;

            var px = Vector3.Dot(point, u);
            var py = Vector3.Dot(point, v);
            foreach (var polygon in _coplanar)
            {
                var inside = false;
                var vertices = polygon.Vertices;
                for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
                {
                    var ax = Vector3.Dot(vertices[i].Position, u);
                    var ay = Vector3.Dot(vertices[i].Position, v);
                    var bx = Vector3.Dot(vertices[j].Position, u);
                    var by = Vector3.Dot(vertices[j].Position, v);
                    if (ay > py != by > py && px < (bx - ax) * (py - ay) / (by - ay) + ax)
                        inside = !inside;
                }

                if (inside)
                    return true;
            }

            return false;
        }

        private static void PlaneBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            var helper = MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            u = Vector3.Normalize(Vector3.Cross(normal, helper));
            v = Vector3.Cross(normal, u);
        }

        private static Vec3 ToVec3(Vector3 p) => new(p.X, p.Y, p.Z);
        private static Vector3 ToVector3(Vec3 p) => new(p.X, p.Y, p.Z);

        /// <summary>
        /// A cell face without any surface cut is solid if it borders a cap: its hull edges
        /// are then edges of neighbouring caps. Closing those faces needs no inside test
        /// and is order-independent; it repeats until nothing changes, so a run of cut-less
        /// faces is closed from the first one that borders a cap.
        /// </summary>
        private void CloseFacesBorderingCaps(int surfaceCount)
        {
            var added = true;
            while (added)
            {
                added = false;
                foreach (var hullFace in _hull)
                {
                    var planeIndex = hullFace.PlaneIndex;
                    if (planeIndex < 0 || _planeCapped[planeIndex] || hullFace.Vertices.Count < 3)
                        continue;

                    if (!SharesEdgeWithCap(hullFace, surfaceCount))
                        continue;

                    var cap = _polygonPool.Rent(hullFace);
                    cap.IsCap = true;
                    _polygons.Add(cap);
                    _planeCapped[planeIndex] = true;
                    added = true;
                }
            }
        }

        private bool SharesEdgeWithCap(Polygon hullFace, int surfaceCount)
        {
            var vertices = hullFace.Vertices;
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i].Position;
                var b = vertices[(i + 1) % vertices.Count].Position;
                for (var polygonIndex = surfaceCount; polygonIndex < _polygons.Count; polygonIndex++)
                {
                    var capVertices = _polygons[polygonIndex].Vertices;
                    for (var j = 0; j < capVertices.Count; j++)
                    {
                        var c = capVertices[j].Position;
                        var d = capVertices[(j + 1) % capVertices.Count].Position;
                        if ((Vector3.DistanceSquared(a, c) < _weldEpsilonSq && Vector3.DistanceSquared(b, d) < _weldEpsilonSq)
                            || (Vector3.DistanceSquared(a, d) < _weldEpsilonSq && Vector3.DistanceSquared(b, c) < _weldEpsilonSq))
                            return true;
                    }
                }
            }

            return false;
        }

        private static Vector3 NewellNormal(List<Vector3> loop)
        {
            var normal = Vector3.Zero;
            for (var i = 0; i < loop.Count; i++)
            {
                var p0 = loop[i];
                var p1 = loop[(i + 1) % loop.Count];
                normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                      (p0.Z - p1.Z) * (p0.X + p1.X),
                                      (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            return normal;
        }

        private static Vector3 NewellNormal(Polygon polygon)
        {
            var normal = Vector3.Zero;
            var count = polygon.Vertices.Count;
            for (var i = 0; i < count; i++)
            {
                var p0 = polygon.Vertices[i].Position;
                var p1 = polygon.Vertices[(i + 1) % count].Position;
                normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                      (p0.Z - p1.Z) * (p0.X + p1.X),
                                      (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            return normal;
        }

        /// <summary>
        /// Merges positions within the weld tolerance (relative to the mesh extent: four
        /// cells meeting at a nearly degenerate Voronoi vertex produce corner duplicates
        /// and slivers well above float precision). The bucket size equals the tolerance,
        /// so checking the neighbouring buckets covers the full radius.
        /// </summary>
        private int GetOrAddPoint(Vector3 position)
        {
            var (kx, ky, kz) = Quantize(position);
            for (var dz = -1; dz <= 1; dz++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                // Buckets chain all their points - a bucket holds many points at this tolerance
                if (!_pointLookup.TryGetValue((kx + dx, ky + dy, kz + dz), out var candidate))
                    continue;

                while (candidate >= 0)
                {
                    if (Vector3.DistanceSquared(_positions[candidate], position) < _weldEpsilonSq)
                        return candidate;

                    candidate = _nextInBucket[candidate];
                }
            }

            var pointId = _positions.Count;
            _positions.Add(position);
            _nextInBucket.Add(_pointLookup.TryGetValue((kx, ky, kz), out var head) ? head : -1);
            _pointLookup[(kx, ky, kz)] = pointId;
            return pointId;
        }

        private (int, int, int) Quantize(Vector3 position)
        {
            return ((int)MathF.Floor(position.X * _weldGridScale),
                    (int)MathF.Floor(position.Y * _weldGridScale),
                    (int)MathF.Floor(position.Z * _weldGridScale));
        }

        private bool[] _planeCapped = [];
        private float _weldEpsilon;
        private float _planeEpsilon;
        private float _onPlaneEpsilon;
        private float _weldEpsilonSq;
        private float _weldGridScale;
        private readonly List<Vector3> _hullLoop = [];
        private readonly List<Polygon> _coplanar = [];
        private readonly List<(Vector3 Start, Vector3 End)> _constraints = [];
        private readonly List<List<(Vertex From, Vertex To)>> _planeSegments = [];
        private readonly List<Vector3> _cellEdgePoints = [];
        private readonly List<ContourVertex> _hullContour = [];
        private readonly List<(float T, Vector3 Position)> _edgePoints = [];
        private readonly List<int> _regionOf = [];
        private readonly List<float> _regionArea = [];
        private readonly List<Vector3> _regionBest = [];
        private readonly List<float> _regionBestPerimeter = [];
        private readonly Dictionary<int, List<int>> _regionTriangles = [];
        private readonly HashSet<(int, int)> _directedEdges = [];
        private readonly Dictionary<int, int> _nextOnOutline = [];
        private readonly Dictionary<int, bool> _regionInside = [];
        private readonly Dictionary<(int, int), int> _edgeOwner = [];
        private readonly PolygonPool _polygonPool = new();
        private readonly List<Polygon> _polygons = [];
        private readonly List<(Vector3 Normal, float Offset)> _planes = [];
        private readonly List<Polygon> _kept = []; // source polygons by reference - never returned to the pool
        private readonly List<Polygon> _hull = [];
        private int _stamp;
        private int[] _stamps = [];
        private readonly List<Vertex> _clipScratch = [];
        private readonly List<(Vertex From, Vertex To)> _cutSegments = [];
        private readonly Dictionary<(int, int, int), int> _pointLookup = [];
        private readonly Dictionary<(int, int), int> _edgeUse = [];
        private readonly HashSet<long> _faceKeys = [];
        private readonly List<bool> _keepFace = [];
        private readonly Dictionary<int, int> _holeEdges = [];
        private readonly HashSet<int> _surfaceHoleEdges = [];
        private readonly HashSet<int> _holeUsed = [];
        private readonly List<int> _holeLoop = [];
        private readonly List<int> _nextInBucket = [];
        private int[] _order = [];
        private float[] _distances = [];
        private readonly List<Vector3> _positions = [];
        private readonly List<int> _corners = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<int> _faceOffsets = [];
        private readonly List<bool> _isCap = [];
    }

    /// <summary>
    /// Read-only view of the source mesh for the cell builders: clip-ready polygons, their
    /// AABBs, the mesh bounds, and a uniform grid so a cell only touches the polygons in
    /// its region instead of the whole mesh.
    /// </summary>
    private sealed class SourceIndex
    {
        public SourceIndex(MeshGeometry source, GeometryAttribute<Vector3>? cornerNormals)
        {
            var offsets = source.FaceCornerOffsets;
            var corners = source.CornerPointIndices;
            _polygons = new Polygon[source.FaceCount];
            _polygonMin = new Vector3[source.FaceCount];
            _polygonMax = new Vector3[source.FaceCount];
            Min = new Vector3(float.MaxValue);
            Max = new Vector3(float.MinValue);

            for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
            {
                var polygon = new Polygon { IsCap = false };
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                for (var c = offsets[faceIndex]; c < offsets[faceIndex + 1]; c++)
                {
                    var position = source.Positions[corners[c]];
                    var normal = cornerNormals != null ? cornerNormals.Values[c] : Vector3.Zero;
                    polygon.Vertices.Add(new Vertex(position, normal));
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }

                _polygons[faceIndex] = polygon;
                _polygonMin[faceIndex] = min;
                _polygonMax[faceIndex] = max;
                Min = Vector3.Min(Min, min);
                Max = Vector3.Max(Max, max);
            }

            // Slightly padded bounds so the hull box strictly contains every vertex
            // Only a hair of padding: hull edges must coincide with flat faces lying in the
            // bounding-box planes, otherwise cut chains ending there sit "almost" on the hull
            // and the walk inserts near-duplicate box corners (hairline slivers, open edges).
            var padding = new Vector3(1e-6f);
            Min -= padding;
            Max += padding;

            // Uniform grid: roughly one cell per few polygons along each axis
            var extent = Max - Min;
            var resolution = Math.Clamp((int)MathF.Ceiling(MathF.Cbrt(source.FaceCount / 4f)), 1, 64);
            _gridResolution = resolution;
            _cellSize = new Vector3(MathF.Max(extent.X, 1e-6f) / resolution,
                                    MathF.Max(extent.Y, 1e-6f) / resolution,
                                    MathF.Max(extent.Z, 1e-6f) / resolution);

            var cellLists = new List<int>[resolution * resolution * resolution];
            for (var faceIndex = 0; faceIndex < source.FaceCount; faceIndex++)
            {
                ToGrid(_polygonMin[faceIndex], out var x0, out var y0, out var z0);
                ToGrid(_polygonMax[faceIndex], out var x1, out var y1, out var z1);
                for (var z = z0; z <= z1; z++)
                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var cell = (z * resolution + y) * resolution + x;
                    (cellLists[cell] ??= []).Add(faceIndex);
                }
            }

            // Flatten into CSR for allocation-free lookup
            _gridOffsets = new int[cellLists.Length + 1];
            for (var i = 0; i < cellLists.Length; i++)
            {
                _gridOffsets[i + 1] = _gridOffsets[i] + (cellLists[i]?.Count ?? 0);
            }

            _gridEntries = new int[_gridOffsets[^1]];
            for (var i = 0; i < cellLists.Length; i++)
            {
                cellLists[i]?.CopyTo(_gridEntries, _gridOffsets[i]);
            }
        }

        public Vector3 Min { get; }
        public Vector3 Max { get; }
        public float Extent => MathF.Max(MathF.Max(Max.X - Min.X, Max.Y - Min.Y), Max.Z - Min.Z);
        public int PolygonCount => _polygons.Length;

        /// <summary>The padded mesh bounding box as six outward-facing quads - the seed for a cell hull.</summary>
        public void AddBoundsBox(List<Polygon> target, PolygonPool pool)
        {
            var min = Min;
            var max = Max;
            Span<Vector3> c = stackalloc Vector3[8];
            c[0] = new Vector3(min.X, min.Y, min.Z);
            c[1] = new Vector3(max.X, min.Y, min.Z);
            c[2] = new Vector3(max.X, max.Y, min.Z);
            c[3] = new Vector3(min.X, max.Y, min.Z);
            c[4] = new Vector3(min.X, min.Y, max.Z);
            c[5] = new Vector3(max.X, min.Y, max.Z);
            c[6] = new Vector3(max.X, max.Y, max.Z);
            c[7] = new Vector3(min.X, max.Y, max.Z);
            AddQuad(target, pool, c[0], c[3], c[2], c[1]); // -Z
            AddQuad(target, pool, c[4], c[5], c[6], c[7]); // +Z
            AddQuad(target, pool, c[0], c[1], c[5], c[4]); // -Y
            AddQuad(target, pool, c[3], c[7], c[6], c[2]); // +Y
            AddQuad(target, pool, c[0], c[4], c[7], c[3]); // -X
            AddQuad(target, pool, c[1], c[2], c[6], c[5]); // +X
        }

        private static void AddQuad(List<Polygon> target, PolygonPool pool, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var polygon = pool.Rent();
            polygon.Vertices.Add(new Vertex(a, Vector3.Zero));
            polygon.Vertices.Add(new Vertex(b, Vector3.Zero));
            polygon.Vertices.Add(new Vertex(c, Vector3.Zero));
            polygon.Vertices.Add(new Vertex(d, Vector3.Zero));
            target.Add(polygon);
        }

        /// <summary>
        /// Sorts the source polygons of the cell region into <paramref name="kept"/> (entirely
        /// inside every plane, referenced without copying) and <paramref name="toClip"/>
        /// (straddling at least one plane, rented copies). Grid cells and polygons that lie
        /// outside any plane are rejected by their AABB before a single vertex is touched.
        /// </summary>
        public void CollectCandidates(Vector3 boxMin, Vector3 boxMax, List<(Vector3 Normal, float Offset)> planes, float planeEpsilon,
                                      List<Polygon> kept, List<Polygon> toClip, PolygonPool pool,
                                      ref int stamp, ref int[] stamps)
        {
            if (stamps.Length < _polygons.Length)
                stamps = new int[_polygons.Length];

            stamp++;
            ToGrid(boxMin, out var x0, out var y0, out var z0);
            ToGrid(boxMax, out var x1, out var y1, out var z1);
            for (var z = z0; z <= z1; z++)
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var cell = (z * _gridResolution + y) * _gridResolution + x;
                if (_gridOffsets[cell + 1] == _gridOffsets[cell])
                    continue; // empty cells are the majority of a boundary cell's AABB - skip before any plane math

                var gridMin = Min + new Vector3(x * _cellSize.X, y * _cellSize.Y, z * _cellSize.Z);
                var gridMax = gridMin + _cellSize;
                if (IsBoxOutsideAnyPlane(gridMin, gridMax, planes, planeEpsilon))
                    continue;

                for (var e = _gridOffsets[cell]; e < _gridOffsets[cell + 1]; e++)
                {
                    var faceIndex = _gridEntries[e];
                    if (stamps[faceIndex] == stamp)
                        continue;

                    stamps[faceIndex] = stamp;
                    var pMin = _polygonMin[faceIndex];
                    var pMax = _polygonMax[faceIndex];
                    if (pMax.X < boxMin.X || pMin.X > boxMax.X
                        || pMax.Y < boxMin.Y || pMin.Y > boxMax.Y
                        || pMax.Z < boxMin.Z || pMin.Z > boxMax.Z)
                        continue;

                    if (IsBoxOutsideAnyPlane(pMin, pMax, planes, planeEpsilon))
                        continue;

                    var polygon = _polygons[faceIndex];
                    if (IsPolygonInsideAllPlanes(polygon, planes, planeEpsilon))
                        kept.Add(polygon);
                    else
                        toClip.Add(pool.Rent(polygon));
                }
            }
        }

        /// <summary>Conservative box-vs-halfspace test using the box corner nearest to each plane.</summary>
        private static bool IsBoxOutsideAnyPlane(Vector3 min, Vector3 max, List<(Vector3 Normal, float Offset)> planes, float planeEpsilon)
        {
            foreach (var (n, offset) in planes)
            {
                var nearest = new Vector3(n.X > 0 ? min.X : max.X,
                                          n.Y > 0 ? min.Y : max.Y,
                                          n.Z > 0 ? min.Z : max.Z);
                if (Vector3.Dot(n, nearest) - offset > planeEpsilon)
                    return true;
            }

            return false;
        }

        private static bool IsPolygonInsideAllPlanes(Polygon polygon, List<(Vector3 Normal, float Offset)> planes, float planeEpsilon)
        {
            foreach (var (n, offset) in planes)
            {
                foreach (var vertex in polygon.Vertices)
                {
                    if (Vector3.Dot(n, vertex.Position) - offset > planeEpsilon)
                        return false;
                }
            }

            return true;
        }

        private void ToGrid(Vector3 position, out int x, out int y, out int z)
        {
            var local = position - Min;
            x = Math.Clamp((int)(local.X / _cellSize.X), 0, _gridResolution - 1);
            y = Math.Clamp((int)(local.Y / _cellSize.Y), 0, _gridResolution - 1);
            z = Math.Clamp((int)(local.Z / _cellSize.Z), 0, _gridResolution - 1);
        }

        private readonly Polygon[] _polygons;
        private readonly Vector3[] _polygonMin;
        private readonly Vector3[] _polygonMax;
        private readonly int _gridResolution;
        private readonly Vector3 _cellSize;
        private readonly int[] _gridOffsets;
        private readonly int[] _gridEntries;
    }

    /// <summary>Recycles polygon objects across cells - cloning the whole mesh per cell otherwise dominates GC.</summary>
    private sealed class PolygonPool
    {
        public Polygon Rent()
        {
            if (_free.Count == 0)
                return new Polygon();

            var polygon = _free[^1];
            _free.RemoveAt(_free.Count - 1);
            polygon.Vertices.Clear();
            polygon.IsCap = false;
            polygon.PlaneIndex = -1;
            return polygon;
        }

        public Polygon Rent(Polygon template)
        {
            var polygon = Rent();
            polygon.IsCap = template.IsCap;
            polygon.PlaneIndex = template.PlaneIndex;
            polygon.Vertices.AddRange(template.Vertices);
            return polygon;
        }

        public void Return(Polygon polygon) => _free.Add(polygon);

        public void ReturnAll(List<Polygon> polygons)
        {
            _free.AddRange(polygons);
            polygons.Clear();
        }

        private readonly List<Polygon> _free = [];
    }

    private readonly record struct Vertex(Vector3 Position, Vector3 Normal)
    {
        public static Vertex Lerp(in Vertex a, in Vertex b, float t)
        {
            var normal = Vector3.Lerp(a.Normal, b.Normal, t);
            if (normal.LengthSquared() > 1e-10f)
                normal = Vector3.Normalize(normal);
            return new Vertex(Vector3.Lerp(a.Position, b.Position, t), normal);
        }
    }

    private sealed class Polygon
    {
        public readonly List<Vertex> Vertices = [];
        public bool IsCap;
        public int PlaneIndex = -1; // for caps: which cell plane created them; -1 for surface and box faces
    }

    private const int CapPolygonSize = 3; // triangles: merging across a constraint edge would undo the cut
    private const float ClipToleranceFactor = 1e-6f; // of the mesh extent; below this a vertex counts as lying in the plane
    private const float OnPlaneToleranceFactor = 1e-5f; // of the mesh extent; edges within this are cut boundaries
    private const float WeldToleranceFactor = 1e-3f; // of the mesh extent; slivers below that merge
    private const float DegenerateEpsilonSq = 1e-7f * 1e-7f;

    public bool TryGetProgress(out float progress) => _asyncComputation.TryGetUiProgress(out progress);

    private readonly MeshGeometry _output = new();
    private readonly AsyncComputation<MeshGeometry> _asyncComputation = new();
    private readonly List<Vector3> _seeds = [];
    private readonly MeshGeometryStats _sourceStats = new();

    [Input(Guid = "31c7e9d4-85f2-4a60-b1c8-6d0a5e3f9b27")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "84a2f6c0-19db-4e75-93a4-c7e1b8d25f06")]
    public readonly InputSlot<StructuredList> Points = new();

    [Input(Guid = "5e0d8b36-a2c7-4f91-b840-97c3e6d1a528")]
    public readonly InputSlot<bool> Async = new();

    [Input(Guid = "7c4e1b90-52d8-4a36-9f1e-b8d0a6c3e574")]
    public readonly InputSlot<bool> FillInterior = new();
}

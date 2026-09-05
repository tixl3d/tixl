using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lib.Utils;
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
            _chainEpsilonSq = _weldEpsilonSq;
            _mergeEpsilonSq = _weldEpsilonSq;
            _hullEpsilonSq = _weldEpsilonSq;
            // Bridging gaps between cut chains is a repair for numerical drift, so it may span
            // a few weld tolerances but never a real distance: a plane through a concave shape
            // cuts several disjoint loops, and joining those to each other destroys the cap.
            _stitchEpsilonSq = _weldEpsilonSq * StitchToleranceFactor * StitchToleranceFactor;
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

                CollectPlaneSegments(planeNormal, planeOffset, surfaceCount);
                BuildCapsForPlane(planeNormal, planeIndex, hullFace, fillInterior, insideTester);
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
        /// Builds the cap(s) of one plane from the cut segments. Closed chains become caps
        /// directly; open chains are closed by walking along the plane's convex hull face,
        /// which is where the cap is bounded by other planes rather than by the surface.
        /// Without any segment the whole hull face is the cap if it lies inside the solid.
        /// </summary>
        private void BuildCapsForPlane(Vector3 planeNormal, int planeIndex, Polygon? hullFace, bool fillInterior,
                                       MeshInsideTester insideTester)
        {
            if (_cutSegments.Count == 0)
            {
                if (fillInterior && hullFace != null && insideTester.IsInside(Centroid(hullFace)))
                {
                    var cap = _polygonPool.Rent(hullFace);
                    cap.IsCap = true;
                    _polygons.Add(cap);
                    _planeCapped[planeIndex] = true;
                }

                return;
            }

            // A chain shorter than the weld tolerance is a plane grazing the surface at a
            // vertex: no usable cut, so the face is closed like a cut-less one.
            if (_cutSegments.Count == 1
                && Vector3.DistanceSquared(_cutSegments[0].From.Position, _cutSegments[0].To.Position) < _weldEpsilonSq)
            {
                _cutSegments.Clear();
                BuildCapsForPlane(planeNormal, planeIndex, hullFace, fillInterior, insideTester);
                return;
            }

            // Chain segments into runs
            var segmentCount = _cutSegments.Count;
            if (_segmentUsed.Length < segmentCount)
                _segmentUsed = new bool[segmentCount];
            Array.Clear(_segmentUsed, 0, segmentCount);

            _chains.Clear();
            for (var startIndex = 0; startIndex < segmentCount; startIndex++)
            {
                if (_segmentUsed[startIndex])
                    continue;

                var chain = new Chain();
                var currentIndex = startIndex;
                for (var guard = 0; guard <= segmentCount; guard++)
                {
                    _segmentUsed[currentIndex] = true;
                    var segment = _cutSegments[currentIndex];
                    chain.Points.Add(segment.From.Position);
                    chain.End = segment.To.Position;

                    var nextIndex = -1;
                    var bestDistanceSq = _chainEpsilonSq;
                    for (var candidate = 0; candidate < segmentCount; candidate++)
                    {
                        if (_segmentUsed[candidate])
                            continue;

                        var distanceSq = Vector3.DistanceSquared(segment.To.Position, _cutSegments[candidate].From.Position);
                        if (distanceSq < bestDistanceSq)
                        {
                            bestDistanceSq = distanceSq;
                            nextIndex = candidate;
                        }
                    }

                    if (nextIndex < 0)
                        break;

                    currentIndex = nextIndex;
                }

                // The walk started at an arbitrary segment, so a loop may have been split into
                // the part after it and the part before it. Extend backwards as well, or the
                // two halves end up as separate chains that only merge by luck of the order.
                for (var guard = 0; guard <= segmentCount; guard++)
                {
                    var previousIndex = -1;
                    var bestDistanceSq = _chainEpsilonSq;
                    for (var candidate = 0; candidate < segmentCount; candidate++)
                    {
                        if (_segmentUsed[candidate])
                            continue;

                        var distanceSq = Vector3.DistanceSquared(_cutSegments[candidate].To.Position, chain.Points[0]);
                        if (distanceSq < bestDistanceSq)
                        {
                            bestDistanceSq = distanceSq;
                            previousIndex = candidate;
                        }
                    }

                    if (previousIndex < 0)
                        break;

                    _segmentUsed[previousIndex] = true;
                    chain.Points.Insert(0, _cutSegments[previousIndex].From.Position);
                }

                chain.IsClosed = Vector3.DistanceSquared(chain.End, chain.Points[0]) < _chainEpsilonSq;
                _chains.Add(chain);
            }

            // Hull face oriented like the cap (counter-clockwise about the plane normal)
            var canWalkHull = fillInterior && hullFace != null;
            if (canWalkHull)
            {
                _hullLoop.Clear();
                foreach (var vertex in hullFace!.Vertices)
                {
                    _hullLoop.Add(vertex.Position);
                }

                if (Vector3.Dot(NewellNormal(_hullLoop), planeNormal) < 0)
                    _hullLoop.Reverse();
            }

            foreach (var chain in _chains)
            {
                chain.StartOnHull = canWalkHull && IsOnHullBoundary(chain.Points[0]);
                chain.EndOnHull = canWalkHull && IsOnHullBoundary(chain.End);
            }

            // Second pass: stitch broken surface chains. A chain end that is not on the hull
            // boundary continues on another chain's start that isn't either, so pair those by
            // proximity - looser than the strict chaining tolerance, but still bounded.
            for (var i = 0; i < _chains.Count; i++)
            {
                var chain = _chains[i];
                if (chain.Consumed || chain.IsClosed)
                    continue;

                for (var guard = 0; guard < _chains.Count; guard++)
                {
                    if (chain.EndOnHull)
                        break;

                    Chain? best = null;
                    var bestDistanceSq = _stitchEpsilonSq;
                    foreach (var other in _chains)
                    {
                        if (other == chain || other.Consumed || other.IsClosed || other.StartOnHull)
                            continue;

                        var distanceSq = Vector3.DistanceSquared(chain.End, other.Points[0]);
                        if (distanceSq < bestDistanceSq)
                        {
                            bestDistanceSq = distanceSq;
                            best = other;
                        }
                    }

                    if (best == null)
                        break;

                    chain.Points.AddRange(best.Points);
                    chain.End = best.End;
                    chain.EndOnHull = best.EndOnHull;
                    best.Consumed = true;
                    best.Points.Clear();
                }

                chain.IsClosed = Vector3.DistanceSquared(chain.End, chain.Points[0]) < _mergeEpsilonSq;
            }

            foreach (var chain in _chains)
            {
                if (chain.Consumed)
                    continue;

                chain.Consumed = true;
                var cap = _polygonPool.Rent();
                cap.IsCap = true;
                cap.PlaneIndex = planeIndex;
                AppendChain(cap, chain, planeNormal);

                var closed = chain.IsClosed;
                if (!closed && canWalkHull && chain.EndOnHull)
                {
                    _walkNormal = planeNormal;
                    // Walk the hull boundary from this chain's end to the next chain start,
                    // merging chains until the loop returns to where it began.
                    var current = chain;
                    for (var guard = 0; guard < _chains.Count + 1; guard++)
                    {
                        var next = WalkHullToNextChain(current.End, cap, chain, insideTester);
                        if (next == null)
                            break;

                        if (next == chain)
                        {
                            closed = true;
                            break;
                        }

                        next.Consumed = true;
                        AppendChain(cap, next, planeNormal);
                        current = next;
                        if (current.IsClosed)
                        {
                            closed = true;
                            break;
                        }
                    }
                }

                // An open chain with both ends on the hull closes with a straight edge back to
                // its start. Along one hull edge that is the true boundary. When the walk gave up
                // because the hull leaves the solid, the edge crosses the empty part of a concave
                // cross section and the face is misshapen - but skipping it does not help: the
                // hole filler then patches the same gap with a fan that looks worse. The real cap
                // here is the hull face intersected with the cross section (a tessellation job).
                closed |= chain.StartOnHull && chain.EndOnHull;

                if (!closed || cap.Vertices.Count < 3 || IsSliver(cap))
                {
                    _polygonPool.Return(cap);
                    continue;
                }

                if (Vector3.Dot(NewellNormal(cap), planeNormal) < 0)
                    cap.Vertices.Reverse();

                _polygons.Add(cap);
                _planeCapped[planeIndex] = true;
            }
        }

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

        /// <summary>
        /// Chain points are segment starts only; an open chain's end is a real cap corner too
        /// (the hull walk or the implicit closing edge continues from it), so it must be emitted.
        /// </summary>
        private static void AppendChain(Polygon cap, Chain chain, Vector3 planeNormal)
        {
            foreach (var p in chain.Points)
            {
                cap.Vertices.Add(new Vertex(p, planeNormal));
            }

            if (!chain.IsClosed)
                cap.Vertices.Add(new Vertex(chain.End, planeNormal));
        }

        /// <summary>
        /// From a point on the hull boundary, walks forward along the hull loop (adding the
        /// hull corners passed) until reaching the start of an unconsumed chain, which is
        /// returned. Returns null if the hull can't be walked.
        /// </summary>
        private Chain? WalkHullToNextChain(Vector3 from, Polygon cap, Chain origin, MeshInsideTester insideTester)
        {
            var hullCount = _hullLoop.Count;
            if (hullCount < 3)
                return null;

            FindHullEdge(from, out var edgeIndex, out var edgeT);
            var position = from;
            for (var step = 0; step <= hullCount; step++)
            {
                // The nearest chain start ahead on the current edge. The originating chain
                // is a valid target too - reaching its start is what closes the loop.
                Chain? best = null;
                var bestT = float.MaxValue;
                foreach (var candidate in _chains)
                {
                    if ((candidate.Consumed && candidate != origin) || !candidate.StartOnHull)
                        continue;

                    // A start within weld distance of the current position is the same point:
                    // its parametric position may round to slightly behind, and skipping it would
                    // send the walk once around the whole hull and build a duplicate cap.
                    if (step == 0 && Vector3.DistanceSquared(candidate.Points[0], from) < _weldEpsilonSq)
                        return candidate;

                    FindHullEdge(candidate.Points[0], out var candidateEdge, out var candidateT);
                    if (candidateEdge != edgeIndex || candidateT + 1e-5f < edgeT)
                        continue;

                    if (candidateT < bestT)
                    {
                        bestT = candidateT;
                        best = candidate;
                    }
                }

                if (best != null)
                    return best;

                // Otherwise continue around the hull corner - but the hull only bounds the cap
                // where it runs through solid material. On a concave cross section the cap is
                // several disjoint regions, and walking out of the solid to find a chain start
                // is what used to fuse them into one polygon spanning the whole model.
                var corner = _hullLoop[(edgeIndex + 1) % hullCount];
                if (!insideTester.IsInside((position + corner) * 0.5f))
                    return null;

                edgeIndex = (edgeIndex + 1) % hullCount;
                edgeT = 0;
                position = corner;
                cap.Vertices.Add(new Vertex(corner, _walkNormal));
            }

            return null;
        }

        private bool IsOnHullBoundary(Vector3 point)
        {
            FindHullEdge(point, out var edgeIndex, out var t);
            var a = _hullLoop[edgeIndex];
            var b = _hullLoop[(edgeIndex + 1) % _hullLoop.Count];
            return Vector3.DistanceSquared(point, a + (b - a) * t) < _hullEpsilonSq;
        }

        /// <summary>Nearest hull edge to a point, with the parametric position along it.</summary>
        private void FindHullEdge(Vector3 point, out int edgeIndex, out float t)
        {
            edgeIndex = 0;
            t = 0;
            var bestDistanceSq = float.MaxValue;
            var hullCount = _hullLoop.Count;
            for (var i = 0; i < hullCount; i++)
            {
                var a = _hullLoop[i];
                var b = _hullLoop[(i + 1) % hullCount];
                var ab = b - a;
                var lengthSq = ab.LengthSquared();
                var candidateT = lengthSq > 1e-12f ? Math.Clamp(Vector3.Dot(point - a, ab) / lengthSq, 0f, 1f) : 0f;
                var distanceSq = Vector3.DistanceSquared(point, a + ab * candidateT);
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    edgeIndex = i;
                    t = candidateT;
                }
            }
        }

        /// <summary>A cap thinner than the weld tolerance across its whole length is a line,
        /// not a face - typically a chain running along a hull edge on a plane the surface lies in.</summary>
        private bool IsSliver(Polygon cap)
        {
            var perimeter = 0f;
            var vertices = cap.Vertices;
            for (var i = 0; i < vertices.Count; i++)
                perimeter += Vector3.Distance(vertices[i].Position, vertices[(i + 1) % vertices.Count].Position);

            var area = NewellNormal(cap).Length() * 0.5f;
            return area < perimeter * _weldEpsilon * 0.5f;
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

        private sealed class Chain
        {
            public readonly List<Vector3> Points = [];
            public Vector3 End;
            public bool IsClosed;
            public bool Consumed;
            public bool StartOnHull;
            public bool EndOnHull;
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
        private float _stitchEpsilonSq;
        private float _chainEpsilonSq;
        private float _mergeEpsilonSq;
        private float _hullEpsilonSq;
        private float _weldEpsilonSq;
        private float _weldGridScale;
        private readonly List<Chain> _chains = [];
        private readonly List<Vector3> _hullLoop = [];
        private Vector3 _walkNormal;
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
        private bool[] _segmentUsed = [];
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

    private const float ClipToleranceFactor = 1e-6f; // of the mesh extent; below this a vertex counts as lying in the plane
    private const float OnPlaneToleranceFactor = 1e-5f; // of the mesh extent; edges within this are cut boundaries
    private const float StitchToleranceFactor = 8f; // of the weld tolerance; the largest gap between cut chains that is still drift
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

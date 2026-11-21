using System;
using System.Collections.Generic;
using System.Numerics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;

namespace Lib.physics;

/// <summary>
/// Approximate convex decomposition of a triangle soup, so a concave object can collide as a compound of convex hulls
/// instead of one hull that fills its holes. The shape is voxelized as a solid, then split best-first along axis
/// planes: the piece whose hull wastes the most volume is cut where the two halves' hulls waste the least, until every
/// piece is close to convex or the hull budget is spent. Each resulting hull is built from the real surface inside it,
/// so outer faces stay exact and only the cuts between hulls follow the voxel grid.
/// </summary>
internal static class PhysicsDecomposition
{
    /** Share of a hull's volume that may lie outside the solid before the piece counts as concave. */
    public const float AutoConcavityTolerance = 0.12f;

    /** Tolerance used when the decomposition was asked for explicitly: only nearly exact hulls stop the splitting. */
    public const float ForcedConcavityTolerance = 0.03f;

    /** Candidate cut positions tried per axis when splitting a piece. */
    private const int CutCandidatesPerAxis = 7;

    /** Weight of unequal halves against wasted hull volume, both relative to the piece. */
    private const float BalanceWeight = 0.05f;

    /** Most voxels along the longest axis of a piece. */
    private const int Resolution = 40;

    /** Islands beyond this many are wrapped in one hull instead; a piece made of crumbs gains nothing from a hull each. */
    private const int MaxIslands = 64;

    /// <summary>
    /// Splits the solid enclosed by <paramref name="triangles"/> (three points per triangle) into at most
    /// <paramref name="maxHulls"/> convex point sets. Returns null when one hull is within <paramref name="tolerance"/>
    /// of the shape, or the triangles do not enclose anything a voxel grid can resolve.
    /// </summary>
    public static Vector3[][] Decompose(ReadOnlySpan<Vector3> triangles, int maxHulls, float tolerance, BufferPool pool)
    {
        if (maxHulls < 2 || triangles.Length < 12)
            return null;

        var grid = VoxelGrid.Build(triangles);
        if (grid == null)
            return null;

        var leaves = new List<Piece>();
        foreach (var island in grid.Components(grid.FilledCells()))
            leaves.Add(new Piece(island, grid, pool));

        if (leaves.Count == 0 || leaves.Count > MaxIslands)
            return null;

        if (leaves.Count == 1 && leaves[0].Concavity <= tolerance)
            return null;

        while (leaves.Count < maxHulls)
        {
            var worstIndex = -1;
            var worstWaste = 0f;
            for (var index = 0; index < leaves.Count; index++)
            {
                var leaf = leaves[index];
                if (leaf.IsFinal || leaf.Concavity <= tolerance || leaf.Waste <= worstWaste)
                    continue;

                worstWaste = leaf.Waste;
                worstIndex = index;
            }

            if (worstIndex < 0)
                break;

            var split = BestSplit(leaves[worstIndex], grid, pool);
            if (split == null || leaves.Count - 1 + split.Count > maxHulls)
            {
                leaves[worstIndex].IsFinal = true;
                continue;
            }

            leaves.RemoveAt(worstIndex);
            leaves.AddRange(split);
        }

        for (var index = 0; index < leaves.Count; index++)
            grid.Assign(leaves[index].Cells, index);

        var pointSets = grid.HullPoints(leaves.Count, triangles);
        var result = new List<Vector3[]>(leaves.Count);
        foreach (var set in pointSets)
        {
            var points = set.ToArray();
            if (points.Length >= 4 && HullVolume(points, pool) > 0f)
                result.Add(points);
        }

        return result.Count > 1 ? result.ToArray() : null;
    }

    /// <summary>Volume of the convex hull around <paramref name="points"/>, or 0 when they don't enclose any.</summary>
    public static float HullVolume(ReadOnlySpan<Vector3> points, BufferPool pool)
    {
        if (points.Length < 4)
            return 0f;

        var copy = points.ToArray();
        try
        {
            ConvexHullHelper.ComputeHull(copy, pool, out var hull);
            try
            {
                return VolumeOf(copy, hull);
            }
            finally
            {
                hull.Dispose(pool);
            }
        }
        catch (Exception)
        {
            // Bepu's hull builder throws on some degenerate inputs; those enclose nothing worth a collider.
            return 0f;
        }
    }

    /// <summary>
    /// Appends the faces of the convex hull around <paramref name="points"/>, offset by <paramref name="offset"/>, as outward
    /// polygons: positions, corner point indices and face corner offsets in the layout of a <see cref="T3.Core.DataTypes.MeshGeometry"/>.
    /// Returns the number of faces added; 0 when the points don't enclose a volume.
    /// </summary>
    public static int AppendHullFaces(ReadOnlySpan<Vector3> points, Vector3 offset, BufferPool pool, List<Vector3> positions, List<int> corners, List<int> faceOffsets)
    {
        if (points.Length < 4)
            return 0;

        var copy = points.ToArray();
        HullData hull;
        try
        {
            ConvexHullHelper.ComputeHull(copy, pool, out hull);
        }
        catch (Exception)
        {
            return 0;
        }

        try
        {
            var mapping = hull.OriginalVertexMapping;
            if (mapping.Length < 4)
                return 0;

            var center = Vector3.Zero;
            var pointBase = positions.Count;
            for (var index = 0; index < mapping.Length; index++)
            {
                center += copy[mapping[index]];
                positions.Add(copy[mapping[index]] + offset);
            }

            center /= mapping.Length;
            var faceCount = hull.FaceStartIndices.Length;
            var added = 0;
            for (var face = 0; face < faceCount; face++)
            {
                var start = hull.FaceStartIndices[face];
                var end = face + 1 < faceCount ? hull.FaceStartIndices[face + 1] : hull.FaceVertexIndices.Length;
                if (end - start < 3)
                    continue;

                var a = copy[mapping[hull.FaceVertexIndices[start]]];
                var normal = Vector3.Zero;
                for (var corner = start + 1; corner + 1 < end; corner++)
                    normal += Vector3.Cross(copy[mapping[hull.FaceVertexIndices[corner]]] - a, copy[mapping[hull.FaceVertexIndices[corner + 1]]] - a);

                // Bepu doesn't promise a winding, so turn every face to look away from the centre.
                var outward = Vector3.Dot(normal, a - center) >= 0f;
                for (var step = 0; step < end - start; step++)
                {
                    var corner = outward ? start + step : end - 1 - step;
                    corners.Add(pointBase + hull.FaceVertexIndices[corner]);
                }

                faceOffsets.Add(corners.Count);
                added++;
            }

            return added;
        }
        finally
        {
            hull.Dispose(pool);
        }
    }

    private static float VolumeOf(Vector3[] points, in HullData hull)
    {
        var mapping = hull.OriginalVertexMapping;
        if (mapping.Length < 4)
            return 0f;

        var center = Vector3.Zero;
        for (var index = 0; index < mapping.Length; index++)
            center += points[mapping[index]];

        center /= mapping.Length;

        var volume = 0f;
        var faceCount = hull.FaceStartIndices.Length;
        for (var face = 0; face < faceCount; face++)
        {
            var start = hull.FaceStartIndices[face];
            var end = face + 1 < faceCount ? hull.FaceStartIndices[face + 1] : hull.FaceVertexIndices.Length;
            if (end - start < 3)
                continue;

            var a = points[mapping[hull.FaceVertexIndices[start]]] - center;
            for (var corner = start + 1; corner + 1 < end; corner++)
            {
                var b = points[mapping[hull.FaceVertexIndices[corner]]] - center;
                var c = points[mapping[hull.FaceVertexIndices[corner + 1]]] - center;
                volume += MathF.Abs(Vector3.Dot(a, Vector3.Cross(b, c)));
            }
        }

        return volume / 6f;
    }

    /// <summary>
    /// The axis cut of <paramref name="piece"/> whose islands waste the least hull volume, with a small preference for
    /// halves of equal size. The balance term decides between cuts that waste the same: halving a ring wastes as much as
    /// slicing a thin layer off its top, but only the halves can be cut again into arcs that free the hole. Null when the
    /// piece cannot be cut into two.
    /// </summary>
    private static List<Piece> BestSplit(Piece piece, VoxelGrid grid, BufferPool pool)
    {
        List<Piece> best = null;
        var bestCost = float.MaxValue;
        var hullVolume = MathF.Max(piece.Waste + piece.Cells.Length * grid.CellVolume, 1e-12f);
        Span<int> min = stackalloc int[3];
        Span<int> max = stackalloc int[3];
        grid.Bounds(piece.Cells, min, max);

        for (var axis = 0; axis < 3; axis++)
        {
            var length = max[axis] - min[axis] + 1;
            if (length < 2)
                continue;

            var candidates = Math.Min(CutCandidatesPerAxis, length - 1);
            for (var candidate = 1; candidate <= candidates; candidate++)
            {
                var cut = min[axis] + (int)MathF.Round(candidate * length / (float)(candidates + 1));
                if (cut <= min[axis] || cut > max[axis])
                    continue;

                var pieces = new List<Piece>();
                var waste = 0f;
                var below = 0;
                var above = 0;
                var halfIndex = 0;
                foreach (var half in grid.SplitAt(piece.Cells, axis, cut))
                {
                    if (halfIndex++ == 0)
                        below = half.Count;
                    else
                        above = half.Count;

                    foreach (var island in grid.Components(half))
                    {
                        var part = new Piece(island, grid, pool);
                        pieces.Add(part);
                        waste += part.Waste;
                    }
                }

                if (pieces.Count < 2)
                    continue;

                var imbalance = MathF.Abs(below - above) / (float)piece.Cells.Length;
                var cost = waste / hullVolume + BalanceWeight * imbalance;
                if (cost >= bestCost)
                    continue;

                bestCost = cost;
                best = pieces;
            }
        }

        return best;
    }

    /// <summary>A connected set of solid voxels and how far its hull is from convex.</summary>
    private sealed class Piece
    {
        public Piece(int[] cells, VoxelGrid grid, BufferPool pool)
        {
            Cells = cells;
            var solid = cells.Length * grid.CellVolume;
            var hull = HullVolume(grid.RowEndCorners(cells), pool);
            Waste = MathF.Max(0f, hull - solid);
            Concavity = hull > 0f ? Waste / hull : 0f;
        }

        public readonly int[] Cells;

        /** Hull volume not covered by the solid. */
        public readonly float Waste;

        public readonly float Concavity;

        /** Set when no cut improves the piece, so it is not tried again. */
        public bool IsFinal;
    }

    /// <summary>Solid voxelization of a triangle soup with the bookkeeping the splitter needs.</summary>
    private sealed class VoxelGrid
    {
        public static VoxelGrid Build(ReadOnlySpan<Vector3> triangles)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var point in triangles)
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                    return null;

                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            var extent = max - min;
            var longest = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
            if (!(longest > 1e-6f))
                return null;

            // One cell of margin on every side keeps the surface away from the grid border.
            var cell = longest / Resolution;
            var grid = new VoxelGrid(min - new Vector3(cell), cell,
                                     (int)MathF.Ceiling(extent.X / cell) + 2,
                                     (int)MathF.Ceiling(extent.Y / cell) + 2,
                                     (int)MathF.Ceiling(extent.Z / cell) + 2);
            grid.FillInterior(triangles);
            grid.MarkSurface(triangles);
            return grid;
        }

        private VoxelGrid(Vector3 origin, float cell, int sizeX, int sizeY, int sizeZ)
        {
            _origin = origin;
            _cell = cell;
            _size = (sizeX, sizeY, sizeZ);
            _owner = new int[sizeX * sizeY * sizeZ];
            Array.Fill(_owner, Empty);
            _visit = new int[_owner.Length];
            _rowMin = new int[sizeY * sizeZ];
            _rowMax = new int[sizeY * sizeZ];
            _rowStamp = new int[sizeY * sizeZ];
        }

        public float CellVolume => _cell * _cell * _cell;

        public IEnumerable<int> FilledCells()
        {
            for (var index = 0; index < _owner.Length; index++)
            {
                if (_owner[index] != Empty)
                    yield return index;
            }
        }

        /// <summary>The 6-connected islands of a cell set.</summary>
        public List<int[]> Components(IEnumerable<int> cells)
        {
            var member = ++_stamp;
            var list = new List<int>();
            foreach (var cell in cells)
            {
                _visit[cell] = member;
                list.Add(cell);
            }

            var islands = new List<int[]>();
            var queue = new Queue<int>();
            var island = new List<int>();
            foreach (var start in list)
            {
                if (_visit[start] != member)
                    continue;

                var seen = ++_stamp;
                _visit[start] = seen;
                queue.Enqueue(start);
                island.Clear();
                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    island.Add(cell);
                    var (x, y, z) = Coordinates(cell);
                    Visit(x - 1, y, z, member, seen, queue);
                    Visit(x + 1, y, z, member, seen, queue);
                    Visit(x, y - 1, z, member, seen, queue);
                    Visit(x, y + 1, z, member, seen, queue);
                    Visit(x, y, z - 1, member, seen, queue);
                    Visit(x, y, z + 1, member, seen, queue);
                }

                islands.Add(island.ToArray());
            }

            return islands;
        }

        public IEnumerable<List<int>> SplitAt(int[] cells, int axis, int cut)
        {
            var below = new List<int>();
            var above = new List<int>();
            foreach (var cell in cells)
            {
                var (x, y, z) = Coordinates(cell);
                var coordinate = axis == 0 ? x : axis == 1 ? y : z;
                (coordinate < cut ? below : above).Add(cell);
            }

            yield return below;
            yield return above;
        }

        public void Bounds(int[] cells, Span<int> min, Span<int> max)
        {
            min[0] = min[1] = min[2] = int.MaxValue;
            max[0] = max[1] = max[2] = int.MinValue;
            foreach (var cell in cells)
            {
                var (x, y, z) = Coordinates(cell);
                min[0] = Math.Min(min[0], x);
                min[1] = Math.Min(min[1], y);
                min[2] = Math.Min(min[2], z);
                max[0] = Math.Max(max[0], x);
                max[1] = Math.Max(max[1], y);
                max[2] = Math.Max(max[2], z);
            }
        }

        /// <summary>
        /// Corners of the first and last cell of every X row of the set. Every other cell corner lies between two of these,
        /// so their hull is the hull of the whole voxel set at a fraction of the points.
        /// </summary>
        public Vector3[] RowEndCorners(int[] cells)
        {
            var stamp = ++_stamp;
            var rows = new List<int>();
            foreach (var cell in cells)
            {
                var (x, y, z) = Coordinates(cell);
                var row = y + z * _size.Y;
                if (_rowStamp[row] != stamp)
                {
                    _rowStamp[row] = stamp;
                    _rowMin[row] = x;
                    _rowMax[row] = x;
                    rows.Add(row);
                    continue;
                }

                _rowMin[row] = Math.Min(_rowMin[row], x);
                _rowMax[row] = Math.Max(_rowMax[row], x);
            }

            var corners = new Vector3[rows.Count * 8];
            var write = 0;
            foreach (var row in rows)
            {
                var y = row % _size.Y;
                var z = row / _size.Y;
                foreach (var x in (ReadOnlySpan<int>)[_rowMin[row], _rowMax[row] + 1])
                {
                    corners[write++] = Corner(x, y, z);
                    corners[write++] = Corner(x, y + 1, z);
                    corners[write++] = Corner(x, y, z + 1);
                    corners[write++] = Corner(x, y + 1, z + 1);
                }
            }

            return corners;
        }

        public void Assign(int[] cells, int owner)
        {
            foreach (var cell in cells)
                _owner[cell] = owner;
        }

        /// <summary>
        /// The points of every assigned piece: the surface that falls into its cells, plus the faces it shares with other
        /// pieces, so neighbouring hulls meet at the cut instead of leaving a gap.
        /// </summary>
        public List<Vector3>[] HullPoints(int pieceCount, ReadOnlySpan<Vector3> triangles)
        {
            var points = new List<Vector3>[pieceCount];
            for (var piece = 0; piece < pieceCount; piece++)
                points[piece] = new List<Vector3>();

            // One sample per half cell is as much detail as the grid resolves; dense meshes would otherwise hand the hull
            // builder hundreds of thousands of points.
            var spacing = _cell * 0.5f;
            var taken = new HashSet<(int, int, int)>();
            for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                var a = triangles[triangle];
                var b = triangles[triangle + 1];
                var c = triangles[triangle + 2];
                var steps = StepsFor(a, b, c, spacing);
                for (var u = 0; u <= steps; u++)
                {
                    for (var v = 0; u + v <= steps; v++)
                    {
                        var point = a + (b - a) * (u / (float)steps) + (c - a) * (v / (float)steps);
                        var owner = OwnerAt(point);
                        if ((uint)owner >= (uint)pieceCount)
                            continue;

                        var local = (point - _origin) / spacing;
                        if (taken.Add(((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y), (int)MathF.Floor(local.Z))))
                            points[owner].Add(point);
                    }
                }
            }

            var faceCorners = new HashSet<Vector3>();
            for (var piece = 0; piece < pieceCount; piece++)
            {
                faceCorners.Clear();
                for (var cell = 0; cell < _owner.Length; cell++)
                {
                    if (_owner[cell] != piece)
                        continue;

                    var (x, y, z) = Coordinates(cell);
                    AddSharedFaces(x, y, z, 0, piece, faceCorners);
                    AddSharedFaces(x, y, z, 1, piece, faceCorners);
                    AddSharedFaces(x, y, z, 2, piece, faceCorners);
                }

                points[piece].AddRange(faceCorners);
            }

            return points;
        }

        /// <summary>Adds the corners of the faces between a cell and its neighbours along one axis that belong to another piece.</summary>
        private void AddSharedFaces(int x, int y, int z, int axis, int owner, HashSet<Vector3> corners)
        {
            for (var side = 0; side < 2; side++)
            {
                var step = side == 0 ? -1 : 1;
                var nx = x + (axis == 0 ? step : 0);
                var ny = y + (axis == 1 ? step : 0);
                var nz = z + (axis == 2 ? step : 0);
                if (!InRange(nx, ny, nz))
                    continue;

                var otherOwner = _owner[Index(nx, ny, nz)];
                if (otherOwner < 0 || otherOwner == owner)
                    continue;

                // The face lies on the cell's low side for the negative neighbour and its high side for the positive one.
                var plane = side == 0 ? 0 : 1;
                for (var corner = 0; corner < 4; corner++)
                {
                    var first = corner & 1;
                    var second = corner >> 1;
                    corners.Add(axis switch
                                    {
                                        0 => Corner(x + plane, y + first, z + second),
                                        1 => Corner(x + first, y + plane, z + second),
                                        _ => Corner(x + first, y + second, z + plane),
                                    });
                }
            }
        }

        /// <summary>
        /// Winding fill along X: a cell is inside when the surface crossings to the left of its centre don't cancel out.
        /// Unlike parity this keeps overlapping shells solid, which models assembled from several closed meshes have.
        /// </summary>
        private void FillInterior(ReadOnlySpan<Vector3> triangles)
        {
            var rowCount = _size.Y * _size.Z;
            var crossings = new List<(float X, int Direction)>[rowCount];

            // An off-grid offset keeps the row rays from running exactly through shared edges, which would count twice.
            const float jitter = 0.0137f;
            for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                var a = (triangles[triangle] - _origin) / _cell;
                var b = (triangles[triangle + 1] - _origin) / _cell;
                var c = (triangles[triangle + 2] - _origin) / _cell;
                var yStart = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) - 0.5f));
                var yEnd = Math.Min(_size.Y - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - 0.5f));
                var zStart = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) - 0.5f));
                var zEnd = Math.Min(_size.Z - 1, (int)MathF.Ceiling(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) - 0.5f));

                // The sign of the projected area is the sign of the normal's X: entering or leaving the solid.
                var denominator = (b.Y - a.Y) * (c.Z - a.Z) - (c.Y - a.Y) * (b.Z - a.Z);
                if (MathF.Abs(denominator) < 1e-12f)
                    continue;

                var direction = denominator > 0f ? 1 : -1;
                for (var z = zStart; z <= zEnd; z++)
                {
                    for (var y = yStart; y <= yEnd; y++)
                    {
                        var py = y + 0.5f + jitter;
                        var pz = z + 0.5f + jitter * 0.61f;
                        var u = ((py - a.Y) * (c.Z - a.Z) - (c.Y - a.Y) * (pz - a.Z)) / denominator;
                        var v = ((b.Y - a.Y) * (pz - a.Z) - (py - a.Y) * (b.Z - a.Z)) / denominator;
                        if (u < 0f || v < 0f || u + v > 1f)
                            continue;

                        var row = y + z * _size.Y;
                        (crossings[row] ??= new List<(float, int)>()).Add((a.X + u * (b.X - a.X) + v * (c.X - a.X), direction));
                    }
                }
            }

            for (var row = 0; row < rowCount; row++)
            {
                var list = crossings[row];
                if (list == null)
                    continue;

                // Crossings that don't cancel mean the surface is open along this row; its surface cells still get marked.
                var total = 0;
                foreach (var crossing in list)
                    total += crossing.Direction;

                if (total != 0)
                    continue;

                list.Sort(static (first, second) => first.X.CompareTo(second.X));
                var y = row % _size.Y;
                var z = row / _size.Y;
                var winding = 0;
                for (var index = 0; index + 1 < list.Count; index++)
                {
                    winding += list[index].Direction;
                    if (winding == 0)
                        continue;

                    var start = Math.Max(0, (int)MathF.Ceiling(list[index].X - 0.5f));
                    var end = Math.Min(_size.X - 1, (int)MathF.Floor(list[index + 1].X - 0.5f));
                    for (var x = start; x <= end; x++)
                        _owner[Index(x, y, z)] = Filled;
                }
            }
        }

        private void MarkSurface(ReadOnlySpan<Vector3> triangles)
        {
            var spacing = _cell * 0.5f;
            for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                var a = triangles[triangle];
                var b = triangles[triangle + 1];
                var c = triangles[triangle + 2];
                var steps = StepsFor(a, b, c, spacing);
                for (var u = 0; u <= steps; u++)
                {
                    for (var v = 0; u + v <= steps; v++)
                    {
                        var point = a + (b - a) * (u / (float)steps) + (c - a) * (v / (float)steps);
                        var index = CellAt(point);
                        if (index >= 0)
                            _owner[index] = Filled;
                    }
                }
            }
        }

        private static int StepsFor(Vector3 a, Vector3 b, Vector3 c, float spacing)
        {
            var longest = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
            return Math.Clamp((int)MathF.Ceiling(longest / spacing), 1, 512);
        }

        private int OwnerAt(Vector3 point)
        {
            var index = CellAt(point);
            return index >= 0 ? _owner[index] : Empty;
        }

        private int CellAt(Vector3 point)
        {
            var local = (point - _origin) / _cell;
            var x = (int)MathF.Floor(local.X);
            var y = (int)MathF.Floor(local.Y);
            var z = (int)MathF.Floor(local.Z);
            return InRange(x, y, z) ? Index(x, y, z) : -1;
        }

        private void Visit(int x, int y, int z, int member, int seen, Queue<int> queue)
        {
            if (!InRange(x, y, z))
                return;

            var index = Index(x, y, z);
            if (_visit[index] != member)
                return;

            _visit[index] = seen;
            queue.Enqueue(index);
        }

        private Vector3 Corner(int x, int y, int z) => _origin + new Vector3(x, y, z) * _cell;

        private bool InRange(int x, int y, int z) => (uint)x < (uint)_size.X && (uint)y < (uint)_size.Y && (uint)z < (uint)_size.Z;

        private int Index(int x, int y, int z) => x + (y + z * _size.Y) * _size.X;

        private (int X, int Y, int Z) Coordinates(int index)
        {
            var x = index % _size.X;
            var rest = index / _size.X;
            return (x, rest % _size.Y, rest / _size.Y);
        }

        private const int Empty = -1;
        private const int Filled = -2;

        private readonly Vector3 _origin;
        private readonly float _cell;
        private readonly (int X, int Y, int Z) _size;

        /** Per cell: Empty, Filled, or the index of the piece it was assigned to. */
        private readonly int[] _owner;

        private readonly int[] _visit;
        private readonly int[] _rowMin;
        private readonly int[] _rowMax;
        private readonly int[] _rowStamp;
        private int _stamp;
    }
}

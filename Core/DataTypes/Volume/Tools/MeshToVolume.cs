#nullable enable
using System;
using T3.Core.DataTypes.Volume.Tools;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.DataTypes;
using T3.Core.Logging;

namespace T3.Core.DataTypes.Volume;

/// <summary>
/// Bounding-volume hierarchy over mesh triangles for closest-point queries,
/// the acceleration behind <see cref="MeshToVolume"/>. Median split over triangle centroids.
/// </summary>
public sealed class MeshBvh
{
    private readonly Vector3[] _positions;
    private readonly int[] _triangles;
    private readonly int[] _order;      // triangle ids in BVH leaf order
    private readonly Vector3[] _faceNormals;
    private readonly Node[] _nodes;
    private readonly int _root;

    private readonly struct Node
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;
        public readonly int Left;   // -1 for leaves
        public readonly int Right;
        public readonly int Start;  // triangle range (leaves)
        public readonly int Count;

        public Node(Vector3 min, Vector3 max, int left, int right, int start, int count)
        {
            Min = min;
            Max = max;
            Left = left;
            Right = right;
            Start = start;
            Count = count;
        }
    }

    public MeshBvh(Vector3[] positions, int[] triangles)
    {
        _positions = positions;
        _triangles = triangles;
        _faceNormals = new Vector3[TriangleCount];
        var indices = new int[TriangleCount];
        var centroids = new Vector3[TriangleCount];
        var boxes = new (Vector3 Min, Vector3 Max)[TriangleCount];

        for (var t = 0; t < TriangleCount; t++)
        {
            var a = _positions[_triangles[t * 3]];
            var b = _positions[_triangles[t * 3 + 1]];
            var c = _positions[_triangles[t * 3 + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            var length = cross.Length();
            _faceNormals[t] = length > 1e-12f ? cross / length : Vector3.UnitZ;
            boxes[t] = (Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
            centroids[t] = (a + b + c) / 3f;
            indices[t] = t;
        }

        var nodeList = new List<Node>(TriangleCount * 2);
        _root = Build(indices, boxes, centroids, 0, TriangleCount, nodeList, out _);
        _nodes = nodeList.ToArray();
        _order = indices;
    }

    public int TriangleCount => _triangles.Length / 3;

    private int Build(int[] indices, (Vector3 Min, Vector3 Max)[] boxes, Vector3[] centroids,
                      int start, int count, List<Node> nodes, out Vector3 nodeMin)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var cMin = new Vector3(float.MaxValue);
        var cMax = new Vector3(float.MinValue);
        for (var i = start; i < start + count; i++)
        {
            var t = indices[i];
            min = Vector3.Min(min, boxes[t].Min);
            max = Vector3.Max(max, boxes[t].Max);
            cMin = Vector3.Min(cMin, centroids[t]);
            cMax = Vector3.Max(cMax, centroids[t]);
        }

        nodeMin = min;
        var nodeIndex = nodes.Count;
        nodes.Add(new Node(min, max, -1, -1, start, count));

        if (count <= 4)
            return nodeIndex;

        var extent = cMax - cMin;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        var mid = count / 2;
        SelectByAxis(indices, boxes, centroids, start, count, mid, axis, cMin, cMax);

        nodes.Add(new Node()); // reserve right slot first so left build can append
        var left = Build(indices, boxes, centroids, start, mid, nodes, out _);
        var right = Build(indices, boxes, centroids, start + mid, count - mid, nodes, out _);
        nodes[nodeIndex] = new Node(nodes[nodeIndex].Min, nodes[nodeIndex].Max, left, right, start, count);
        return nodeIndex;
    }

    /// <summary>Nth-element style partition of triangles by centroid along one axis.</summary>
    private static void SelectByAxis(int[] indices, (Vector3 Min, Vector3 Max)[] boxes, Vector3[] centroids,
                                     int start, int count, int mid, int axis, Vector3 cMin, Vector3 cMax)
    {
        var target = (axis == 0 ? cMin.X + cMax.X : axis == 1 ? cMin.Y + cMax.Y : cMin.Z + cMax.Z) * 0.5f;
        var lo = start;
        var hi = start + count - 1;
        while (lo < hi)
        {
            while (lo < hi && CentroidAxis(centroids[indices[lo]], axis) < target)
                lo++;
            while (lo < hi && CentroidAxis(centroids[indices[hi]], axis) >= target)
                hi--;
            if (lo < hi)
                (indices[lo], indices[hi]) = (indices[hi], indices[lo]);
        }

        if (lo == start || lo == start + count)
        {
            // Degenerate split (all centroids on one side): force a midpoint split by index.
            (indices[start], indices[start + count - 1]) = (indices[start + count - 1], indices[start]);
            return;
        }

        _ = mid;
    }

    private static float CentroidAxis(in Vector3 c, int axis) => axis == 0 ? c.X : axis == 1 ? c.Y : c.Z;

    private static float SquaredDistanceToBox(in Vector3 p, in Vector3 min, in Vector3 max)
    {
        var dx = MathF.Max(MathF.Max(min.X - p.X, p.X - max.X), 0);
        var dy = MathF.Max(MathF.Max(min.Y - p.Y, p.Y - max.Y), 0);
        var dz = MathF.Max(MathF.Max(min.Z - p.Z, p.Z - max.Z), 0);
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>
    /// Closest triangle to <paramref name="p"/>. Outputs the squared distance, the hit triangle
    /// index, the closest point, and a feature code (0 face, 1-3 vertices, 4-6 edges) for
    /// pseudo-normal sign computation.
    /// </summary>
    public float ClosestPoint(Vector3 p, out int triangleIndex, out Vector3 closest, out int feature)
    {
        var best = float.MaxValue;
        triangleIndex = -1;
        closest = Vector3.Zero;
        feature = 0;

        var stack = _nodes.Length > 64 ? new int[64] : new int[_nodes.Length];
        var sp = 0;
        stack[sp++] = _root;

        while (sp > 0)
        {
            ref readonly var node = ref _nodes[stack[--sp]];
            if (SquaredDistanceToBox(p, node.Min, node.Max) >= best)
                continue;

            if (node.Left < 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var t = _order[i];
                    var hit = ClosestPointOnTriangle(p,
                                                     _positions[_triangles[t * 3]],
                                                     _positions[_triangles[t * 3 + 1]],
                                                     _positions[_triangles[t * 3 + 2]],
                                                     out var candidate, out var candidateFeature);
                    if (hit < best)
                    {
                        best = hit;
                        triangleIndex = t;
                        closest = candidate;
                        feature = candidateFeature;
                    }
                }
            }
            else
            {
                var dl = SquaredDistanceToBox(p, _nodes[node.Left].Min, _nodes[node.Left].Max);
                var dr = SquaredDistanceToBox(p, _nodes[node.Right].Min, _nodes[node.Right].Max);
                if (sp + 2 >= stack.Length)
                    Array.Resize(ref stack, stack.Length * 2 + 2);

                if (dl <= dr)
                {
                    stack[sp++] = node.Right;
                    stack[sp++] = node.Left;
                }
                else
                {
                    stack[sp++] = node.Left;
                    stack[sp++] = node.Right;
                }
            }
        }

        return best;
    }

    /// <summary>Ericson's closest-point-on-triangle; feature tells which simplex element won.</summary>
    public static float ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c,
                                               out Vector3 closest, out int feature)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;

        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            closest = a;
            feature = 1;
            return ap.LengthSquared();
        }

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            closest = b;
            feature = 2;
            return bp.LengthSquared();
        }

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            var v = d1 / (d1 - d3);
            closest = a + ab * v;
            feature = 4;
            return (p - closest).LengthSquared();
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            closest = c;
            feature = 3;
            return cp.LengthSquared();
        }

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            var w = d2 / (d2 - d6);
            closest = a + ac * w;
            feature = 5;
            return (p - closest).LengthSquared();
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            closest = b + (c - b) * w;
            feature = 6;
            return (p - closest).LengthSquared();
        }

        var denom = 1f / (va + vb + vc);
        var v2 = vb * denom;
        var w2 = vc * denom;
        closest = a + ab * v2 + ac * w2;
        feature = 0;
        return (p - closest).LengthSquared();
    }

    /// <summary>Angle-weighted pseudo-normal at the given closest feature; sign of dot(p - closest, n) classifies inside/outside for watertight meshes.</summary>
    public Vector3 PseudoNormal(int triangleIndex, in Vector3 closest, int feature, Vector3[] positions, int[] corners)
    {
        switch (feature)
        {
            case 0:
                return _faceNormals[triangleIndex];
            case 1:
            case 2:
            case 3:
            {
                var pointIndex = corners[triangleIndex * 3 + (feature - 1)];
                return VertexPseudoNormal(pointIndex, positions);
            }
            case 4:
                return EdgePseudoNormal(corners[triangleIndex * 3], corners[triangleIndex * 3 + 1]);
            case 5:
                return EdgePseudoNormal(corners[triangleIndex * 3], corners[triangleIndex * 3 + 2]);
            case 6:
                return EdgePseudoNormal(corners[triangleIndex * 3 + 1], corners[triangleIndex * 3 + 2]);
            default:
                return _faceNormals[triangleIndex];
        }
    }

    private Dictionary<int, (float Angle, int Triangle)>[]? _vertexTable;

    private Vector3 VertexPseudoNormal(int pointIndex, Vector3[] positions)
    {
        var table = _vertexTable ??= BuildVertexTable();
        var sum = Vector3.Zero;
        if (pointIndex < table.Length)
        {
            foreach (var entry in table[pointIndex].Values)
                sum += _faceNormals[entry.Triangle] * entry.Angle;
        }

        var length = sum.Length();
        return length > 1e-12f ? sum / length : Vector3.UnitY;
    }

    private Dictionary<int, (float Angle, int Triangle)>[] BuildVertexTable()
    {
        var list = new Dictionary<int, (float, int)>[256];
        for (var t = 0; t < TriangleCount; t++)
        {
            var a = _positions[_triangles[t * 3]];
            var b = _positions[_triangles[t * 3 + 1]];
            var c = _positions[_triangles[t * 3 + 2]];
            var angleA = AngleBetween(b - a, c - a);
            var angleB = AngleBetween(a - b, c - b);
            var angleC = AngleBetween(a - c, b - c);
            Accumulate(list, _triangles[t * 3], angleA, t);
            Accumulate(list, _triangles[t * 3 + 1], angleB, t);
            Accumulate(list, _triangles[t * 3 + 2], angleC, t);
        }

        return list;

        static float AngleBetween(in Vector3 u, in Vector3 v)
        {
            var denom = u.Length() * v.Length();
            if (denom < 1e-12f)
                return 0f;
            return MathF.Acos(Math.Clamp(Vector3.Dot(u, v) / denom, -1f, 1f));
        }

        static void Accumulate(Dictionary<int, (float, int)>[] list, int pointIndex, float angle, int triangle)
        {
            if (pointIndex >= list.Length)
                Array.Resize(ref list, pointIndex + 1);
            var entry = list[pointIndex] ??= new Dictionary<int, (float, int)>();
            entry[triangle] = (angle, triangle);
        }
    }

private Dictionary<(int, int), List<int>>? _edgeFaces;

    private Vector3 EdgePseudoNormal(int pointA, int pointB)
    {
        var key = pointA < pointB ? (pointA, pointB) : (pointB, pointA);
        _edgeFaces ??= BuildEdgeFaces();
        var sum = Vector3.Zero;
        if (_edgeFaces.TryGetValue(key, out var faces))
        {
            foreach (var t in faces)
                sum += _faceNormals[t];
        }
        else
        {
            // Boundary edge (open mesh): fall back to its face normal, found via the triangle table.
            for (var t = 0; t < TriangleCount; t++)
            {
                var i0 = _triangles[t * 3];
                var i1 = _triangles[t * 3 + 1];
                var i2 = _triangles[t * 3 + 2];
                if ((i0 == key.Item1 && i1 == key.Item2) || (i1 == key.Item1 && i2 == key.Item2) || (i2 == key.Item1 && i0 == key.Item2)
                    || (i0 == key.Item2 && i1 == key.Item1) || (i1 == key.Item2 && i2 == key.Item1) || (i2 == key.Item2 && i0 == key.Item1))
                {
                    sum = _faceNormals[t];
                    break;
                }
            }
        }

        var length = sum.Length();
        return length > 1e-12f ? sum / length : Vector3.UnitY;
    }

    private Dictionary<(int, int), List<int>> BuildEdgeFaces()
    {
        var table = new Dictionary<(int, int), List<int>>();
        for (var t = 0; t < TriangleCount; t++)
        {
            AddEdge(table, _triangles[t * 3], _triangles[t * 3 + 1], t);
            AddEdge(table, _triangles[t * 3 + 1], _triangles[t * 3 + 2], t);
            AddEdge(table, _triangles[t * 3 + 2], _triangles[t * 3], t);
        }

        return table;

        static void AddEdge(Dictionary<(int, int), List<int>> table, int a, int b, int t)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!table.TryGetValue(key, out var list))
                table[key] = list = new List<int>(2);
            list.Add(t);
        }
    }

    public Vector3 FaceNormal(int triangleIndex) => _faceNormals[triangleIndex];
}

public enum VdbVolumeType
{
    Sdf,
    Fog,
}

public static class MeshToVolume
{
    /// <summary>
    /// Converts a triangle soup into an SDF or fog volume. Distance is computed exactly against
    /// a BVH within a narrow band of <paramref name="halfWidthVoxels"/>; the sign uses the
    /// angle-weighted pseudo-normal (robust on watertight meshes). Fog output stores density 0..1.
    /// </summary>
    public static VdbGrid<float> Convert(Vector3[] positions, int[] cornerPointIndices,
                                         float voxelSize, int halfWidthVoxels, VdbVolumeType volumeType,
                                         Vector3? translation = null)
    {
        if (positions == null || cornerPointIndices == null || cornerPointIndices.Length < 3)
            throw new ArgumentException("Mesh needs at least one triangle");

        voxelSize = Math.Clamp(voxelSize, 1e-5f, 1e4f);
        var band = Math.Clamp(halfWidthVoxels, 1, 64);

        // Fan-triangulate faces into a corner-index soup.
        var corners = new List<int>(cornerPointIndices.Length);
        var faceOffsets = cornerPointIndices.Length; // caller passes CSR-style when coming from MeshGeometry
        _ = faceOffsets;
        return ConvertCorners(positions, cornerPointIndices, voxelSize, band, volumeType, translation);
    }

    /// <summary>CSR face variant: <paramref name="faceCornerOffsets"/> length FaceCount+1.</summary>
    public static VdbGrid<float> ConvertGeometry(Vector3[] positions, int[] faceCornerOffsets, int[] cornerPointIndices,
                                                 float voxelSize, int halfWidthVoxels, VdbVolumeType volumeType,
                                                 Vector3? translation = null)
    {
        var corners = new List<int>();
        for (var f = 0; f < faceCornerOffsets.Length - 1; f++)
        {
            var faceStart = faceCornerOffsets[f];
            var faceEnd = faceCornerOffsets[f + 1];
            var faceCorners = faceEnd - faceStart;
            if (faceCorners < 3)
                continue;
            for (var i = 1; i < faceCorners - 1; i++)
            {
                corners.Add(cornerPointIndices[faceStart]);
                corners.Add(cornerPointIndices[faceStart + i]);
                corners.Add(cornerPointIndices[faceStart + i + 1]);
            }
        }

        return ConvertCorners(positions, corners.ToArray(), voxelSize, halfWidthVoxels, volumeType, translation);
    }

    /// <summary>About 600 MB of voxel data, the same bound the primitives use.</summary>
    private const int MaxLeaves = 262_144;

    public static VdbGrid<float> ConvertCorners(Vector3[] positions, int[] triangles,
                                                 float voxelSize, int band, VdbVolumeType volumeType,
                                                 Vector3? translation)
    {
        // A zero or negative voxel size would divide the bounds to infinity; an unbounded band
        // would rasterize a huge shell around the mesh. Both are user inputs.
        voxelSize = Math.Clamp(voxelSize, 1e-5f, 1e4f);
        band = Math.Clamp(band, 1, 64);

        var bvh = new MeshBvh(positions, triangles);

        // Surface bounds in index space.
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in positions)
        {
            // A NaN or infinite point (a broken upstream mesh) would stretch the bounds over the whole index range.
            if (!float.IsFinite(p.X + p.Y + p.Z))
                continue;

            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        if (min.X > max.X)
            throw new ArgumentException("The mesh has no finite points.");

        var offset = translation ?? Vector3.Zero;
        min -= offset;
        max -= offset;

        var transform = new VdbTransform(voxelSize, offset);
        var bandWorld = band * voxelSize;
        var iMin = new VdbCoord((int)MathF.Floor((min.X - bandWorld) / voxelSize) - 1,
                                (int)MathF.Floor((min.Y - bandWorld) / voxelSize) - 1,
                                (int)MathF.Floor((min.Z - bandWorld) / voxelSize) - 1);
        var iMax = new VdbCoord((int)MathF.Ceiling((max.X + bandWorld) / voxelSize) + 1,
                                (int)MathF.Ceiling((max.Y + bandWorld) / voxelSize) + 1,
                                (int)MathF.Ceiling((max.Z + bandWorld) / voxelSize) + 1);

        var isSdf = volumeType == VdbVolumeType.Sdf;
        var background = isSdf ? bandWorld : 0f;
        var grid = new VdbGrid<float>(background, voxelSize, isSdf ? "LS" : "density",
                                      isSdf ? VdbGridClass.LevelSet : VdbGridClass.FogVolume);

        // Phase 1: collect the leaves touched by the band (triangle bboxes expanded by band).
        var leafCoords = new HashSet<VdbCoord>();
        for (var t = 0; t < triangles.Length; t += 3)
        {
            var a = (positions[triangles[t]] - offset) / voxelSize;
            var b = (positions[triangles[t + 1]] - offset) / voxelSize;
            var c = (positions[triangles[t + 2]] - offset) / voxelSize;
            if (!float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z + c.X + c.Y + c.Z))
                continue;

            var triMin = Vector3.Min(a, Vector3.Min(b, c));
            var triMax = Vector3.Max(a, Vector3.Max(b, c));
            var lo = new VdbCoord((int)MathF.Floor(triMin.X) - band - 1, (int)MathF.Floor(triMin.Y) - band - 1, (int)MathF.Floor(triMin.Z) - band - 1);
            var hi = new VdbCoord((int)MathF.Ceiling(triMax.X) + band + 1, (int)MathF.Ceiling(triMax.Y) + band + 1, (int)MathF.Ceiling(triMax.Z) + band + 1);
            lo = VdbCoord.Max(lo, iMin);
            hi = VdbCoord.Min(hi, iMax);

            // A large triangle on a fine grid touches millions of leaves; stop before the set exhausts memory.
            var boxLeaves = ((long)(hi.X - (lo.X & ~7)) / 8 + 1) * ((long)(hi.Y - (lo.Y & ~7)) / 8 + 1) * ((long)(hi.Z - (lo.Z & ~7)) / 8 + 1);
            if (boxLeaves > MaxLeaves || leafCoords.Count > MaxLeaves)
                throw new ArgumentException($"Mesh too large for voxel size {voxelSize} (over {MaxLeaves} leaves); increase VoxelSize.");

            // Scan leaf-aligned origins so every candidate leaf intersects the triangle's band box.
            for (var z = lo.Z & ~7; z <= hi.Z; z += 8)
            {
                for (var y = lo.Y & ~7; y <= hi.Y; y += 8)
                {
                    for (var x = lo.X & ~7; x <= hi.X; x += 8)
                    {
                        leafCoords.Add(new VdbCoord(x, y, z));
                    }
                }
            }
        }

        // Phase 2: per leaf, compute exact distances. Node allocation happens serially first
        // (the tree is not thread-safe), then voxels fill in parallel - each leaf writes only itself.
        var orderedLeaves = new List<VdbCoord>(leafCoords);
        orderedLeaves.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        var leafNodes = new List<VdbLeafNode<float>>(orderedLeaves.Count);
        foreach (var origin in orderedLeaves)
            leafNodes.Add(grid.Tree.AddLeaf(origin));

        Parallel.For(0, orderedLeaves.Count, VolumeParallelism.Options, leafIndex =>
        {
            var leafOrigin = orderedLeaves[leafIndex];
            var leaf = leafNodes[leafIndex];
            var values = new float[VdbLeafNode<float>.Size];
            Array.Fill(values, background);
            var onBits = new bool[VdbLeafNode<float>.Size];
            var anyActive = false;

            for (var offsetIndex = 0; offsetIndex < VdbLeafNode<float>.Size; offsetIndex++)
            {
                var local = VdbCoord.LocalFromOffset(offsetIndex);
                var coord = leafOrigin + local;
                if (coord.X < iMin.X || coord.X > iMax.X || coord.Y < iMin.Y || coord.Y > iMax.Y || coord.Z < iMin.Z || coord.Z > iMax.Z)
                    continue;

                var world = transform.IndexToWorld(coord) - offset;
                var dist2 = bvh.ClosestPoint(world, out var triIndex, out var closest, out var feature);
                var dist = MathF.Sqrt(dist2);
                if (dist > bandWorld)
                    continue;

                if (isSdf)
                {
                    var pseudo = bvh.PseudoNormal(triIndex, closest, feature, positions, triangles);
                    var signed = Vector3.Dot(world - closest, pseudo) < 0 ? -dist : dist;
                    values[offsetIndex] = signed;
                }
                else
                {
                    // Fog: density 1 at the surface falling off over the band.
                    values[offsetIndex] = Math.Clamp(1f - dist / bandWorld, 0f, 1f);
                }

                onBits[offsetIndex] = true;
                anyActive = true;
            }

            if (!anyActive)
                return;

            Array.Copy(values, leaf.Values, values.Length);
            for (var i = 0; i < onBits.Length; i++)
            {
                if (onBits[i])
                    leaf.ValueMask.SetOn(i);
            }
        });

        if (isSdf)
        {
            FillInterior(grid, iMin, iMax, background);
        }

        grid.Tree.Prune(0f);
        return grid;
    }

    /// <summary>
    /// Floods the mesh interior with inactive -background values so deep-inside probes read
    /// signed (OpenVDB level-set convention). Columns are classified by band-voxel sign parity,
    /// which is exact for watertight surfaces.
    /// </summary>
    private static void FillInterior(VdbGrid<float> grid, VdbCoord iMin, VdbCoord iMax, float background)
    {
        // Gather band voxels per (y, z) column.
        var columns = new Dictionary<(int Y, int Z), List<(int X, float D)>>();
        foreach (var leaf in grid.Tree.Leaves)
        {
            foreach (var i in leaf.ValueMask.OnBits())
            {
                var local = VdbCoord.LocalFromOffset(i);
                var coord = leaf.Origin + local;
                var key = (coord.Y, coord.Z);
                if (!columns.TryGetValue(key, out var list))
                    columns[key] = list = new List<(int X, float D)>();
                list.Add((coord.X, leaf.Values[i]));
            }
        }

        foreach (var (column, list) in columns)
        {
            list.Sort((a, b) => a.X.CompareTo(b.X));

            // Walk the column: while the last band voxel seen was negative (inside), everything
            // up to the next positive band voxel is interior.
            var filling = false;
            var fillStart = 0;
            foreach (var (x, d) in list)
            {
                if (!filling && d < 0)
                {
                    filling = true;
                    fillStart = x + 1;
                }
                else if (filling && d >= 0)
                {
                    filling = false;
                    FillColumnSpan(grid, fillStart, x - 1, column.Y, column.Z, iMin, iMax, -background);
                }
            }
        }
    }

    private static void FillColumnSpan(VdbGrid<float> grid, int x0, int x1, int y, int z, VdbCoord iMin, VdbCoord iMax, float value)
    {
        if (x1 < x0)
            return;

        for (var x = Math.Max(x0, iMin.X); x <= Math.Min(x1, iMax.X); x++)
        {
            var coord = new VdbCoord(x, y, z);
            if (grid.Tree.ProbeLeaf(coord) is { } leaf)
            {
                var offset = coord.LeafOffset();
                if (!leaf.ValueMask.IsOn(offset))
                    leaf.Values[offset] = value;
                continue;
            }

            // Interior voxels further than the band from the surface have no leaf yet. Without
            // one they fall back to the grid background (+band), i.e. they read as "outside":
            // offsets and CSG then invert the solid. Create the leaf and store the interior value.
            grid.Tree.SetValueOnly(coord, value);
        }
    }
}

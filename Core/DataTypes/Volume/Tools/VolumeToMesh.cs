#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume.Tools;

namespace T3.Core.DataTypes.Volume;

/// <summary>
/// Classic marching-cubes surface extraction over a float grid (OpenVDB's volumeToMesh with
/// adaptivity 0). Cells are marched through the active band; vertices on shared edges are
/// deduplicated across cells and leaves so the surface is watertight, and normals come from central-difference
/// gradients. Output is a triangulated <see cref="MeshGeometry"/>.
/// </summary>
public static class VolumeToMesh
{
    /// <summary>
    /// Upper bound for an extracted surface. Eight million vertices already need roughly 300 MB
    /// (positions, normals and corner indices); beyond that a runaway volume is reported instead
    /// of exhausting memory.
    /// </summary>
    public const int MaxVertices = 8_000_000;

    public static MeshGeometry Extract(VdbGrid<float> grid, float isovalue = 0f)
    {
        var tree = grid.Tree;
        var leaves = new List<VdbLeafNode<float>>(tree.Leaves);

        // Leaves are marched in parallel, each against a copy of its neighbourhood, so a cell reads its corners
        // from an array instead of walking the tree eight times. Vertices on edges between leaves are computed by
        // both leaves from the same values and merged below, so the mesh stays watertight.
        var parts = new LeafPart[leaves.Count];
        if (leaves.Count > 0)
        {
            System.Threading.Tasks.Parallel.For(0, leaves.Count, VolumeParallelism.Options,
                                                () => new LeafScratch(),
                                                (index, _, scratch) =>
                                                {
                                                    parts[index] = MarchLeaf(grid, leaves[index], isovalue, scratch);
                                                    return scratch;
                                                },
                                                _ => { });
        }

        // Merge in leaf order: the output is identical from run to run whatever the thread count.
        var vertexCount = 0;
        var cornerCount = 0;
        foreach (var part in parts)
        {
            if (part == null)
                continue;
            vertexCount += part.Keys.Count;
            cornerCount += part.Corners.Count;
        }

        var positions = new List<Vector3>(vertexCount);
        var normals = new List<Vector3>(vertexCount);
        var cornerIndices = new int[cornerCount];
        var edgeToVertex = new Dictionary<(VdbCoord Corner, int Axis), int>(vertexCount);
        var cornerWrite = 0;
        var localToGlobal = new List<int>();
        foreach (var part in parts)
        {
            if (part == null)
                continue;

            localToGlobal.Clear();
            for (var local = 0; local < part.Keys.Count; local++)
            {
                if (!edgeToVertex.TryGetValue(part.Keys[local], out var global))
                {
                    if (positions.Count >= MaxVertices)
                        throw new InvalidOperationException($"Extracted surface exceeds {MaxVertices} vertices; raise the voxel size or Isovalue.");

                    global = positions.Count;
                    positions.Add(part.Positions[local]);
                    normals.Add(part.Normals[local]);
                    edgeToVertex.Add(part.Keys[local], global);
                }

                localToGlobal.Add(global);
            }

            foreach (var corner in part.Corners)
                cornerIndices[cornerWrite++] = localToGlobal[corner];
        }

        var geometry = new MeshGeometry
        {
            Positions = positions.ToArray(),
            FaceCornerOffsets = BuildFaceOffsets(cornerIndices.Length / 3),
            CornerPointIndices = cornerIndices,
        };
        var normalAttribute = geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, geometry.CornerCount);
        for (var corner = 0; corner < cornerIndices.Length; corner++)
            normalAttribute.Values[corner] = normals[cornerIndices[corner]];
        return geometry;
    }

    /// <summary>The triangles one leaf contributes, with its vertices keyed by the edge they sit on.</summary>
    private sealed class LeafPart
    {
        public readonly List<(VdbCoord Corner, int Axis)> Keys = new();
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<int> Corners = new();
    }

    /// <summary>Per-thread state: the leaf's neighbourhood and the per-cell scratch.</summary>
    private sealed class LeafScratch
    {
        /// <summary>One voxel for the far cell corners, one more for their central-difference gradients.</summary>
        public readonly VdbNeighbourhood Block = new(2);
        public readonly Dictionary<(VdbCoord Corner, int Axis), int> LocalEdges = new();
        public readonly float[] CornerValues = new float[8];
        public readonly int[] EdgeVertices = new int[12];
    }

    private static LeafPart? MarchLeaf(VdbGrid<float> grid, VdbLeafNode<float> leaf, float isovalue, LeafScratch scratch)
    {
        var block = scratch.Block;
        block.Fill(grid.Tree, leaf.Origin);
        var values = block.Values;
        var activeFlags = block.Active;

        // Most leaves of a solid level set lie entirely inside or outside the surface: no cell can cross it.
        var anyInside = false;
        var anyOutside = false;
        for (var i = 0; i < values.Length && !(anyInside && anyOutside); i++)
        {
            if (values[i] < isovalue)
                anyInside = true;
            else
                anyOutside = true;
        }

        if (!anyInside || !anyOutside)
            return null;

        var cornerValues = scratch.CornerValues;
        var edgeVertices = scratch.EdgeVertices;
        var localEdges = scratch.LocalEdges;
        localEdges.Clear();
        LeafPart? part = null;

        // Each cell is owned by exactly one leaf (cells span corner..corner+1, so the cell at
        // a leaf's max border belongs to the neighbor). Every crossing cell is covered once.
        for (var z = 0; z < VdbLeafNode<float>.Dim; z++)
        {
            for (var y = 0; y < VdbLeafNode<float>.Dim; y++)
            {
                for (var x = 0; x < VdbLeafNode<float>.Dim; x++)
                {
                    var caseIndex = 0;
                    for (var c = 0; c < 8; c++)
                    {
                        var (cx, cy, cz) = CornerOffset(c);
                        var value = values[block.Index(x + cx, y + cy, z + cz)];
                        cornerValues[c] = value;
                        if (value < isovalue)
                            caseIndex |= 1 << c;
                    }

                    if (caseIndex == 0 || caseIndex == 255)
                        continue;

                    // Narrow-band level sets store +background outside and -background
                    // inside, so only the shell around the surface is active. A sign change
                    // between two *inactive* corners is the band edge, not the surface —
                    // emitting those cells creates geometry offset from the real surface.
                    // A crossing with at least one active endpoint is the actual surface.
                    var resolved = true;
                    for (var e = 0; e < 12; e++)
                    {
                        var (ca, cb) = EdgeCorners(e);
                        if (cornerValues[ca] < isovalue == cornerValues[cb] < isovalue)
                            continue;

                        var (ax, ay, az) = CornerOffset(ca);
                        var (bx, by, bz) = CornerOffset(cb);
                        if (activeFlags[block.Index(x + ax, y + ay, z + az)] || activeFlags[block.Index(x + bx, y + by, z + bz)])
                            continue;

                        resolved = false;
                        break;
                    }

                    if (!resolved)
                        continue;

                    part ??= new LeafPart();
                    Array.Clear(edgeVertices);

                    var tris = MarchingCubesTables.TriTable[caseIndex];
                    for (var t = 0; t < tris.Length; t += 3)
                    {
                        if (tris[t] < 0)
                            break;

                        // The table winds triangles clockwise seen from the inside; meshes in TiXL face
                        // outward counter-clockwise, so the last two corners are swapped.
                        for (var v = 0; v < 3; v++)
                        {
                            var edge = tris[t + (v == 0 ? 0 : 3 - v)];
                            var cornerIndex = edgeVertices[edge];
                            if (cornerIndex == 0)
                            {
                                var (cornerA, cornerB) = EdgeCorners(edge);
                                var (ax, ay, az) = CornerOffset(cornerA);
                                var (bx, by, bz) = CornerOffset(cornerB);
                                var localA = new VdbCoord(x + ax, y + ay, z + az);
                                var localB = new VdbCoord(x + bx, y + by, z + bz);
                                var axis = ax != bx ? 0 : ay != by ? 1 : 2;
                                var edgeKey = (leaf.Origin + VdbCoord.Min(localA, localB), axis);
                                if (!localEdges.TryGetValue(edgeKey, out cornerIndex))
                                {
                                    cornerIndex = AddVertex(grid, leaf.Origin, block, localA, localB,
                                                            cornerValues[cornerA], cornerValues[cornerB], isovalue, part, edgeKey);
                                    localEdges[edgeKey] = cornerIndex;
                                }

                                edgeVertices[edge] = cornerIndex + 1;
                            }
                            else
                            {
                                cornerIndex -= 1;
                            }

                            part.Corners.Add(cornerIndex);
                        }
                    }
                }
            }
        }

        return part;
    }

    private static int AddVertex(VdbGrid<float> grid, VdbCoord origin, VdbNeighbourhood block, VdbCoord localA, VdbCoord localB,
                                 float va, float vb, float isovalue, LeafPart part, (VdbCoord, int) edgeKey)
    {
        var t = Math.Abs(vb - va) < 1e-20f ? 0.5f : (isovalue - va) / (vb - va);
        var indexPos = Vector3.Lerp((origin + localA).ToVector3(), (origin + localB).ToVector3(), t);
        // Values live at voxel centers, so the interpolated position is a center-based index position.
        part.Positions.Add(grid.Transform.IndexToWorld(indexPos));

        // Central-difference gradient in index space; the uniform-scale transform preserves direction.
        var normal = Gradient(block, localA) + Gradient(block, localB);
        var length = normal.Length();
        part.Normals.Add(length > 1e-9f ? normal / length : Vector3.UnitY);
        part.Keys.Add(edgeKey);
        return part.Keys.Count - 1;
    }

    private static Vector3 Gradient(VdbNeighbourhood block, VdbCoord c)
    {
        var values = block.Values;
        return new Vector3(values[block.Index(c.X + 1, c.Y, c.Z)] - values[block.Index(c.X - 1, c.Y, c.Z)],
                           values[block.Index(c.X, c.Y + 1, c.Z)] - values[block.Index(c.X, c.Y - 1, c.Z)],
                           values[block.Index(c.X, c.Y, c.Z + 1)] - values[block.Index(c.X, c.Y, c.Z - 1)]);
    }

    private static int[] BuildFaceOffsets(int triangleCount)
    {
        var offsets = new int[triangleCount + 1];
        for (var i = 0; i <= triangleCount; i++)
            offsets[i] = i * 3;
        return offsets;
    }

    private static (int X, int Y, int Z) CornerOffset(int corner) => corner switch
    {
        1 => (1, 0, 0),
        2 => (1, 1, 0),
        3 => (0, 1, 0),
        4 => (0, 0, 1),
        5 => (1, 0, 1),
        6 => (1, 1, 1),
        7 => (0, 1, 1),
        _ => (0, 0, 0),
    };

    private static (int A, int B) EdgeCorners(int edge) => edge switch
    {
        0 => (0, 1),
        1 => (1, 2),
        2 => (2, 3),
        3 => (3, 0),
        4 => (4, 5),
        5 => (5, 6),
        6 => (6, 7),
        7 => (7, 4),
        8 => (0, 4),
        9 => (1, 5),
        10 => (2, 6),
        _ => (3, 7),
    };
}

#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.DataTypes;

/// <summary>
/// CPU-side procedural mesh: N-gon faces over shared points, with typed attributes
/// per domain and an optional part table. This is the flexible authoring format -
/// triangulation and packing into <see cref="MeshBuffers"/> happen late, in the
/// compile step ([GeometryToMeshBuffers]).
///
/// Topology is CSR-style: face f uses corners FaceCornerOffsets[f] .. FaceCornerOffsets[f+1],
/// and each corner indexes a point via CornerPointIndices. Arrays are authoritative
/// (Length == element count). Instances flow through the graph by reference like
/// MeshBuffers: ops must not mutate their inputs - they build into their own reused
/// output instance and reallocate only when sizes change.
/// </summary>
public sealed class MeshGeometry
{
    /// <summary>Point positions; the Point domain's element count.</summary>
    public Vector3[] Positions = [];

    /// <summary>Per face: start offset into CornerPointIndices. Length FaceCount + 1 (last entry = corner count).</summary>
    public int[] FaceCornerOffsets = [0];

    /// <summary>Per corner: the point it references. The Corner domain's element count.</summary>
    public int[] CornerPointIndices = [];

    /// <summary>
    /// Contiguous face ranges forming semantic pieces (glyphs, fracture cells, ...).
    /// Empty means one implicit part covering all faces. Parts map 1:1 to chunks at compile time.
    /// </summary>
    public GeometryPart[] Parts = [];

    public GeometryAttributes Attributes { get; } = new();

    public int PointCount => Positions.Length;
    public int FaceCount => FaceCornerOffsets.Length - 1;
    public int CornerCount => CornerPointIndices.Length;

    public int GetFaceCornerCount(int faceIndex) => FaceCornerOffsets[faceIndex + 1] - FaceCornerOffsets[faceIndex];

    /// <summary>
    /// Unique-edge topology, derived from the faces on first access and cached.
    /// Call <see cref="InvalidateTopologyCaches"/> after changing faces or corners.
    /// </summary>
    public EdgeTopology Edges => _edgeTopology ??= EdgeTopology.Build(this);

    public void InvalidateTopologyCaches() => _edgeTopology = null;

    /// <summary>Triangle count after fan-triangulating all N-gons (what the compile step will emit).</summary>
    public int GetTriangleCount()
    {
        var count = 0;
        for (var faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            var corners = GetFaceCornerCount(faceIndex);
            if (corners >= 3)
                count += corners - 2;
        }

        return count;
    }

    private EdgeTopology? _edgeTopology;
}

/// <summary>A semantic piece of a <see cref="MeshGeometry"/>: a contiguous face range with placement metadata.</summary>
public readonly record struct GeometryPart(int FaceStart, int FaceCount, Vector3 Pivot, int Id, int Seed);

/// <summary>
/// Unique edges of a mesh with face adjacency, derived lazily from the N-gon topology.
/// Assumes at most two faces per edge (non-manifold extra faces keep the first two).
/// </summary>
public sealed class EdgeTopology
{
    /// <summary>Point indices per edge, PointA &lt; PointB. Face1 is -1 for boundary edges.</summary>
    public readonly record struct Edge(int PointA, int PointB, int Face0, int Face1);

    public Edge[] Edges = [];

    /// <summary>Per corner: the edge from this corner's point to the next corner's point within the face.</summary>
    public int[] CornerEdgeIndices = [];

    public static EdgeTopology Build(MeshGeometry geometry)
    {
        var cornerCount = geometry.CornerCount;
        var edgeLookup = new Dictionary<(int, int), int>(cornerCount);
        var edges = new List<Edge>(cornerCount / 2 + 4);
        var cornerEdges = new int[cornerCount];

        var offsets = geometry.FaceCornerOffsets;
        var cornerPoints = geometry.CornerPointIndices;

        for (var faceIndex = 0; faceIndex < geometry.FaceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            for (var corner = start; corner < end; corner++)
            {
                var nextCorner = corner + 1 == end ? start : corner + 1;
                var p0 = cornerPoints[corner];
                var p1 = cornerPoints[nextCorner];
                var key = p0 < p1 ? (p0, p1) : (p1, p0);

                if (edgeLookup.TryGetValue(key, out var edgeIndex))
                {
                    var edge = edges[edgeIndex];
                    if (edge.Face1 == -1 && edge.Face0 != faceIndex)
                        edges[edgeIndex] = edge with { Face1 = faceIndex };
                }
                else
                {
                    edgeIndex = edges.Count;
                    edgeLookup.Add(key, edgeIndex);
                    edges.Add(new Edge(key.Item1, key.Item2, faceIndex, -1));
                }

                cornerEdges[corner] = edgeIndex;
            }
        }

        return new EdgeTopology
                   {
                       Edges = edges.ToArray(),
                       CornerEdgeIndices = cornerEdges,
                   };
    }
}

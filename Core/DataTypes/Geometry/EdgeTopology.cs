#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.DataTypes;

/// <summary>
/// Unique edges of the polygons of a <see cref="Geometry"/> with primitive adjacency, derived lazily from the N-gon
/// topology. Assumes at most two polygons per edge (non-manifold extra polygons keep the first two). Curve vertices
/// get edge -1.
/// </summary>
public sealed class EdgeTopology
{
    /// <summary>Point indices per edge, PointA &lt; PointB. Face1 is -1 for boundary edges.</summary>
    public readonly record struct Edge(int PointA, int PointB, int Face0, int Face1);

    public Edge[] Edges = [];

    /// <summary>Per corner: the edge from this corner's point to the next corner's point within the face.</summary>
    public int[] CornerEdgeIndices = [];

    public static EdgeTopology Build(Geometry geometry)
    {
        return Build(geometry.PrimitiveVertexOffsets, geometry.VertexPoints, geometry.PrimitiveCount, geometry.PrimitiveKind);
    }

    /// <param name="kinds">Null when every primitive is a polygon; otherwise curves are skipped.</param>
    private static EdgeTopology Build(int[] offsets, int[] cornerPoints, int faceCount, PrimitiveKinds[]? kinds)
    {
        var cornerCount = cornerPoints.Length;
        var edgeLookup = new Dictionary<(int, int), int>(cornerCount);
        var edges = new List<Edge>(cornerCount / 2 + 4);
        var cornerEdges = new int[cornerCount];
        if (kinds != null)
            Array.Fill(cornerEdges, -1);

        for (var faceIndex = 0; faceIndex < faceCount; faceIndex++)
        {
            if (kinds != null && kinds[faceIndex] != PrimitiveKinds.Polygon)
                continue;

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

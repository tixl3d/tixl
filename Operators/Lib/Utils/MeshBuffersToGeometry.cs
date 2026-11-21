using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Rendering;

namespace Lib.Utils;

/// <summary>
/// Turns the vertex, triangle and chunk arrays of a <see cref="MeshBuffers"/> back into a <see cref="MeshGeometry"/>: one
/// triangle face per index triple, one part per chunk. Kept free of GPU types so it can be tested without a device;
/// [MeshToGeometry] does the readback.
/// </summary>
internal static class MeshBuffersToGeometry
{
    /// <summary>
    /// Fills <paramref name="output"/> and returns false when an index points outside the vertices. With
    /// <paramref name="weld"/>, vertices at the same position become one point, so faces that were split for their normals
    /// or UVs are connected again; normals and texture coordinates stay per corner either way.
    /// </summary>
    public static bool Build(ReadOnlySpan<PbrVertex> vertices, ReadOnlySpan<Int3> triangles, ReadOnlySpan<MeshChunkDef> chunks, bool weld,
                             MeshGeometry output, Dictionary<Vector3, int> weldLookup)
    {
        for (var triangle = 0; triangle < triangles.Length; triangle++)
        {
            var t = triangles[triangle];
            if ((uint)t.X >= (uint)vertices.Length || (uint)t.Y >= (uint)vertices.Length || (uint)t.Z >= (uint)vertices.Length)
                return false;
        }

        var cornerCount = triangles.Length * 3;
        var vertexToPoint = new int[vertices.Length];
        var pointCount = 0;
        weldLookup.Clear();
        for (var vertex = 0; vertex < vertices.Length; vertex++)
        {
            var position = vertices[vertex].Position;
            if (weld && weldLookup.TryGetValue(position, out var existing))
            {
                vertexToPoint[vertex] = existing;
                continue;
            }

            if (weld)
                weldLookup.Add(position, pointCount);

            vertexToPoint[vertex] = pointCount++;
        }

        if (output.Positions.Length != pointCount)
            output.Positions = new Vector3[pointCount];
        if (output.CornerPointIndices.Length != cornerCount)
            output.CornerPointIndices = new int[cornerCount];
        if (output.FaceCornerOffsets.Length != triangles.Length + 1)
            output.FaceCornerOffsets = new int[triangles.Length + 1];

        for (var vertex = 0; vertex < vertices.Length; vertex++)
            output.Positions[vertexToPoint[vertex]] = vertices[vertex].Position;

        output.Attributes.Clear();
        var normals = output.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, cornerCount).Values;
        var texCoords = output.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, cornerCount).Values;
        for (var triangle = 0; triangle < triangles.Length; triangle++)
        {
            var t = triangles[triangle];
            output.FaceCornerOffsets[triangle] = triangle * 3;
            for (var side = 0; side < 3; side++)
            {
                var corner = triangle * 3 + side;
                var vertex = side == 0 ? t.X : side == 1 ? t.Y : t.Z;
                output.CornerPointIndices[corner] = vertexToPoint[vertex];
                normals[corner] = vertices[vertex].Normal;
                texCoords[corner] = vertices[vertex].Texcoord;
            }
        }

        output.FaceCornerOffsets[triangles.Length] = cornerCount;
        output.Parts = BuildParts(vertices, chunks, triangles.Length);
        output.InvalidateTopologyCaches();
        return true;
    }

    /// <summary>One part per chunk, pivoted at the centre of its vertices. A single chunk, or none, is one implicit part.</summary>
    private static GeometryPart[] BuildParts(ReadOnlySpan<PbrVertex> vertices, ReadOnlySpan<MeshChunkDef> chunks, int faceCount)
    {
        if (chunks.Length < 2)
            return [];

        var parts = new GeometryPart[chunks.Length];
        for (var index = 0; index < chunks.Length; index++)
        {
            var chunk = chunks[index];
            var faceStart = Math.Clamp(chunk.StartFaceIndex, 0, faceCount);
            var faceEnd = Math.Clamp(chunk.StartFaceIndex + Math.Max(0, chunk.FaceCount), faceStart, faceCount);
            var vertexStart = Math.Clamp(chunk.StartVertexIndex, 0, vertices.Length);
            var vertexEnd = Math.Clamp(chunk.StartVertexIndex + Math.Max(0, chunk.VertexCount), vertexStart, vertices.Length);

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var vertex = vertexStart; vertex < vertexEnd; vertex++)
            {
                min = Vector3.Min(min, vertices[vertex].Position);
                max = Vector3.Max(max, vertices[vertex].Position);
            }

            var pivot = vertexEnd > vertexStart ? (min + max) * 0.5f : Vector3.Zero;
            parts[index] = new GeometryPart(faceStart, faceEnd - faceStart, pivot, index, index);
        }

        return parts;
    }
}

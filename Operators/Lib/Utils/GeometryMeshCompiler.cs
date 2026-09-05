#nullable enable
using System;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Rendering;
using T3.Core.Utils.Geometry;

namespace Lib.Utils;

/// <summary>
/// Compiles a MeshGeometry into packed PbrVertex/triangle arrays: N-gons are fan
/// triangulated, corner attributes resolved, tangent bases computed. One vertex per
/// corner, so a part's contiguous face range maps to contiguous vertex and triangle
/// ranges - that is what makes the per-part chunk table possible. Buffers are kept
/// between calls; use one compiler per op.
/// </summary>
internal sealed class GeometryMeshCompiler
{
    public PbrVertex[] Vertices { get; private set; } = [];
    public Int3[] Triangles { get; private set; } = [];

    /// <summary>One chunk per part in part order; a single whole-mesh chunk for part-less geometry.</summary>
    public MeshChunkDef[] Chunks { get; private set; } = [];

    /// <summary>
    /// Compiles <paramref name="geometry"/>. With <paramref name="relativeToPartPivots"/> the vertex
    /// positions of every part are expressed relative to its pivot, so a chunk draw can place
    /// the part with a point transform.
    /// </summary>
    public void Compile(MeshGeometry geometry, bool relativeToPartPivots)
    {
        var cornerCount = geometry.CornerCount;
        var triangleCount = geometry.GetTriangleCount();
        if (Vertices.Length != cornerCount)
            Vertices = new PbrVertex[cornerCount];
        if (Triangles.Length != triangleCount)
            Triangles = new Int3[triangleCount];

        // Attribute lookups once, outside the loops
        geometry.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var cornerNormals);
        geometry.Attributes.TryGet<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, out var cornerUvs);
        geometry.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Corner, out var cornerColors);

        // Coarser color domains are promoted to corners here: face color, else part color
        GeometryAttribute<Vector4>? faceColors = null;
        GeometryAttribute<Vector4>? partColors = null;
        if (cornerColors == null
            && !geometry.Attributes.TryGet(GeometryAttributeNames.Color, AttributeDomain.Face, out faceColors))
        {
            geometry.Attributes.TryGet(GeometryAttributeNames.Color, AttributeDomain.Part, out partColors);
        }

        if (partColors != null)
            BuildFaceToPartMap(geometry);

        var positions = geometry.Positions;
        var offsets = geometry.FaceCornerOffsets;
        var cornerPoints = geometry.CornerPointIndices;
        var vertices = Vertices;
        var triangles = Triangles;

        var triangleIndex = 0;
        for (var faceIndex = 0; faceIndex < geometry.FaceCount; faceIndex++)
        {
            var start = offsets[faceIndex];
            var end = offsets[faceIndex + 1];
            var faceCornerCount = end - start;
            if (faceCornerCount < 3)
                continue;

            // Face normal as fallback for meshes without a corner normal attribute (Newell's method handles N-gons)
            var faceNormal = Vector3.Zero;
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var p0 = positions[cornerPoints[c]];
                var p1 = positions[cornerPoints[next]];
                faceNormal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                          (p0.Z - p1.Z) * (p0.X + p1.X),
                                          (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            faceNormal = faceNormal.LengthSquared() > 1e-10f ? Vector3.Normalize(faceNormal) : Vector3.UnitY;

            var faceColor = Vector4.One;
            if (faceColors != null)
                faceColor = faceColors.Values[faceIndex];
            else if (partColors != null)
                faceColor = _faceToPart[faceIndex] >= 0 ? partColors.Values[_faceToPart[faceIndex]] : Vector4.One;

            for (var c = start; c < end; c++)
            {
                var normal = cornerNormals != null ? cornerNormals.Values[c] : faceNormal;
                var uv = cornerUvs != null ? cornerUvs.Values[c] : Vector2.Zero;
                var color = cornerColors != null ? cornerColors.Values[c] : faceColor;

                vertices[c] = new PbrVertex
                                  {
                                      Position = positions[cornerPoints[c]],
                                      Normal = normal,
                                      Texcoord = uv,
                                      Texcoord2 = uv,
                                      Selection = 1,
                                      ColorRgb = new Vector3(color.X, color.Y, color.Z),
                                  };
            }

            // Tangent basis from the face's first triangle - exact for planar faces
            {
                var c0 = start;
                var c1 = start + 1;
                var c2 = start + 2;
                MeshUtils.CalcTBNSpace(positions[cornerPoints[c0]], cornerUvs?.Values[c0] ?? Vector2.Zero,
                                       positions[cornerPoints[c1]], cornerUvs?.Values[c1] ?? Vector2.UnitX,
                                       positions[cornerPoints[c2]], cornerUvs?.Values[c2] ?? Vector2.One,
                                       faceNormal, out var tangent, out var bitangent);
                if (tangent.LengthSquared() < 1e-10f || float.IsNaN(tangent.X))
                {
                    tangent = Vector3.Normalize(Vector3.Cross(faceNormal, Math.Abs(faceNormal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
                    bitangent = Vector3.Cross(faceNormal, tangent);
                }

                for (var c = start; c < end; c++)
                {
                    vertices[c].Tangent = tangent;
                    vertices[c].Bitangent = bitangent;
                }
            }

            // Fan triangulation (assumes convex faces - sufficient until a real triangulator lands)
            for (var i = 1; i < faceCornerCount - 1; i++)
            {
                triangles[triangleIndex++] = new Int3(start, start + i, start + i + 1);
            }
        }

        BuildChunks(geometry, relativeToPartPivots);
    }

    private void BuildChunks(MeshGeometry geometry, bool relativeToPartPivots)
    {
        var parts = geometry.Parts;
        var offsets = geometry.FaceCornerOffsets;
        var vertices = Vertices;

        if (parts.Length == 0)
        {
            if (Chunks.Length != 1)
                Chunks = new MeshChunkDef[1];

            Chunks[0] = new MeshChunkDef
                            {
                                StartFaceIndex = 0,
                                FaceCount = Triangles.Length,
                                StartVertexIndex = 0,
                                VertexCount = vertices.Length,
                            };
            return;
        }

        if (Chunks.Length != parts.Length)
            Chunks = new MeshChunkDef[parts.Length];

        var triangleStart = 0;
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = parts[partIndex];
            var faceEnd = Math.Min(part.FaceStart + part.FaceCount, geometry.FaceCount);
            var cornerStart = offsets[part.FaceStart];
            var cornerEnd = offsets[faceEnd];

            var triangleCount = 0;
            for (var faceIndex = part.FaceStart; faceIndex < faceEnd; faceIndex++)
            {
                var corners = offsets[faceIndex + 1] - offsets[faceIndex];
                if (corners >= 3)
                    triangleCount += corners - 2;
            }

            if (relativeToPartPivots)
            {
                var pivot = part.Pivot;
                for (var c = cornerStart; c < cornerEnd; c++)
                {
                    vertices[c].Position -= pivot;
                }
            }

            Chunks[partIndex] = new MeshChunkDef
                                    {
                                        StartFaceIndex = triangleStart,
                                        FaceCount = triangleCount,
                                        StartVertexIndex = cornerStart,
                                        VertexCount = cornerEnd - cornerStart,
                                    };
            triangleStart += triangleCount;
        }
    }

    private void BuildFaceToPartMap(MeshGeometry geometry)
    {
        if (_faceToPart.Length != geometry.FaceCount)
            _faceToPart = new int[geometry.FaceCount];
        Array.Fill(_faceToPart, -1);

        var parts = geometry.Parts;
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = parts[partIndex];
            var end = Math.Min(part.FaceStart + part.FaceCount, geometry.FaceCount);
            for (var faceIndex = part.FaceStart; faceIndex < end; faceIndex++)
            {
                _faceToPart[faceIndex] = partIndex;
            }
        }
    }

    private int[] _faceToPart = [];
}

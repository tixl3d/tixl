#nullable enable
using System;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Rendering;
using T3.Core.Utils.Math3d;

namespace Lib.Utils;

/// <summary>
/// Compiles the polygons of a <see cref="Geometry"/> or a <see cref="MeshGeometry"/> into packed PbrVertex/triangle
/// arrays: N-gons are fan triangulated, per-vertex attributes resolved, tangent bases computed. One output vertex per
/// polygon vertex, written in primitive order, so a part's contiguous primitive range maps to contiguous vertex and
/// triangle ranges - that is what makes the per-part chunk table possible. Curves and polygons with fewer than three
/// vertices produce nothing. Buffers are kept between calls; use one compiler per op.
/// </summary>
internal sealed class GeometryMeshCompiler
{
    public PbrVertex[] Vertices { get; private set; } = [];
    public Int3[] Triangles { get; private set; } = [];

    /// <summary>One chunk per part in part order; a single whole-mesh chunk for part-less geometry.</summary>
    public MeshChunkDef[] Chunks { get; private set; } = [];

    /// <summary>
    /// Compiles the polygon primitives of <paramref name="geometry"/>. With <paramref name="relativeToPartPivots"/>
    /// the vertex positions of every part are expressed relative to its pivot, so a chunk draw can place the part
    /// with a point transform.
    /// </summary>
    public void Compile(Geometry geometry, bool relativeToPartPivots)
    {
        var parts = geometry.Parts;
        SetPartCount(parts.Length);
        for (var i = 0; i < parts.Length; i++)
            SetPart(i, parts[i].PrimitiveStart, parts[i].PrimitiveCount, parts[i].Pivot);

        Compile(new Source(geometry.Positions, geometry.PrimitiveVertexOffsets, geometry.VertexPoints, geometry.PrimitiveKind,
                           geometry.Attributes, AttributeDomain.Vertex, AttributeDomain.Primitive),
                relativeToPartPivots);
    }

    /// <inheritdoc cref="Compile(Geometry, bool)"/>
    public void Compile(MeshGeometry geometry, bool relativeToPartPivots)
    {
        var parts = geometry.Parts;
        SetPartCount(parts.Length);
        for (var i = 0; i < parts.Length; i++)
            SetPart(i, parts[i].FaceStart, parts[i].FaceCount, parts[i].Pivot);

        Compile(new Source(geometry.Positions, geometry.FaceCornerOffsets, geometry.CornerPointIndices, null,
                           geometry.Attributes, AttributeDomain.Corner, AttributeDomain.Face),
                relativeToPartPivots);
    }

    /// <param name="Kinds">Null when every primitive is a polygon.</param>
    private readonly record struct Source(Vector3[] Positions, int[] Offsets, int[] VertexPoints, PrimitiveKinds[]? Kinds,
                                          GeometryAttributes Attributes, AttributeDomain VertexDomain, AttributeDomain PrimitiveDomain)
    {
        public int PrimitiveCount => Offsets.Length - 1;

        /** Polygons with at least three vertices; everything else is skipped. */
        public bool IsDrawn(int primitive)
        {
            return (Kinds == null || Kinds[primitive] == PrimitiveKinds.Polygon) && Offsets[primitive + 1] - Offsets[primitive] >= 3;
        }
    }

    private void Compile(in Source source, bool relativeToPartPivots)
    {
        var primitiveCount = source.PrimitiveCount;
        if (_primitiveVertexStart.Length != primitiveCount + 1)
        {
            _primitiveVertexStart = new int[primitiveCount + 1];
            _primitiveTriangleStart = new int[primitiveCount + 1];
        }

        // Output layout first: compacted ranges per primitive
        var vertexCount = 0;
        var triangleCount = 0;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            _primitiveVertexStart[primitive] = vertexCount;
            _primitiveTriangleStart[primitive] = triangleCount;
            if (!source.IsDrawn(primitive))
                continue;

            var corners = source.Offsets[primitive + 1] - source.Offsets[primitive];
            vertexCount += corners;
            triangleCount += corners - 2;
        }

        _primitiveVertexStart[primitiveCount] = vertexCount;
        _primitiveTriangleStart[primitiveCount] = triangleCount;

        if (Vertices.Length != vertexCount)
            Vertices = new PbrVertex[vertexCount];
        if (Triangles.Length != triangleCount)
            Triangles = new Int3[triangleCount];

        // Attribute lookups once, outside the loops
        var attributes = source.Attributes;
        attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, source.VertexDomain, out var vertexNormals);
        attributes.TryGet<Vector2>(GeometryAttributeNames.TexCoord, source.VertexDomain, out var vertexUvs);
        attributes.TryGet<Vector2>(GeometryAttributeNames.TexCoord2, source.VertexDomain, out var vertexUvs2);
        attributes.TryGet<Vector4>(GeometryAttributeNames.Color, source.VertexDomain, out var vertexColors);

        // Coarser color domains are promoted to vertices here: primitive color, else part color
        GeometryAttribute<Vector4>? primitiveColors = null;
        GeometryAttribute<Vector4>? partColors = null;
        if (vertexColors == null && !attributes.TryGet(GeometryAttributeNames.Color, source.PrimitiveDomain, out primitiveColors))
            attributes.TryGet(GeometryAttributeNames.Color, AttributeDomain.Part, out partColors);

        if (partColors != null)
            BuildPrimitiveToPartMap(primitiveCount);

        var positions = source.Positions;
        var vertexPoints = source.VertexPoints;
        var vertices = Vertices;
        var triangles = Triangles;

        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            if (!source.IsDrawn(primitive))
                continue;

            var start = source.Offsets[primitive];
            var end = source.Offsets[primitive + 1];
            var cornerCount = end - start;
            var outStart = _primitiveVertexStart[primitive];

            // Face normal as fallback for meshes without a normal attribute (Newell's method handles N-gons)
            var faceNormal = Vector3.Zero;
            for (var c = start; c < end; c++)
            {
                var next = c + 1 == end ? start : c + 1;
                var p0 = positions[vertexPoints[c]];
                var p1 = positions[vertexPoints[next]];
                faceNormal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z),
                                          (p0.Z - p1.Z) * (p0.X + p1.X),
                                          (p0.X - p1.X) * (p0.Y + p1.Y));
            }

            faceNormal = faceNormal.LengthSquared() > 1e-10f ? Vector3.Normalize(faceNormal) : Vector3.UnitY;

            var faceColor = Vector4.One;
            if (primitiveColors != null)
                faceColor = primitiveColors.Values[primitive];
            else if (partColors != null)
                faceColor = _primitiveToPart[primitive] >= 0 ? partColors.Values[_primitiveToPart[primitive]] : Vector4.One;

            for (var c = start; c < end; c++)
            {
                var normal = vertexNormals != null ? vertexNormals.Values[c] : faceNormal;
                var uv = vertexUvs != null ? vertexUvs.Values[c] : Vector2.Zero;
                var uv2 = vertexUvs2 != null ? vertexUvs2.Values[c] : uv;
                var color = vertexColors != null ? vertexColors.Values[c] : faceColor;

                vertices[outStart + c - start] = new PbrVertex
                                                     {
                                                         Position = positions[vertexPoints[c]],
                                                         Normal = normal,
                                                         Texcoord = uv,
                                                         Texcoord2 = uv2,
                                                         Selection = 1,
                                                         ColorRgb = new Vector3(color.X, color.Y, color.Z),
                                                     };
            }

            // Tangent basis from the face's first triangle - exact for planar faces
            {
                var c0 = start;
                var c1 = start + 1;
                var c2 = start + 2;
                MeshUtils.CalcTBNSpace(positions[vertexPoints[c0]], vertexUvs?.Values[c0] ?? Vector2.Zero,
                                       positions[vertexPoints[c1]], vertexUvs?.Values[c1] ?? Vector2.UnitX,
                                       positions[vertexPoints[c2]], vertexUvs?.Values[c2] ?? Vector2.One,
                                       faceNormal, out var tangent, out var bitangent);
                if (tangent.LengthSquared() < 1e-10f || float.IsNaN(tangent.X))
                {
                    tangent = Vector3.Normalize(Vector3.Cross(faceNormal, Math.Abs(faceNormal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
                    bitangent = Vector3.Cross(faceNormal, tangent);
                }

                for (var v = outStart; v < outStart + cornerCount; v++)
                {
                    vertices[v].Tangent = tangent;
                    vertices[v].Bitangent = bitangent;
                }
            }

            // Fan triangulation (assumes convex faces - sufficient until a real triangulator lands)
            var triangleIndex = _primitiveTriangleStart[primitive];
            for (var i = 1; i < cornerCount - 1; i++)
                triangles[triangleIndex++] = new Int3(outStart, outStart + i, outStart + i + 1);
        }

        BuildChunks(primitiveCount, relativeToPartPivots);
    }

    private void BuildChunks(int primitiveCount, bool relativeToPartPivots)
    {
        var vertices = Vertices;
        if (_partCount == 0)
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

        if (Chunks.Length != _partCount)
            Chunks = new MeshChunkDef[_partCount];

        for (var partIndex = 0; partIndex < _partCount; partIndex++)
        {
            var first = Math.Clamp(_partStarts[partIndex], 0, primitiveCount);
            var end = Math.Clamp(first + _partCounts[partIndex], first, primitiveCount);
            var vertexStart = _primitiveVertexStart[first];
            var vertexEnd = _primitiveVertexStart[end];

            if (relativeToPartPivots)
            {
                var pivot = _partPivots[partIndex];
                for (var v = vertexStart; v < vertexEnd; v++)
                    vertices[v].Position -= pivot;
            }

            Chunks[partIndex] = new MeshChunkDef
                                    {
                                        StartFaceIndex = _primitiveTriangleStart[first],
                                        FaceCount = _primitiveTriangleStart[end] - _primitiveTriangleStart[first],
                                        StartVertexIndex = vertexStart,
                                        VertexCount = vertexEnd - vertexStart,
                                    };
        }
    }

    private void BuildPrimitiveToPartMap(int primitiveCount)
    {
        if (_primitiveToPart.Length != primitiveCount)
            _primitiveToPart = new int[primitiveCount];
        Array.Fill(_primitiveToPart, -1);

        for (var partIndex = 0; partIndex < _partCount; partIndex++)
        {
            var end = Math.Min(_partStarts[partIndex] + _partCounts[partIndex], primitiveCount);
            for (var primitive = _partStarts[partIndex]; primitive < end; primitive++)
                _primitiveToPart[primitive] = partIndex;
        }
    }

    private void SetPartCount(int count)
    {
        _partCount = count;
        if (_partStarts.Length >= count)
            return;

        _partStarts = new int[count];
        _partCounts = new int[count];
        _partPivots = new Vector3[count];
    }

    private void SetPart(int index, int primitiveStart, int primitiveCount, Vector3 pivot)
    {
        _partStarts[index] = primitiveStart;
        _partCounts[index] = primitiveCount;
        _partPivots[index] = pivot;
    }

    private int[] _primitiveVertexStart = [];
    private int[] _primitiveTriangleStart = [];
    private int[] _primitiveToPart = [];
    private int _partCount;
    private int[] _partStarts = [];
    private int[] _partCounts = [];
    private Vector3[] _partPivots = [];
}

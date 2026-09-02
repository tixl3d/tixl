using T3.Core.Rendering;
using T3.Core.Utils.Geometry;

namespace Lib.geometry;

[Guid("4fea8c0e-cb7f-41fe-a410-df1d3886bf98")]
internal sealed class GeometryToMeshBuffers : Instance<GeometryToMeshBuffers>
{
    [Output(Guid = "4bf00eb5-a39c-4567-a81e-46c2276d1418")]
    public readonly Slot<MeshBuffers> Buffers = new();

    public GeometryToMeshBuffers()
    {
        Buffers.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var geometry = Geometry.GetValue(context);
        if (geometry == null || geometry.FaceCount == 0)
        {
            Buffers.Value = null;
            return;
        }

        var cornerCount = geometry.CornerCount;
        var triangleCount = geometry.GetTriangleCount();
        if (_vertexData.Length != cornerCount)
            _vertexData = new PbrVertex[cornerCount];
        if (_indexData.Length != triangleCount)
            _indexData = new Int3[triangleCount];

        // Attribute lookups once, outside the loops
        geometry.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var cornerNormals);
        geometry.Attributes.TryGet<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, out var cornerUvs);
        geometry.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Corner, out var cornerColors);

        var positions = geometry.Positions;
        var offsets = geometry.FaceCornerOffsets;
        var cornerPoints = geometry.CornerPointIndices;

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

            for (var c = start; c < end; c++)
            {
                var normal = cornerNormals != null ? cornerNormals.Values[c] : faceNormal;
                var uv = cornerUvs != null ? cornerUvs.Values[c] : Vector2.Zero;
                var color = cornerColors != null ? cornerColors.Values[c] : Vector4.One;

                _vertexData[c] = new PbrVertex
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
                    _vertexData[c].Tangent = tangent;
                    _vertexData[c].Bitangent = bitangent;
                }
            }

            // Fan triangulation (assumes convex faces - sufficient until a real triangulator lands)
            for (var i = 1; i < faceCornerCount - 1; i++)
            {
                _indexData[triangleIndex++] = new Int3(start, start + i, start + i + 1);
            }
        }

        ResourceManager.SetupBufferWithViews(_vertexData, ref _vertexBufferWithViews);
        ResourceManager.SetupBufferWithViews(_indexData, ref _indexBufferWithViews);
        _data.VertexBuffer = _vertexBufferWithViews;
        _data.IndicesBuffer = _indexBufferWithViews;
        Buffers.Value = _data;
    }

    private PbrVertex[] _vertexData = [];
    private Int3[] _indexData = [];
    private BufferWithViews _vertexBufferWithViews;
    private BufferWithViews _indexBufferWithViews;
    private readonly MeshBuffers _data = new();

    [Input(Guid = "57469ea6-f0c5-4e8d-a53c-40e8ac495bc3")]
    public readonly InputSlot<MeshGeometry> Geometry = new();
}

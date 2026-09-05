using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;


namespace Lib.mesh.generate;

[Guid("5fb3dafe-aed4-4fff-a5b9-c144ea023d35")]
internal sealed class SphereMesh : Instance<SphereMesh>
{
    [Output(Guid = "322717ef-3a76-4e23-845f-a12a03d73969")]
    public readonly Slot<MeshBuffers> Data = new();

    public SphereMesh()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            var radius = Radius.GetValue(context);
            var segments = Segments.GetValue(context);
            var uSegments = segments.Width.Clamp(2, 10000) + 1;
            var vSegments = segments.Height.Clamp(2, 10000) + 1;
            var polTriangleCount = 2 * uSegments;
            var sideTriangleCount = (vSegments - 2) * uSegments * 2;
            var triangleCount = polTriangleCount + sideTriangleCount;
            var verticesCount = (vSegments + 1) * (uSegments);

            // Create buffers
            if (_vertexBufferData.Length != verticesCount)
                _vertexBufferData = new PbrVertex[verticesCount];

            if (_indexBufferData.Length != triangleCount)
                _indexBufferData = new Int3[triangleCount];

            // Initialize
            var vAngleFraction = 1f / (vSegments - 1) * 1.0 * Math.PI;
            var uAngleFraction = 1f / (uSegments - 1) * 2.0 * Math.PI;

            for (int vIndex = 0; vIndex < vSegments; ++vIndex)
            {
                var vAngle = vIndex * vAngleFraction;
                var tubePosition1Y = Math.Cos(vAngle) * radius;
                var radius1 = Math.Sin(vAngleFraction * vIndex) * radius;

                var v0 = 1 - vIndex / (float)(vSegments - 1);

                var isTop = vIndex == 0;
                var isBottom = vIndex == vSegments - 1;

                if (isTop || isBottom)
                {
                    var normalPol0 = radius > 0 ? VectorT3.Up : VectorT3.Down;
                    var normalPol1 = radius > 0 ? VectorT3.Down : VectorT3.Up;

                    for (int uIndex = 0; uIndex < uSegments; ++uIndex)
                    {
                        var u0 = (uIndex) / (float)(uSegments - 1);
                        var uAngle = uIndex * uAngleFraction;

                        // top
                        var tangentPol0 = Vector3.Normalize(new Vector3(MathF.Sin((float)uAngle),
                                                                        0,
                                                                        MathF.Cos((float)uAngle)));
                        var binormalPol0 = Vector3.Normalize(new Vector3(MathF.Sin((float)uAngle + MathF.PI / 2),
                                                                         0,
                                                                         MathF.Cos((float)uAngle + MathF.PI / 2)));

                        var pPol0 = new Vector3(0,
                                                radius,
                                                0);

                        var uv0 = new Vector2(u0, 1);

                        _vertexBufferData[0 + uIndex] = new PbrVertex
                                                            {
                                                                Position = pPol0,
                                                                Normal = normalPol0,
                                                                Tangent = tangentPol0,
                                                                Bitangent = binormalPol0,
                                                                Texcoord = uv0,
                                                                Selection = 1,
                                                                ColorRgb = Vector3.One,
                                                            };

                            
                        // bottom
                        var tangentPol1 = Vector3.Normalize(new Vector3(MathF.Sin((float)uAngle +  MathF.PI / 2),
                                                                        0,
                                                                        MathF.Cos((float)uAngle + MathF.PI / 2)));
                        var binormalPol1 = Vector3.Normalize(new Vector3(MathF.Sin((float)uAngle),
                                                                         0,
                                                                         MathF.Cos((float)uAngle )));

                        var pPol1 = new Vector3(0,
                                                -radius,
                                                0);

                        var uv1 = new Vector2(u0, 0);

                        _vertexBufferData[ (vSegments - 1) * uSegments + uIndex] = new PbrVertex
                                                                                       {
                                                                                           Position = pPol1,
                                                                                           Normal = normalPol1,
                                                                                           Tangent = tangentPol1,
                                                                                           Bitangent = binormalPol1,
                                                                                           Texcoord = uv1,
                                                                                           Selection = 1,
                                                                                           ColorRgb = Vector3.One,
                                                                                       };
                            
                            
                            
                        if (uIndex >= uSegments - 1)
                            continue;

                        _indexBufferData[uIndex] = new Int3(uIndex,
                                                            uIndex + uSegments,
                                                            uIndex + uSegments + 1);

                        _indexBufferData[uIndex] = new Int3(uIndex,
                                                            uIndex + uSegments,
                                                            uIndex + uSegments + 1);
                    }
                }
                else
                {
                    for (int uIndex = 0; uIndex < uSegments; ++uIndex)
                    {
                        var vVertexIndex = vIndex * uSegments;
                        var faceIndex = 2 * (uIndex + vIndex * (uSegments - 1));

                        var u0 = (uIndex) / (float)(uSegments - 1);
                        var uAngle = uIndex * uAngleFraction;

                        var p = new Vector3((float)(Math.Sin(uAngle) * radius1),
                                            (float)tubePosition1Y,
                                            (float)(Math.Cos(uAngle) * radius1)
                                           );

                        var uv0 = new Vector2(u0, v0);

                        var normal0 = Vector3.Normalize(p);
                        var tangent0 = Vector3.Normalize(new Vector3(normal0.Z, 0, -normal0.X));
                        var binormal0 = Vector3.Cross(normal0, tangent0);

                        _vertexBufferData[vVertexIndex + uIndex] = new PbrVertex
                                                                       {
                                                                           Position = p,
                                                                           Normal = normal0,
                                                                           Tangent = tangent0,
                                                                           Bitangent = binormal0,
                                                                           Texcoord = uv0,
                                                                           Selection = 1,
                                                                           ColorRgb = Vector3.One,
                                                                       };

                        if (vIndex >= vSegments - 1 || uIndex >= uSegments - 1)
                            continue;

                        var nextUIndex = (uIndex + 1) % uSegments;

                        _indexBufferData[faceIndex + 0] = new Int3(vVertexIndex + nextUIndex,
                                                                   vVertexIndex + uIndex + 0,
                                                                   vVertexIndex + uIndex + uSegments);
                        _indexBufferData[faceIndex + 1] = new Int3((vVertexIndex + nextUIndex + uSegments),
                                                                   (vVertexIndex + nextUIndex),
                                                                   vVertexIndex + uIndex + uSegments + 0);
                    }
                }
            }

            // Write Data
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, PbrVertex.Stride * verticesCount, PbrVertex.Stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;

            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * triangleCount, stride, ref _indexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_indexBuffer, ref _indexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_indexBuffer, UnorderedAccessViewBufferFlags.None, ref _indexBufferWithViews.Uav);
            _indexBufferWithViews.Buffer = _indexBuffer;

            _data.VertexBuffer = _vertexBufferWithViews;
            _data.IndicesBuffer = _indexBufferWithViews;
            Data.Value = _data;
            Data.DirtyFlag.Clear();
        }
        catch (Exception e)
        {
            Log.Error("Failed to create sphere mesh:" + e.Message);
        }
    }

    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();
    private readonly MeshBuffers _data = new();

    [Input(Guid = "24a1e643-3e52-4a8b-97b6-7c6f1706d14c")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "6f327667-9054-4952-9f8f-570fa5497b13")]
    public readonly InputSlot<Int2> Segments = new();
}
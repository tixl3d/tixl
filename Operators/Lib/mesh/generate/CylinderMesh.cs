using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;


namespace Lib.mesh.generate;

[Guid("5777a005-bbae-48d6-b633-5e998ca76c91")]
internal sealed class CylinderMesh : Instance<CylinderMesh>
{
    [Output(Guid = "b4bed6e3-bef5-4601-99bd-f85bf1a956f5")]
    public readonly Slot<MeshBuffers> Data = new();

    public CylinderMesh()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            var rotation = Rotation.GetValue(context);
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(rotation.Y.ToRadians(),
                                                                  rotation.X.ToRadians(),
                                                                  rotation.Z.ToRadians());

            var lowerRadius = Radius.GetValue(context);
            var upperRadius = lowerRadius + RadiusOffset.GetValue(context);
            var height = Height.GetValue(context);

            var rows = Rows.GetValue(context).Clamp(1, 10000);
            var columns = Columns.GetValue(context).Clamp(1, 10000);

            var center = Center.GetValue(context);

            var spinInRad = Spin.GetValue(context) * MathUtils.ToRad;
            var twistInRad = Twist.GetValue(context) * MathUtils.ToRad;
            var basePivot = BasePivot.GetValue(context);

            var fillRatio = Fill.GetValue(context) / 360f;
            var capSegments = CapSegments.GetValue(context).Clamp(0, 1000);
            var addCaps = capSegments > 0;
            //var isHullClosed = true; //Math.Abs(fillRatio - 1) < 0.01f;

            var isFlipped = lowerRadius < 0;

            var vertexHullColumns = columns + 1;

            var hullVerticesCount = (rows + 1) * vertexHullColumns;
            var hullTriangleCount = rows * columns * 2;

            var capsVertexCount = +2 * (capSegments * vertexHullColumns + 1);
            var capsTriangleCount = 2 * ((capSegments - 1) * columns * 2 + columns);

            var totalVertexCount = hullVerticesCount + (addCaps ? capsVertexCount : 0);
            var totalTriangleCount = hullTriangleCount + (addCaps ? capsTriangleCount : 0);

            // Create buffers
            if (_vertexBufferData.Length != totalVertexCount)
                _vertexBufferData = new PbrVertex[totalVertexCount];

            if (_indexBufferData.Length != totalTriangleCount)
                _indexBufferData = new Int3[totalTriangleCount];

            // Initialize
            var radiusAngleFraction = fillRatio / (vertexHullColumns - 1) * 2.0 * Math.PI;
            var rowStep = height / rows;

            var squeezeAngle = MathF.Atan2(upperRadius - lowerRadius, height);
                
            // Hull
            for (var rowIndex = 0; rowIndex < rows + 1; ++rowIndex)
            {
                var heightFraction = rowIndex / (float)rows;
                var rowRadius = MathUtils.Lerp(lowerRadius, upperRadius, heightFraction);
                var rowLevel = height * (heightFraction - basePivot);

                for (var columnIndex = 0; columnIndex < vertexHullColumns; ++columnIndex)
                {
                    var columnAngle = (float)( columnIndex * radiusAngleFraction + spinInRad + twistInRad * heightFraction + Math.PI);

                    var u0 = columnIndex / (float)columns;
                    var v1 = addCaps ? (rowIndex / (float)rows) / 2f
                                 : rowIndex / (float)rows;

                    var p = new Vector3(MathF.Sin(columnAngle) * rowRadius,
                                        rowLevel,
                                        MathF.Cos(columnAngle) * rowRadius);
                        

                    var uv0 = new Vector2(u0, v1);
                        
                    var normal0 = new Vector3(MathF.Sin(columnAngle) * MathF.Cos(squeezeAngle),
                                              MathF.Cos(-squeezeAngle-MathF.PI/2),
                                              MathF.Cos(columnAngle) * MathF.Cos(squeezeAngle)
                                             );
                        
                    var binormal0 = new Vector3(MathF.Sin(squeezeAngle) * MathF.Sin(columnAngle),
                                                MathF.Cos(-squeezeAngle),
                                                MathF.Sin(squeezeAngle) * MathF.Cos(columnAngle)
                                               );
                        
                    var tangent0 = Vector3.Cross(-normal0, binormal0);

                    var vertexIndex = rowIndex * vertexHullColumns + columnIndex;
                    p = Vector3.TransformNormal(p, rotationMatrix);
                    _vertexBufferData[vertexIndex] = new PbrVertex
                                                         {
                                                             Position = p + center,
                                                             Normal =  Vector3.TransformNormal((isFlipped ? normal0 * -1 : normal0), rotationMatrix),
                                                             Tangent = Vector3.TransformNormal(tangent0,rotationMatrix),
                                                             Bitangent = Vector3.TransformNormal(isFlipped ? binormal0 * -1 : binormal0, rotationMatrix),
                                                             Texcoord = uv0,
                                                             Texcoord2 = uv0,
                                                             Selection = 1,
                                                             ColorRgb = Vector3.One,
                                                         };

                    var faceIndex = 2 * (rowIndex * (vertexHullColumns - 1) + columnIndex);
                    if (columnIndex < vertexHullColumns - 1 && rowIndex < rows)
                    {
                        if (isFlipped)
                        {
                            _indexBufferData[faceIndex + 0] = new Int3(vertexIndex + vertexHullColumns, vertexIndex + 1, vertexIndex + 0);
                            _indexBufferData[faceIndex + 1] =
                                new Int3(vertexIndex + vertexHullColumns + 1, vertexIndex + 1, vertexIndex + vertexHullColumns);
                        }
                        else
                        {
                            _indexBufferData[faceIndex + 0] = new Int3(vertexIndex + 0, vertexIndex + 1, vertexIndex + vertexHullColumns);
                            _indexBufferData[faceIndex + 1] =
                                new Int3(vertexIndex + vertexHullColumns, vertexIndex + 1, vertexIndex + vertexHullColumns + 1);
                        }
                    }
                }
            }

            // Caps
            if (addCaps)
            {
                for (var capIndex = 0; capIndex <= 1; capIndex++)
                {
                    var isLowerCap = capIndex == 0;
                    var capLevel = ((isLowerCap ? 0 : 1) - basePivot) * height;
                    var capRadius = isLowerCap ? lowerRadius : upperRadius;
                    var isReverse = isFlipped ^ isLowerCap;

                    var centerVertexIndex = hullVerticesCount + (capsVertexCount / 2) * (capIndex + 1) - 1;

                    for (var capSegmentIndex = 0; capSegmentIndex < capSegments; ++capSegmentIndex)
                    {
                        var isCenterSegment = capSegmentIndex == capSegments - 1;

                        var capFraction = 1f - (capSegmentIndex) / (float)capSegments;
                        var nextCapFraction = 1f - (capSegmentIndex + 1) / (float)capSegments;
                        var radius = capRadius * capFraction;
                        //var nextRadius = upperRadius * nextCapFraction;

                            
                        for (var columnIndex = 0; columnIndex < vertexHullColumns; ++columnIndex)
                        {
                            var columnAngle = (float)(columnIndex * radiusAngleFraction + spinInRad + twistInRad * (isLowerCap ? 0 : 1) );

                            var xx = MathF.Sin(columnAngle);
                            var yy = MathF.Cos(columnAngle);
                                
                            var p = new Vector3(-xx * radius,
                                                capLevel,
                                                -yy * radius);

                            var normal0 = isLowerCap ? VectorT3.Down : VectorT3.Up;

                            var tangent0 = VectorT3.Left;
                            var binormal0 = VectorT3.ForwardRH;

                            var vertexIndex = capSegmentIndex * vertexHullColumns + columnIndex
                                                                                  + hullVerticesCount + (capsVertexCount / 2) * capIndex;

                            // Write vertex
                            var capUvOffset = isLowerCap 
                                                  ? new Vector2(0.25f, 0.75f)
                                                  : new Vector2(0.75f, 0.75f);
                                
                            p = Vector3.TransformNormal(p, rotationMatrix);                                
                            _vertexBufferData[vertexIndex] = new PbrVertex
                                                                 {
                                                                     Position = p + center,
                                                                     Normal = isFlipped ? normal0 * -1 : normal0,
                                                                     Tangent = tangent0,
                                                                     Bitangent = isFlipped ? binormal0 * -1 : binormal0,
                                                                     Texcoord = new Vector2(-xx * (isLowerCap ? -1 : 1),yy) * capFraction/4 + capUvOffset,
                                                                     Selection = 1,
                                                                     ColorRgb = Vector3.One,
                                                                 };

                            if (isCenterSegment) 
                            {
                                if (columnIndex == 0)
                                {
                                    var p2 = new Vector3(0, capLevel, 0);
                                    p2 = Vector3.TransformNormal(p2, rotationMatrix);
                                    _vertexBufferData[centerVertexIndex] = new PbrVertex
                                                                               {
                                                                                   Position = p2 + center,
                                                                                   Normal = (isFlipped) ? normal0 * -1 : normal0,
                                                                                   Tangent = tangent0,
                                                                                   Bitangent = (isFlipped ^ isLowerCap) ? binormal0 * -1 : binormal0,
                                                                                   Texcoord =  capUvOffset,
                                                                                   Selection = 1,
                                                                                   ColorRgb = Vector3.One
                                                                               };
                                }
                            }

                            if (columnIndex < vertexHullColumns - 1 && capSegmentIndex < capSegments)
                            {
                                if (isCenterSegment)
                                {
                                    var faceIndex = (capSegmentIndex * (vertexHullColumns - 1) * 2 + columnIndex)
                                                    + hullTriangleCount + (capsTriangleCount / 2) * capIndex;

                                    var f1 = isReverse
                                                 ? new Int3(vertexIndex + 1, vertexIndex, centerVertexIndex)
                                                 : new Int3(centerVertexIndex, vertexIndex, vertexIndex + 1);
                                    _indexBufferData[faceIndex] = f1;
                                }
                                else
                                {
                                    var faceIndex = (capSegmentIndex * (vertexHullColumns - 1) * 2) + columnIndex * 2
                                                                                                    + hullTriangleCount +
                                                                                                    (capsTriangleCount / 2) * capIndex;

                                    _indexBufferData[faceIndex]
                                        = isReverse
                                              ? new Int3(vertexIndex + vertexHullColumns, vertexIndex + 1, vertexIndex)
                                              : new Int3(vertexIndex, vertexIndex + 1, vertexIndex + vertexHullColumns);

                                    _indexBufferData[faceIndex + 1]
                                        = isReverse
                                              ? new Int3(vertexIndex + vertexHullColumns, vertexIndex + vertexHullColumns + 1, vertexIndex + 1)
                                              : new Int3(vertexIndex + 1, vertexIndex + vertexHullColumns + 1, vertexIndex + vertexHullColumns);
                                }
                            }
                        }
                    }
                }
            }

            // Write Data
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, PbrVertex.Stride * totalVertexCount, PbrVertex.Stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;

            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * totalTriangleCount, stride, ref _indexBuffer);
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
            Log.Error("Failed to create torus mesh:" + e.Message);
        }
    }

    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();

    private readonly MeshBuffers _data = new();

    [Input(Guid = "8d290afb-2574-4afa-a545-a0d3588f89f6")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "4C91B66C-670D-45FF-94CC-01D1A68CD040")]
    public readonly InputSlot<float> RadiusOffset = new();

    [Input(Guid = "57f3310c-6ed2-4a52-af72-43e083f73360")]
    public readonly InputSlot<float> Height = new();

    [Input(Guid = "4DD4C4F0-C6B7-4EE8-92E2-CB8DF6131E0A")]
    public readonly InputSlot<int> Rows = new();

    [Input(Guid = "321693A5-4E2C-47A0-A42E-95CBDC6EBF80")]
    public readonly InputSlot<int> Columns = new();

    [Input(Guid = "DB5E3C51-5765-44D8-A61B-A7B552FCE5B3")]
    public readonly InputSlot<int> CapSegments = new();
        
    [Input(Guid = "C29B5881-85BC-4D29-BC72-6DD36730FA8F")]
    public readonly InputSlot<float> Spin = new();

    [Input(Guid = "1D1CE8C4-FD3C-4D69-BE0E-679247A811C9")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "91FD4FBF-1CEC-4D89-8014-CEED0021A5EE")]
    public readonly InputSlot<float> Fill = new();

    [Input(Guid = "66332A91-E0C2-442A-99F6-347DEDAED72E")]
    public readonly InputSlot<Vector3> Center = new();

        
    [Input(Guid = "6DDF5966-9140-4BEA-A56B-20690F9F436F")]
    public readonly InputSlot<float> BasePivot = new();

    [Input(Guid = "4C7E0F67-A35B-4A23-B640-B0375C1A3259")]
    public readonly InputSlot<Vector3> Rotation = new();

}
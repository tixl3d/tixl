using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;


namespace Lib.mesh.generate;

[Guid("c47ab830-aae7-4f8f-b67c-9119bcbaf7df")]
internal sealed class CubeMesh : Instance<CubeMesh>
{
    [Output(Guid = "35660e2b-5005-44a2-bf57-db9a3f1b791d")]
    public readonly Slot<MeshBuffers> Data = new();

    public CubeMesh()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {

            var scale = Scale.GetValue(context);
            var stretch = Stretch.GetValue(context);
            var pivot = Pivot.GetValue(context);
            var rotation = Rotation.GetValue(context);
            var cubeRotationMatrix = Matrix4x4.CreateFromYawPitchRoll(MathUtils.ToRad * (rotation.Y),
                                                                      MathUtils.ToRad * (rotation.X),
                                                                      MathUtils.ToRad * (rotation.Z));

            var center = Center.GetValue(context);
            // var offset = new SharpDX.Vector3(stretch.X * scale * (pivot.X - 0.5f),
            //                                  stretch.Y * scale * (pivot.Y - 0.5f),
            //                                  stretch.Z * scale * (pivot.Z - 0.5f));
            var uvMapMode = TexCoord.GetValue(context);
            var uvMapMode2 = TexCoord2.GetValue(context);
            var margin = Margin.GetValue(context);
            var margin2 = Margin2.GetValue(context);
            // Initialize UV mapper based on selected mode
            IUvMapper uvMapper = GetUvMapper(uvMapMode);
            IUvMapper uvMapper2 = GetUvMapper(uvMapMode2);

            var offset = -Vector3.One * 0.5f;

            var center2 = new Vector3(center.X, center.Y, center.Z);

            var segments = Segments.GetValue(context);
            _xSegments = segments.X.Clamp(1, 10000) + 1;
            _ySegments = segments.Y.Clamp(1, 10000) + 1;
            _zSegments = segments.Z.Clamp(1, 10000) + 1;

            var faceCount = (_ySegments - 1) * (_xSegments - 1) * 2 * 2 // front / back
                            + (_ySegments - 1) * (_zSegments - 1) * 2 * 2 // top / bottom
                            + (_xSegments - 1) * (_zSegments - 1) * 2 * 2; // left / right

            var verticesCount = (_ySegments * _xSegments + _ySegments * _zSegments + _xSegments * _zSegments) * 2;

            // Create buffers
            if (_vertexBufferData.Length != verticesCount)
                _vertexBufferData = new PbrVertex[verticesCount];

            if (_indexBufferData.Length != faceCount)
                _indexBufferData = new Int3[faceCount];

            int sideFaceIndex = 0;
            int sideVertexIndex = 0;

            for (var sideIndex = 0; sideIndex < _sides.Length; sideIndex++)
            {
                var side = _sides[sideIndex];

                var sideRotationMatrix = Matrix4x4.CreateFromYawPitchRoll(side.SideRotation.Y,
                                                                          side.SideRotation.X,
                                                                          side.SideRotation.Z);

                var rotationMatrix = Matrix4x4.Multiply(sideRotationMatrix, cubeRotationMatrix);

                var columnCount = GetSegmentCountForAxis(side.ColumnAxis);
                var rowCount = GetSegmentCountForAxis(side.RowAxis);
                var columnStretch = GetComponentForAxis(side.ColumnAxis, stretch);
                var rowStretch = GetComponentForAxis(side.RowAxis, stretch);
                var depthStretch = GetComponentForAxis(side.DepthAxis, stretch);

                double columnStep = 1.0 / (columnCount - 1);
                double rowStep = 1.0 / (rowCount - 1);
                float depthScale = 1f;

                var normal = Vector3.TransformNormal(VectorT3.ForwardLH, rotationMatrix);
                var tangent = Vector3.TransformNormal(VectorT3.Right, rotationMatrix);
                var binormal = Vector3.TransformNormal(VectorT3.Up, rotationMatrix);

                // Initialize
                for (int columnIndex = 0; columnIndex < columnCount; ++columnIndex)
                {
                    var columnFragment = (float)(columnIndex * columnStep);

                    var u0 = columnIndex / ((float)columnCount - 1);

                    for (int rowIndex = 0; rowIndex < rowCount; ++rowIndex)
                    {
                        var rowFragment = (float)(rowIndex * rowStep);
                        var vertexIndex = rowIndex + columnIndex * rowCount + sideVertexIndex;
                        var faceIndex = 2 * (rowIndex + columnIndex * (rowCount - 1)) + sideFaceIndex;

                        var p = new Vector3(columnFragment,
                                            rowFragment,
                                            depthScale);

                        var v0 = (rowIndex) / ((float)rowCount - 1);
                        // Apply UV mapping based on selected mode
                        var uv0 = uvMapper.CalculateUV(u0, v0, sideIndex, margin);
                        var uv1 = uvMapper2.CalculateUV(u0, v0, sideIndex, margin2);

                        var position = (Vector3.TransformNormal(p + offset, sideRotationMatrix) + pivot) * stretch * scale;
                        position = Vector3.TransformNormal(position, cubeRotationMatrix);

                        _vertexBufferData[vertexIndex + 0] = new PbrVertex
                                                                 {
                                                                     Position = position + center2,
                                                                     Normal = normal,
                                                                     Tangent = tangent,
                                                                     Bitangent = binormal,
                                                                     Texcoord = uv0,
                                                                     Texcoord2 = uv1,
                                                                     Selection = 1,
                                                                 };

                        if (columnIndex >= columnCount - 1 || rowIndex >= rowCount - 1)
                            continue;

                        _indexBufferData[faceIndex + 0] = new Int3(vertexIndex, vertexIndex + rowCount, vertexIndex + 1);
                        _indexBufferData[faceIndex + 1] = new Int3(vertexIndex + rowCount, vertexIndex + rowCount + 1, vertexIndex + 1);
                    }
                }

                sideVertexIndex += columnCount * rowCount;
                sideFaceIndex += (columnCount - 1) * (rowCount - 1) * 2;
            }

            // Write Data
            ResourceManager.SetupStructuredBuffer(_vertexBufferData, PbrVertex.Stride * verticesCount, PbrVertex.Stride, ref _vertexBuffer);
            ResourceManager.CreateStructuredBufferSrv(_vertexBuffer, ref _vertexBufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_vertexBuffer, UnorderedAccessViewBufferFlags.None, ref _vertexBufferWithViews.Uav);
            _vertexBufferWithViews.Buffer = _vertexBuffer;

            const int stride = 3 * 4;
            ResourceManager.SetupStructuredBuffer(_indexBufferData, stride * faceCount, stride, ref _indexBuffer);
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
            Log.Error("Failed to create cube mesh:" + e.Message);
        }
    }

    private int GetSegmentCountForAxis(SegmentAxis axis)
    {
        return axis switch
                   {
                       SegmentAxis.X => _xSegments,
                       SegmentAxis.Y => _ySegments,
                       _             => _zSegments
                   };
    }

    private float GetComponentForAxis(SegmentAxis axis, Vector3 vector)
    {
        return axis switch
                   {
                       SegmentAxis.X => vector.X,
                       SegmentAxis.Y => vector.Y,
                       _             => vector.Z
                   };
    }

    private enum SegmentAxis
    {
        X,
        Y,
        Z,
    }

    private int _xSegments;
    private int _ySegments;
    private int _zSegments;

    private struct Side
    {
        public Vector3 Normal;
        public Vector3 Tangent;
        public Vector3 Binormal;
        public Vector2 UvScale;
        public Vector2 UvOffset;
        public SegmentAxis ColumnAxis;
        public SegmentAxis RowAxis;
        public SegmentAxis DepthAxis;
        public Vector3 SideRotation;
    }

    static private Side[] _sides =
        {
            // Front
            new()
                {
                    Normal = VectorT3.ForwardLH,
                    Tangent = VectorT3.Right,
                    Binormal = VectorT3.Up,
                    UvScale = Vector2.One,
                    UvOffset = Vector2.Zero,
                    ColumnAxis = SegmentAxis.X,
                    RowAxis = SegmentAxis.Y,
                    DepthAxis = SegmentAxis.Z,
                    SideRotation = Vector3.Zero
                },
            // Right
            new()
                {
                    Normal = default,
                    Tangent = default,
                    Binormal = default,
                    UvScale = Vector2.One,
                    UvOffset = Vector2.Zero,
                    ColumnAxis = SegmentAxis.Z,
                    RowAxis = SegmentAxis.Y,
                    DepthAxis = SegmentAxis.X,
                    SideRotation = new Vector3(0, (float)(Math.PI * 0.5), 0),
                },
            // Back
            new()
                {
                    Normal = -VectorT3.ForwardLH,
                    Tangent = -VectorT3.Right,
                    Binormal = -VectorT3.Up,
                    UvScale = default,
                    UvOffset = default,
                    ColumnAxis = SegmentAxis.X,
                    RowAxis = SegmentAxis.Y,
                    DepthAxis = SegmentAxis.Z,
                    SideRotation = new Vector3(0, (float)Math.PI, 0),
                },
            // Left
            new()
                {
                    Normal = default,
                    Tangent = default,
                    Binormal = default,
                    UvScale = Vector2.One,
                    UvOffset = Vector2.Zero,
                    ColumnAxis = SegmentAxis.Z,
                    RowAxis = SegmentAxis.Y,
                    DepthAxis = SegmentAxis.X,
                    SideRotation = new Vector3(0, (float)(Math.PI * 1.5), 0),
                },
            // Top
            new()
                {
                    Normal = default,
                    Tangent = default,
                    Binormal = default,
                    UvScale = Vector2.One,
                    UvOffset = Vector2.Zero,
                    ColumnAxis = SegmentAxis.X,
                    RowAxis = SegmentAxis.Z,
                    DepthAxis = SegmentAxis.Y,
                    SideRotation = new Vector3((float)(Math.PI * 0.5), 0, 0),
                },
            // Bottom
            new()
                {
                    Normal = default,
                    Tangent = default,
                    Binormal = default,
                    UvScale = Vector2.One,
                    UvOffset = Vector2.Zero,
                    ColumnAxis = SegmentAxis.X,
                    RowAxis = SegmentAxis.Z,
                    DepthAxis = SegmentAxis.Y,
                    SideRotation = new Vector3((float)(Math.PI * 1.5), 0, 0),
                },
        };

    private IUvMapper GetUvMapper(int uvMapMode)
    {
        return uvMapMode switch
        {
            0 => new StandardUvMapper(),           // Standard/Default mapping
            1 => new UnwrappedCubeMapper(),        // Unwrapped cube map with padding
            2 => new CubeMap(),                    // CubeMap normalized
            3 => new CubeMapSquare(),              // CubeMap for square texture
            _ => new StandardUvMapper()            // Default fallback
        };
    }
    // Interface for UV mapping strategies
    private interface IUvMapper
    {
        Vector2 CalculateUV(float u, float v, int sideIndex, float padding);
    }
    // Standard/Default UV mapping
    private class StandardUvMapper : IUvMapper
    {
        public Vector2 CalculateUV(float u, float v, int sideIndex, float padding)
        {
            // Compute the scaled UVs centered at (0.5, 0.5)
            float scaledU = 0.5f + (u - 0.5f) * (1f - 2f * padding);
            float scaledV = 0.5f + (v - 0.5f) * (1f - 2f * padding);

            return new Vector2(scaledU, scaledV);
        }
    }
    // Unwrapped cube map with padding
    private class UnwrappedCubeMapper : IUvMapper
    {
        public Vector2 CalculateUV(float u, float v, int sideIndex, float padding)
        {
            // UV island padding (adjust as needed)
            //float padding = 0.00f;

            // Calculate usable space (accounting for padding)
            float usableWidth = (1.0f - (4 * padding)) / 3.0f;  // 3 columns with 4 padding spaces
            float usableHeight = (1.0f - (3 * padding)) / 2.0f; // 2 rows with 3 padding spaces

            // Determine face position
            int column = sideIndex % 3;
            int row = sideIndex / 3;

            // Calculate actual face dimensions to maintain square aspect ratio
            float faceSize = Math.Min(usableWidth, usableHeight);

            // Center the face in its cell if there's extra space
            float xOffset = column * (usableWidth + padding) + padding;
            float yOffset = row * (usableHeight + padding) + padding;

            // If we're maintaining square aspect ratio and have extra space,
            // center the square in the available space
            if (usableWidth > usableHeight)
            {
                xOffset += (usableWidth - faceSize) / 2;
            }
            else if (usableHeight > usableWidth)
            {
                yOffset += (usableHeight - faceSize) / 2;
            }

            // Adjust UV coordinates to fit in the right grid cell with padding
            return new Vector2(
                (u * faceSize) + xOffset,
                (v * faceSize) + yOffset
            );
        }
    }
    // Custom layout with face-specific control
    private class CubeMap : IUvMapper
    {
        private static float _oneThird = 1.0f / 3.0f;
        // Define UV regions for each face [x, y, width, height]
        private static readonly Vector4[] _faceRegions = new Vector4[]
        {
        new Vector4(0.25f, _oneThird, 0.25f, _oneThird), // Front
        new Vector4(0.50f, _oneThird, 0.25f, _oneThird), // Right
        new Vector4(0.75f, _oneThird, 0.25f, _oneThird), // Back
        new Vector4(0.0f, _oneThird, 0.25f, _oneThird), // Left
        new Vector4(0.25f, 0.00f, 0.25f, _oneThird), // Top
        new Vector4(0.25f, _oneThird*2, 0.25f, _oneThird)  // Bottom
        };
        
        public Vector2 CalculateUV(float u, float v, int sideIndex, float padding)
        {
            // Get region for current face
            var region = _faceRegions[sideIndex];

            // Apply padding to avoid texture bleeding
            float x = region.X + padding;
            float y = region.Y + padding;
            float width = region.Z - (padding * 2);
            float height = region.W - (padding * 2);

            float xCenter = region.X + region.Z / 2;
            float yCenter = region.Y + region.W / 2;
            x = xCenter - width / 2 + padding;
            y = yCenter - height / 2 + padding;
            width = width - (padding * 2);
            height = height - (padding * 2);


            // Map UV to the adjusted region
            return new Vector2(
                x + (u * width),
                y + (v * height)
            );
        }
    }
    private class CubeMapSquare : IUvMapper
    {
        // Define UV regions for each face [x, y, width, height]
     
        private static readonly Vector4[] _faceRegions = new Vector4[]
       {
        new Vector4(0.25f, 0.25f+0.125f, 0.25f, 0.25f), // Front
        new Vector4(0.50f, 0.25f+0.125f, 0.25f, 0.25f), // Right
        new Vector4(0.75f, 0.25f+0.125f, 0.25f, 0.25f), // Back
        new Vector4(0.00f, 0.25f+0.125f, 0.25f, 0.25f), // Left
        new Vector4(0.25f, 0.00f+0.125f, 0.25f, 0.25f), // Top
        new Vector4(0.25f, 0.50f+0.125f, 0.25f, 0.25f)  // Bottom
       };

        public Vector2 CalculateUV(float u, float v, int sideIndex, float padding)
        {
            // Get region for current face
            var region = _faceRegions[sideIndex];

            // Apply padding to avoid texture bleeding
            float x = region.X + padding;
            float y = region.Y + padding;
            float width = region.Z - (padding * 2);
            float height = region.W - (padding * 2);

            // Make it square (using the smaller dimension)
            //float size = Math.Min(width, height);
            float xCenter = region.X + region.Z / 2;
            float yCenter = region.Y + region.W / 2;
            x = xCenter - width / 2 + padding;
            y = yCenter - height / 2 + padding;
            width = width - (padding * 2);
            height = height - (padding * 2);


            // Map UV to the adjusted region
            return new Vector2(
                x + (u * width),
                y + (v * height)
            );
        }
    }

    private Buffer _vertexBuffer;
    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private readonly BufferWithViews _vertexBufferWithViews = new();

    private Buffer _indexBuffer;
    private Int3[] _indexBufferData = new Int3[0];
    private readonly BufferWithViews _indexBufferWithViews = new();

    private readonly MeshBuffers _data = new();

    private enum UvModes
    {
        Default,
        Rows,
        CubeMap,
        CubeMapForSquare,
    }


    [Input(Guid = "E445A6DA-0B66-46AE-AD2B-650E9CC50798")]
    public readonly InputSlot<Int3> Segments = new();

    [Input(Guid = "97C9849E-751C-49A9-823D-0AF839FA503E")]
    public readonly InputSlot<Vector3> Stretch = new();

    [Input(Guid = "9a7d34a1-ca39-48bc-b977-9a786d23f3b1")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "FEBFAE90-13E8-4F0A-8CCF-B8825EA525F8")]
    public readonly InputSlot<Vector3> Pivot = new();

    [Input(Guid = "f4a78f77-8d8c-4b7b-8545-ea80947b428d")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "e641c244-9dc8-444d-8dee-c3e9b710f9db")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "e1adb865-42f5-46a9-a4ac-bfae00b03caa", MappedType = typeof(UvModes))]
    public readonly InputSlot<int> TexCoord = new();

    [Input(Guid = "573b74e2-8ab9-4364-9de7-605248528cee")]
    public readonly InputSlot<float> Margin = new();

    [Input(Guid = "f946d7a1-8bb6-4ba1-a3bb-f2f95fc2242d", MappedType = typeof(UvModes))]
    public readonly InputSlot<int> TexCoord2 = new();
   
    [Input(Guid = "ba355fc0-0f1c-41d9-842e-d8ae9bee162a")]
    public readonly InputSlot<float> Margin2 = new();

}
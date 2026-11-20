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
            var uvMapMode = TexCoord.GetValue(context);
            var uvMapMode2 = TexCoord2.GetValue(context);
            var margin = Margin.GetValue(context);
            var margin2 = Margin2.GetValue(context);
            // Initialize UV mapper based on selected mode
            IUvMapper uvMapper = GetUvMapper(uvMapMode);
            IUvMapper uvMapper2 = GetUvMapper(uvMapMode2);

            var size = stretch * scale;
            var halfExtent = Vector3.Abs(size) * 0.5f;
            var sizeSign = new Vector3(size.X < 0 ? -1 : 1, size.Y < 0 ? -1 : 1, size.Z < 0 ? -1 : 1);

            var smallestHalfExtent = MathF.Min(halfExtent.X, MathF.Min(halfExtent.Y, halfExtent.Z));
            _bevelWidth = (BevelWidth.GetValue(context) * MathF.Abs(scale)).Clamp(0, smallestHalfExtent);
            _bevelSides = BevelSides.GetValue(context).Clamp(1, 64);
            _bevelFlatness = BevelFlatness.GetValue(context).Clamp(0, 1);
            _oddSeamTan = MathF.Tan((_bevelSides - 1) / 2 * MathF.PI / (2 * _bevelSides));

            var segments = Segments.GetValue(context);
            FillAxisStations(ref _xStations, segments.X, halfExtent.X);
            FillAxisStations(ref _yStations, segments.Y, halfExtent.Y);
            FillAxisStations(ref _zStations, segments.Z, halfExtent.Z);

            var verticesCount = 0;
            var faceCount = 0;
            for (var sideIndex = 0; sideIndex < _sides.Length; sideIndex++)
            {
                var columnCount = GetStationsForAxis(_sides[sideIndex].ColumnAxis).Length;
                var rowCount = GetStationsForAxis(_sides[sideIndex].RowAxis).Length;
                verticesCount += columnCount * rowCount;
                faceCount += (columnCount - 1) * (rowCount - 1) * 2;
            }

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

                var columnDirection = Vector3.TransformNormal(VectorT3.Right, sideRotationMatrix);
                var rowDirection = Vector3.TransformNormal(VectorT3.Up, sideRotationMatrix);
                var depthDirection = Vector3.TransformNormal(VectorT3.ForwardLH, sideRotationMatrix);

                var columns = GetStationsForAxis(side.ColumnAxis);
                var rows = GetStationsForAxis(side.RowAxis);
                var columnCount = columns.Length;
                var rowCount = rows.Length;
                var depthInner = GetComponentForAxis(side.DepthAxis, halfExtent) - _bevelWidth;

                var faceTangent = Vector3.TransformNormal(VectorT3.Right, rotationMatrix);
                var faceBinormal = Vector3.TransformNormal(VectorT3.Up, rotationMatrix);

                for (int columnIndex = 0; columnIndex < columnCount; ++columnIndex)
                {
                    var u0 = columns[columnIndex].Fraction;

                    for (int rowIndex = 0; rowIndex < rowCount; ++rowIndex)
                    {
                        var vertexIndex = rowIndex + columnIndex * rowCount + sideVertexIndex;
                        var faceIndex = 2 * (rowIndex + columnIndex * (rowCount - 1)) + sideFaceIndex;

                        ComputeSurfacePoint(columns[columnIndex], rows[rowIndex], depthInner, out var facePosition, out var faceNormal);

                        var boxPosition = columnDirection * facePosition.X + rowDirection * facePosition.Y + depthDirection * facePosition.Z;
                        var position = Vector3.TransformNormal(boxPosition * sizeSign + pivot * size, cubeRotationMatrix);

                        var boxNormal = columnDirection * faceNormal.X + rowDirection * faceNormal.Y + depthDirection * faceNormal.Z;
                        var normal = Vector3.TransformNormal(boxNormal, cubeRotationMatrix);
                        var tangent = Vector3.Normalize(faceTangent - normal * Vector3.Dot(normal, faceTangent));
                        var binormal = Vector3.Normalize(faceBinormal - normal * Vector3.Dot(normal, faceBinormal));

                        var v0 = rows[rowIndex].Fraction;
                        // Apply UV mapping based on selected mode
                        var uv0 = uvMapper.CalculateUV(u0, v0, sideIndex, margin);
                        var uv1 = uvMapper2.CalculateUV(u0, v0, sideIndex, margin2);

                        _vertexBufferData[vertexIndex + 0] = new PbrVertex
                                                                 {
                                                                     Position = position + center,
                                                                     Normal = normal,
                                                                     Tangent = tangent,
                                                                     Bitangent = binormal,
                                                                     Texcoord = uv0,
                                                                     Texcoord2 = uv1,
                                                                     Selection = 1,
                                                                     ColorRgb = Vector3.One,
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
            ResourceManager.SetupBufferWithViews(_vertexBufferData, ref _vertexBufferWithViews);
            ResourceManager.SetupBufferWithViews(_indexBufferData, ref _indexBufferWithViews);

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

    /// <summary>
    /// Lays out the vertex rows along one box axis: the flat segments in the middle and, with a bevel, the rows
    /// that follow the bevel up to the 45° seam where the neighbouring face takes over.
    /// </summary>
    private void FillAxisStations(ref AxisStation[] stations, int segmentCount, float halfExtent)
    {
        segmentCount = segmentCount.Clamp(1, 10000);
        var hasBevel = _bevelWidth > 0;

        // A flat profile needs a second vertex row where the face meets the bevel to get a hard edge.
        var hasHardEdge = hasBevel && _bevelFlatness > 0;
        var stepsToSeam = (_bevelSides + 1) / 2;
        var bevelStationCount = hasBevel ? stepsToSeam + (hasHardEdge ? 1 : 0) : 0;

        var count = segmentCount + 1 + 2 * bevelStationCount;
        if (stations.Length != count)
            stations = new AxisStation[count];

        var innerHalfExtent = halfExtent - _bevelWidth;
        var index = 0;

        for (var bevelIndex = bevelStationCount - 1; bevelIndex >= 0; bevelIndex--)
        {
            stations[index++] = CreateBevelStation(bevelIndex, -1, innerHalfExtent, halfExtent, hasHardEdge);
        }

        for (var segmentIndex = 0; segmentIndex <= segmentCount; segmentIndex++)
        {
            var fraction = (float)segmentIndex / segmentCount;
            var inner = innerHalfExtent * (2 * fraction - 1);
            stations[index++] = new AxisStation
                                    {
                                        Inner = inner,
                                        Fraction = hasBevel ? 0.5f + inner / (2 * halfExtent) : fraction,
                                    };
        }

        for (var bevelIndex = 0; bevelIndex < bevelStationCount; bevelIndex++)
        {
            stations[index++] = CreateBevelStation(bevelIndex, 1, innerHalfExtent, halfExtent, hasHardEdge);
        }
    }

    private AxisStation CreateBevelStation(int bevelIndex, float side, float innerHalfExtent, float halfExtent, bool hasHardEdge)
    {
        var step = hasHardEdge ? bevelIndex : bevelIndex + 1;
        var isSeam = step == (_bevelSides + 1) / 2;
        var tan = isSeam ? 1f : MathF.Tan(step * MathF.PI / (2 * _bevelSides));
        var inner = side * innerHalfExtent;

        return new AxisStation
                   {
                       Inner = inner,
                       Tan = side * tan,
                       Side = side,
                       IsOddSeam = isSeam && _bevelSides % 2 == 1,
                       Fraction = 0.5f + (inner + side * tan * _bevelWidth) / (2 * halfExtent),
                   };
    }

    /// <summary>
    /// Pushes a face grid point out from the box shrunk by the bevel width. Positions and normals are in the
    /// face frame (column, row, depth).
    /// </summary>
    private void ComputeSurfacePoint(in AxisStation column, in AxisStation row, float depthInner, out Vector3 position, out Vector3 normal)
    {
        var columnTan = column.IsOddSeam ? column.Side * _oddSeamTan : column.Tan;
        var rowTan = row.IsOddSeam ? row.Side * _oddSeamTan : row.Tan;

        var offsetSum = ProjectOnProfile(new Vector3(columnTan, rowTan, 1), out var roundNormalSum);
        var sampleCount = 1;

        // With an odd number of sides the seam to the neighbouring face falls into the middle of a bevel segment,
        // so its vertex is the average of the segment's end points on both faces.
        if (column.IsOddSeam)
        {
            offsetSum += ProjectOnProfile(new Vector3(column.Side, rowTan, _oddSeamTan), out var roundNormal);
            roundNormalSum += roundNormal;
            sampleCount++;
        }

        if (row.IsOddSeam)
        {
            offsetSum += ProjectOnProfile(new Vector3(columnTan, row.Side, _oddSeamTan), out var roundNormal);
            roundNormalSum += roundNormal;
            sampleCount++;
        }

        position = new Vector3(column.Inner, row.Inner, depthInner) + offsetSum / sampleCount;

        var chamferNormal = Vector3.Normalize(new Vector3(column.Side, row.Side, 1));
        normal = Vector3.Normalize(Vector3.Lerp(Vector3.Normalize(roundNormalSum), chamferNormal, _bevelFlatness));
    }

    /** Offset from the shrunk box along a direction, landing on a sphere (round) or an octahedron (chamfer). */
    private Vector3 ProjectOnProfile(Vector3 cubeDirection, out Vector3 direction)
    {
        direction = Vector3.Normalize(cubeDirection);
        var chamferDistance = 1f / (MathF.Abs(direction.X) + MathF.Abs(direction.Y) + MathF.Abs(direction.Z));
        return direction * (_bevelWidth * MathUtils.Lerp(1f, chamferDistance, _bevelFlatness));
    }

    private AxisStation[] GetStationsForAxis(SegmentAxis axis)
    {
        return axis switch
                   {
                       SegmentAxis.X => _xStations,
                       SegmentAxis.Y => _yStations,
                       _             => _zStations
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

    /** One vertex row along a box axis. */
    private struct AxisStation
    {
        /** Coordinate clamped into the box shrunk by the bevel width. */
        public float Inner;

        /** Tangent of the bevel angle, ±1 on the seam to the neighbouring face. */
        public float Tan;

        /** Direction of the bevel the station belongs to, 0 on the flat part of a face. */
        public float Side;

        public bool IsOddSeam;

        /** Position across the face (0..1) used for texture coordinates. */
        public float Fraction;
    }

    private AxisStation[] _xStations = [];
    private AxisStation[] _yStations = [];
    private AxisStation[] _zStations = [];

    private float _bevelWidth;
    private int _bevelSides;
    private float _bevelFlatness;
    private float _oddSeamTan;

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

    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private BufferWithViews _vertexBufferWithViews;

    private Int3[] _indexBufferData = new Int3[0];
    private BufferWithViews _indexBufferWithViews;

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

    [Input(Guid = "dc13413f-e106-4cbe-81c7-dd260c9085cc")]
    public readonly InputSlot<float> BevelWidth = new();

    [Input(Guid = "85bb3f80-ea9d-4244-8289-4dedb6fa8fb0")]
    public readonly InputSlot<int> BevelSides = new();

    [Input(Guid = "aecf0016-95a8-43bb-9cee-d5f75356ae72")]
    public readonly InputSlot<float> BevelFlatness = new();

    [Input(Guid = "e1adb865-42f5-46a9-a4ac-bfae00b03caa", MappedType = typeof(UvModes))]
    public readonly InputSlot<int> TexCoord = new();

    [Input(Guid = "573b74e2-8ab9-4364-9de7-605248528cee")]
    public readonly InputSlot<float> Margin = new();

    [Input(Guid = "f946d7a1-8bb6-4ba1-a3bb-f2f95fc2242d", MappedType = typeof(UvModes))]
    public readonly InputSlot<int> TexCoord2 = new();
   
    [Input(Guid = "ba355fc0-0f1c-41d9-842e-d8ae9bee162a")]
    public readonly InputSlot<float> Margin2 = new();

}
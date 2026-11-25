using T3.Core.Rendering;
using T3.Core.Utils;

namespace Lib.mesh.generate;

[Guid("a835ab86-29c1-438e-a7f7-2e297108bfd5")]
internal sealed class TorusMesh : Instance<TorusMesh>
{
    [Output(Guid = "f8f17f87-56f2-4411-b9bf-b9193b9aa90d")]
    public readonly Slot<MeshBuffers> Data = new();

    public TorusMesh()
    {
        Data.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            var majorRadius = Radius.GetValue(context);
            var tubeRadius = Thickness.GetValue(context);

            var segments = Segments.GetValue(context);
            var ringSegmentCount = segments.Width.Clamp(1, 10000);
            var tubeSegmentCount = segments.Height.Clamp(1, 10000);

            var spin = Spin.GetValue(context);
            var fill = Fill.GetValue(context);
            var smoothAngle = SmoothAngle.GetValue(context);

            // The angle between the normals of two neighbouring faces equals the angle step of one segment,
            // so each direction can be tested on its own: a coarse ring gets hard edges while the tube stays round.
            var ringStepInDegrees = fill.X / ringSegmentCount;
            var tubeStepInDegrees = fill.Y / tubeSegmentCount;
            var flatAlongRing = MathF.Abs(ringStepInDegrees) > smoothAngle;
            var flatAlongTube = MathF.Abs(tubeStepInDegrees) > smoothAngle;

            var geometry = new TorusGeometry
                               {
                                   MajorRadius = majorRadius,
                                   TubeRadius = tubeRadius,
                                   RingStart = spin.X * MathUtils.ToRad,
                                   TubeStart = spin.Y * MathUtils.ToRad,
                                   RingStep = ringStepInDegrees * MathUtils.ToRad,
                                   TubeStep = tubeStepInDegrees * MathUtils.ToRad,
                                   RingSegmentCount = ringSegmentCount,
                                   TubeSegmentCount = tubeSegmentCount,
                                   NormalSign = tubeRadius < 0 ? -1f : 1f,
                               };

            if (flatAlongRing || flatAlongTube)
                BuildFacetedMesh(ref geometry, flatAlongRing, flatAlongTube);
            else
                BuildSmoothMesh(ref geometry);

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
            Log.Error("Failed to create torus mesh:" + e.Message);
        }
    }

    /// <summary>
    /// One shared vertex per grid point, so normals interpolate across all faces.
    /// </summary>
    private void BuildSmoothMesh(ref TorusGeometry g)
    {
        var columns = g.RingSegmentCount + 1;
        var rows = g.TubeSegmentCount + 1;
        EnsureBufferSizes(columns * rows, g.RingSegmentCount * g.TubeSegmentCount * 2);

        for (var tubeIndex = 0; tubeIndex < rows; ++tubeIndex)
        {
            var tubeAngle = g.TubeStart + tubeIndex * g.TubeStep;

            for (var ringIndex = 0; ringIndex < columns; ++ringIndex)
            {
                var ringAngle = g.RingStart + ringIndex * g.RingStep;
                var normal = TorusNormal(ringAngle, tubeAngle, 1f) * g.NormalSign;
                var vertexIndex = ringIndex + tubeIndex * columns;
                _vertexBufferData[vertexIndex] = CreateVertex(ref g, ringIndex, tubeIndex, ringAngle, tubeAngle, normal);

                if (tubeIndex >= g.TubeSegmentCount || ringIndex >= g.RingSegmentCount)
                    continue;

                var faceIndex = 2 * (ringIndex + tubeIndex * g.RingSegmentCount);
                _indexBufferData[faceIndex + 0] = new Int3(vertexIndex, vertexIndex + 1, vertexIndex + columns);
                _indexBufferData[faceIndex + 1] = new Int3(vertexIndex + columns, vertexIndex + 1, vertexIndex + columns + 1);
            }
        }
    }

    /// <summary>
    /// Four vertices per quad so normals can break at segment borders. In a flat direction every corner
    /// uses the normal at the quad's middle angle, which makes that direction show hard edges.
    /// </summary>
    private void BuildFacetedMesh(ref TorusGeometry g, bool flatAlongRing, bool flatAlongTube)
    {
        var quadCount = g.RingSegmentCount * g.TubeSegmentCount;
        EnsureBufferSizes(quadCount * 4, quadCount * 2);

        // A flat ring segment is a straight chord, which sits closer to the torus axis than the arc by
        // cos(halfStep). Scaling the normal's z by that factor keeps it perpendicular to the planar quad.
        var ringFlatteningFactor = flatAlongRing ? MathF.Cos(g.RingStep * 0.5f) : 1f;

        for (var tubeIndex = 0; tubeIndex < g.TubeSegmentCount; ++tubeIndex)
        {
            var tubeMidAngle = g.TubeStart + (tubeIndex + 0.5f) * g.TubeStep;

            for (var ringIndex = 0; ringIndex < g.RingSegmentCount; ++ringIndex)
            {
                var ringMidAngle = g.RingStart + (ringIndex + 0.5f) * g.RingStep;
                var quadIndex = ringIndex + tubeIndex * g.RingSegmentCount;
                var firstVertex = quadIndex * 4;

                // Corner order: (ring 0, tube 0), (ring 1, tube 0), (ring 0, tube 1), (ring 1, tube 1)
                for (var corner = 0; corner < 4; corner++)
                {
                    var cornerRingIndex = ringIndex + (corner & 1);
                    var cornerTubeIndex = tubeIndex + (corner >> 1);
                    var ringAngle = g.RingStart + cornerRingIndex * g.RingStep;
                    var tubeAngle = g.TubeStart + cornerTubeIndex * g.TubeStep;

                    var normal = TorusNormal(flatAlongRing ? ringMidAngle : ringAngle,
                                             flatAlongTube ? tubeMidAngle : tubeAngle,
                                             ringFlatteningFactor) * g.NormalSign;

                    _vertexBufferData[firstVertex + corner] = CreateVertex(ref g, cornerRingIndex, cornerTubeIndex, ringAngle, tubeAngle, normal);
                }

                _indexBufferData[quadIndex * 2 + 0] = new Int3(firstVertex + 0, firstVertex + 1, firstVertex + 2);
                _indexBufferData[quadIndex * 2 + 1] = new Int3(firstVertex + 2, firstVertex + 1, firstVertex + 3);
            }
        }
    }

    private static PbrVertex CreateVertex(ref TorusGeometry g, int ringIndex, int tubeIndex, float ringAngle, float tubeAngle, Vector3 normal)
    {
        var position = g.Position(ringAngle, tubeAngle);
        var uv = g.Uv(ringIndex, tubeIndex);

        // The tangent follows the ring direction; CalcTBNSpace derives it from the first edge only.
        var ringNeighbour = g.Position(ringAngle + g.RingStep, tubeAngle);
        var tubeNeighbour = g.Position(ringAngle, tubeAngle + g.TubeStep);
        MeshUtils.CalcTBNSpace(position, uv,
                               ringNeighbour, g.Uv(ringIndex + 1, tubeIndex),
                               tubeNeighbour, g.Uv(ringIndex, tubeIndex + 1),
                               normal, out var tangent, out var bitangent);

        return new PbrVertex
                   {
                       Position = position,
                       Normal = normal,
                       Tangent = tangent,
                       Bitangent = bitangent,
                       Texcoord = uv,
                       Selection = 1,
                       ColorRgb = Vector3.One,
                   };
    }

    /// <summary>
    /// Outward torus normal. <paramref name="zScale"/> is 1 for the curved surface.
    /// </summary>
    private static Vector3 TorusNormal(float ringAngle, float tubeAngle, float zScale)
    {
        var radial = MathF.Sin(tubeAngle);
        return Vector3.Normalize(new Vector3(MathF.Sin(ringAngle) * radial,
                                             MathF.Cos(ringAngle) * radial,
                                             MathF.Cos(tubeAngle) * zScale));
    }

    private void EnsureBufferSizes(int vertexCount, int faceCount)
    {
        if (_vertexBufferData.Length != vertexCount)
            _vertexBufferData = new PbrVertex[vertexCount];

        if (_indexBufferData.Length != faceCount)
            _indexBufferData = new Int3[faceCount];
    }

    private struct TorusGeometry
    {
        public float MajorRadius;
        public float TubeRadius;
        public float RingStart;
        public float TubeStart;
        public float RingStep;
        public float TubeStep;
        public int RingSegmentCount;
        public int TubeSegmentCount;
        public float NormalSign;

        public readonly Vector3 Position(float ringAngle, float tubeAngle)
        {
            var distanceFromAxis = MajorRadius + MathF.Sin(tubeAngle) * TubeRadius;
            return new Vector3(MathF.Sin(ringAngle) * distanceFromAxis,
                               MathF.Cos(ringAngle) * distanceFromAxis,
                               MathF.Cos(tubeAngle) * TubeRadius);
        }

        // V is offset by one row to keep the texture placement of existing projects.
        public readonly Vector2 Uv(int ringIndex, int tubeIndex)
        {
            return new Vector2(ringIndex / (float)RingSegmentCount,
                               (tubeIndex + 1) / (float)TubeSegmentCount);
        }
    }

    private PbrVertex[] _vertexBufferData = new PbrVertex[0];
    private BufferWithViews _vertexBufferWithViews;

    private Int3[] _indexBufferData = new Int3[0];
    private BufferWithViews _indexBufferWithViews;

    private readonly MeshBuffers _data = new();

    [Input(Guid = "608DE038-6C7A-43FC-BA89-374C7B1A318E")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "FDBAD44A-2504-453B-BFAE-976828372CC0")]
    public readonly InputSlot<float> Thickness = new();

    [Input(Guid = "99F5D952-8490-4930-B8AB-9D8E968183C6")]
    public readonly InputSlot<Int2> Segments = new();

    [Input(Guid = "770A164B-10E7-4145-B1C9-DAD1F564EC6B")]
    public readonly InputSlot<Vector2> Spin = new();

    [Input(Guid = "F3E7341C-0C81-42AF-BA48-B43D345188C1")]
    public readonly InputSlot<Vector2> Fill = new();

    // [Input(Guid = "1457EDC2-5F9B-4F72-9CB2-4CA40066F177")]
    // public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

    [Input(Guid = "2D083DC4-1576-4A44-8744-E0896424A6A9")]
    public readonly InputSlot<float> SmoothAngle = new();
}
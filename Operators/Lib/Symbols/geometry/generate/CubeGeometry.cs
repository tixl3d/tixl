namespace Lib.geometry.generate;

[Guid("85c19291-2bc1-4271-88f9-f1b50696da1b")]
internal sealed class CubeGeometry : Instance<CubeGeometry>
{
    [Output(Guid = "a3b04e1c-aa43-41fe-bf6a-1c21f6f2efd3")]
    public readonly Slot<Geometry> Geometry = new();

    public CubeGeometry()
    {
        Geometry.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var size = Size.GetValue(context);
        var half = size * UniformScale.GetValue(context) * 0.5f;
        var segments = Segments.GetValue(context);
        var segmentCounts = new Int3(Math.Clamp(segments.X, 1, MaxSegments), Math.Clamp(segments.Y, 1, MaxSegments), Math.Clamp(segments.Z, 1, MaxSegments));
        if (segmentCounts != _segmentCounts || _unitPositions.Length == 0)
        {
            _segmentCounts = segmentCounts;
            BuildTopology(segmentCounts);
        }

        var positions = _geometry.Positions;
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = _unitPositions[i] * half;
        }

        // The instance is reused, so bump its Version or caching consumers (e.g. async Bevel) keep stale results.
        _geometry.InvalidateTopologyCaches();
        Geometry.Value = _geometry;
    }

    /// <summary>
    /// Splits every face into a grid of quads. Points live on an integer lattice of the segment counts, so faces
    /// meeting at a cube edge share their points and the cube stays closed.
    /// </summary>
    private void BuildTopology(Int3 segments)
    {
        var counts = new[] { segments.X, segments.Y, segments.Z };
        _pointLookup.Clear();
        _points.Clear();

        var quadCount = 0;
        foreach (var (corners, _) in FaceDefinitions)
            quadCount += GetSteps(corners[0], corners[1], counts) * GetSteps(corners[0], corners[3], counts);

        _geometry.PrimitiveVertexOffsets = new int[quadCount + 1];
        _geometry.VertexPoints = new int[quadCount * 4];
        _geometry.SetAllPrimitiveKinds(PrimitiveKinds.Polygon);
        _geometry.Attributes.Clear();
        var normals = _geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Vertex, quadCount * 4).Values;
        var texCoords = _geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Vertex, quadCount * 4).Values;

        var quad = 0;
        foreach (var (corners, normal) in FaceDefinitions)
        {
            var origin = ToLattice(_unitCorners[corners[0]], counts);
            var alongU = ToLattice(_unitCorners[corners[1]], counts) - origin;
            var alongV = ToLattice(_unitCorners[corners[3]], counts) - origin;
            var stepsU = GetSteps(corners[0], corners[1], counts);
            var stepsV = GetSteps(corners[0], corners[3], counts);
            for (var v = 0; v < stepsV; v++)
            {
                for (var u = 0; u < stepsU; u++)
                {
                    _geometry.PrimitiveVertexOffsets[quad] = quad * 4;
                    for (var i = 0; i < 4; i++)
                    {
                        // Same corner order as the unsplit face: (0,0), (1,0), (1,1), (0,1) in grid steps
                        var gridU = u + (i is 1 or 2 ? 1 : 0);
                        var gridV = v + (i is 2 or 3 ? 1 : 0);
                        var lattice = origin + alongU * gridU / stepsU + alongV * gridV / stepsV;
                        var vertex = quad * 4 + i;
                        _geometry.VertexPoints[vertex] = GetPoint(lattice, counts);
                        normals[vertex] = normal;
                        texCoords[vertex] = new Vector2(1 - gridU / (float)stepsU, 1 - gridV / (float)stepsV);
                    }

                    quad++;
                }
            }
        }

        _geometry.PrimitiveVertexOffsets[quadCount] = quadCount * 4;
        _unitPositions = _points.ToArray();
        _geometry.Positions = new Vector3[_unitPositions.Length];
    }

    /** Grid steps between two face corners: the segment count of the axis they differ in. */
    private static int GetSteps(int cornerA, int cornerB, int[] counts)
    {
        var delta = _unitCorners[cornerB] - _unitCorners[cornerA];
        return MathF.Abs(delta.X) > 0 ? counts[0] : MathF.Abs(delta.Y) > 0 ? counts[1] : counts[2];
    }

    private static Int3 ToLattice(Vector3 unitCorner, int[] counts)
    {
        return new Int3(unitCorner.X > 0 ? counts[0] : 0, unitCorner.Y > 0 ? counts[1] : 0, unitCorner.Z > 0 ? counts[2] : 0);
    }

    private int GetPoint(Int3 lattice, int[] counts)
    {
        if (_pointLookup.TryGetValue(lattice, out var index))
            return index;

        index = _points.Count;
        _pointLookup[lattice] = index;
        _points.Add(new Vector3(lattice.X * 2f / counts[0] - 1, lattice.Y * 2f / counts[1] - 1, lattice.Z * 2f / counts[2] - 1));
        return index;
    }

    // Corner order per face is counter-clockwise seen from outside along the face normal.
    private static readonly (int[] Corners, Vector3 Normal)[] FaceDefinitions =
        [
            ([4, 5, 6, 7], new Vector3(0, 0, 1)),  // front  +Z
            ([1, 0, 3, 2], new Vector3(0, 0, -1)), // back   -Z
            ([5, 1, 2, 6], new Vector3(1, 0, 0)),  // right  +X
            ([0, 4, 7, 3], new Vector3(-1, 0, 0)), // left   -X
            ([7, 6, 2, 3], new Vector3(0, 1, 0)),  // top    +Y
            ([0, 1, 5, 4], new Vector3(0, -1, 0)), // bottom -Y
        ];

    private static readonly Vector3[] _unitCorners =
        [
            new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
            new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
        ];

    private static readonly Vector2[] _quadUvs = [new(1, 1), new(0, 1), new(0, 0), new(1, 0)];

    private const int MaxSegments = 256;

    private readonly Geometry _geometry = new();
    private readonly Dictionary<Int3, int> _pointLookup = new();
    private readonly List<Vector3> _points = [];
    private Vector3[] _unitPositions = [];
    private Int3 _segmentCounts;

    [Input(Guid = "d5c13c76-103e-4264-8056-88a33a9f0e99")]
    public readonly InputSlot<Vector3> Size = new();

    [Input(Guid = "e6b1a8f2-3c4d-4f7a-9b2e-5d8c1a6f3e47")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "51c56d70-2a5b-4525-b4d1-87c867c25ae2")]
    public readonly InputSlot<Int3> Segments = new();
}

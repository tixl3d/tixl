namespace Lib.geometry;

[Guid("85c19291-2bc1-4271-88f9-f1b50696da1b")]
internal sealed class CubeGeometry : Instance<CubeGeometry>
{
    [Output(Guid = "a3b04e1c-aa43-41fe-bf6a-1c21f6f2efd3")]
    public readonly Slot<MeshGeometry> Geometry = new();

    public CubeGeometry()
    {
        Geometry.UpdateAction = Update;
        BuildStaticTopology();
    }

    private void Update(EvaluationContext context)
    {
        var size = Size.GetValue(context);
        var half = size * 0.5f;

        var positions = _geometry.Positions;
        for (var i = 0; i < 8; i++)
        {
            positions[i] = _unitCorners[i] * half;
        }

        Geometry.Value = _geometry;
    }

    private void BuildStaticTopology()
    {
        _geometry.Positions = new Vector3[8];
        _geometry.FaceCornerOffsets = new int[FaceDefinitions.Length + 1];
        _geometry.CornerPointIndices = new int[FaceDefinitions.Length * 4];

        var normals = _geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, _geometry.CornerPointIndices.Length);
        var texCoords = _geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, _geometry.CornerPointIndices.Length);

        for (var faceIndex = 0; faceIndex < FaceDefinitions.Length; faceIndex++)
        {
            var (corners, normal) = FaceDefinitions[faceIndex];
            _geometry.FaceCornerOffsets[faceIndex] = faceIndex * 4;
            for (var i = 0; i < 4; i++)
            {
                var corner = faceIndex * 4 + i;
                _geometry.CornerPointIndices[corner] = corners[i];
                normals.Values[corner] = normal;
                texCoords.Values[corner] = _quadUvs[i];
            }
        }

        _geometry.FaceCornerOffsets[FaceDefinitions.Length] = FaceDefinitions.Length * 4;
        _geometry.InvalidateTopologyCaches();
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

    private static readonly Vector2[] _quadUvs = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)];

    private readonly MeshGeometry _geometry = new();

    [Input(Guid = "d5c13c76-103e-4264-8056-88a33a9f0e99")]
    public readonly InputSlot<Vector3> Size = new();
}

namespace Lib.geometry.generate;

/// <summary>
/// Generates a regular polygon as one closed, straight-segment curve in the XY plane. The first corner points along
/// +X before rotation, so three sides give a right-pointing triangle; Stretch scales after the rotation, which turns a
/// square rotated by 45° into a rectangle.
/// </summary>
[Guid("6dbc0a61-951c-4df1-b145-04df58aebca2")]
internal sealed class NGonCurves : Instance<NGonCurves>
{
    [Output(Guid = "7989acc2-66f4-4e55-84f2-326daa44182b")]
    public readonly Slot<Geometry> Result = new();

    public NGonCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var sides = Math.Clamp(Sides.GetValue(context), 3, MaxSides);
        var radius = Radius.GetValue(context);
        var rotation = Rotation.GetValue(context) * MathF.PI / 180f;
        var stretch = Stretch.GetValue(context);
        var center = Center.GetValue(context);

        if (_geometry.PointCount != sides)
            BuildTopology(sides);

        var positions = _geometry.Positions;
        for (var corner = 0; corner < sides; corner++)
        {
            var angle = rotation + corner * 2 * MathF.PI / sides;
            positions[corner] = center + new Vector3(MathF.Cos(angle) * radius * stretch.X, MathF.Sin(angle) * radius * stretch.Y, 0);
        }

        // The instance is reused, so bump its Version or caching consumers keep stale results.
        _geometry.InvalidateTopologyCaches();
        Result.Value = _geometry;
    }

    private void BuildTopology(int sides)
    {
        _geometry.Resize(sides, 1, sides);
        _geometry.PrimitiveVertexOffsets[0] = 0;
        _geometry.PrimitiveVertexOffsets[1] = sides;
        _geometry.PrimitiveKind[0] = PrimitiveKinds.Curve;
        _geometry.PrimitiveClosed[0] = true;
        for (var corner = 0; corner < sides; corner++)
            _geometry.VertexPoints[corner] = corner;

        _geometry.Parts = [];
        _geometry.Attributes.Clear();
    }

    private const int MaxSides = 1024;

    private readonly Geometry _geometry = new();

    [Input(Guid = "2596fff1-f31a-45ad-9798-fcbcb097a493")]
    public readonly InputSlot<int> Sides = new();

    [Input(Guid = "ba25033e-5e27-462b-9d76-b3f917a66032")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "13e778b9-95b6-424a-8bdf-e794caf8f7b7")]
    public readonly InputSlot<float> Rotation = new();

    [Input(Guid = "bce96ea9-fa9c-4e23-91ae-30202e0b7e07")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "a3105845-b595-423a-99bd-745bfdba0630")]
    public readonly InputSlot<Vector3> Center = new();
}

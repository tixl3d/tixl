using System;
using System.Collections.Generic;

namespace Lib.geometry;

/// <summary>
/// Writes a corner-domain Color attribute by sampling a ScalarField at each corner's
/// position: the field value is remapped from ValueRange to 0..1 and blends ColorA to
/// ColorB. Note: the built-in mesh shaders don't display vertex colors yet - the
/// attribute is consumed by downstream geometry ops and future draw paths.
/// </summary>
[Guid("d7e8318c-b5d2-4ca1-b228-bb70d6add820")]
internal sealed class ColorFromField : Instance<ColorFromField>
{
    [Output(Guid = "d86945c8-9889-4102-9632-e6dda2dc0e20")]
    public readonly Slot<MeshGeometry> Result = new();

    public ColorFromField()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var field = Field.GetValue(context);
        if (source == null || field == null)
        {
            Result.Value = source;
            return;
        }

        var colorA = ColorA.GetValue(context);
        var colorB = ColorB.GetValue(context);
        var range = ValueRange.GetValue(context);
        var rangeSpan = range.Y - range.X;
        var invSpan = MathF.Abs(rangeSpan) > 1e-10f ? 1f / rangeSpan : 0f;

        _output.Positions = source.Positions;
        _output.FaceCornerOffsets = source.FaceCornerOffsets;
        _output.CornerPointIndices = source.CornerPointIndices;
        _output.Parts = source.Parts;

        _output.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            if (!string.Equals(attribute.Name, GeometryAttributeNames.Color, StringComparison.OrdinalIgnoreCase))
                _sharedAttributes.Add(attribute);
        }

        foreach (var shared in _sharedAttributes)
        {
            _output.Attributes.Add(shared);
        }

        _sharedAttributes.Clear();

        var colors = _output.Attributes.GetOrCreate<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Corner, source.CornerCount);
        var positions = source.Positions;
        var cornerPoints = source.CornerPointIndices;
        var evaluate = field.Evaluate;
        for (var c = 0; c < source.CornerCount; c++)
        {
            var sample = new FieldSample(positions[cornerPoints[c]]);
            var t = Math.Clamp((evaluate(in sample) - range.X) * invSpan, 0f, 1f);
            colors.Values[c] = Vector4.Lerp(colorA, colorB, t);
        }

        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private readonly MeshGeometry _output = new();
    private readonly List<GeometryAttribute> _sharedAttributes = [];

    [Input(Guid = "7ff90229-3731-4a5f-adc3-2abab11d3d46")]
    public readonly InputSlot<MeshGeometry> Geometry = new();

    [Input(Guid = "5c50c45e-dbe4-4f2a-a1fa-863e3d8e2023")]
    public readonly InputSlot<ScalarField> Field = new();

    [Input(Guid = "aa992691-1a1c-4c52-9024-341812603d35")]
    public readonly InputSlot<Vector4> ColorA = new();

    [Input(Guid = "3f412276-cbc9-4b0d-a5a5-5a02ef1ac230")]
    public readonly InputSlot<Vector4> ColorB = new();

    [Input(Guid = "a42f59fa-1d30-4d68-a350-00300f9726c8")]
    public readonly InputSlot<Vector2> ValueRange = new();
}

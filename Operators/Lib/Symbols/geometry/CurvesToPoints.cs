using System;
using System.Collections.Generic;

namespace Lib.geometry;

/// <summary>
/// Flattens curves into a CPU point list, one polyline per contour separated by
/// separator points - the bridge from CurveGeometry to every existing line and
/// point op. A Color attribute on the part or contour domain lands in Point.Color;
/// F2 carries the part index.
/// </summary>
[Guid("6a1d8c3f-2b57-4e90-a4f6-c8e2d5b7f019")]
internal sealed class CurvesToPoints : Instance<CurvesToPoints>
{
    [Output(Guid = "e3c5a7f1-9d24-4b68-8f0e-a1b3c6d9e274")]
    public readonly Slot<StructuredList> Points = new();

    public CurvesToPoints()
    {
        Points.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var curves = Curves.GetValue(context);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-5f);
        var closeLoops = CloseLoops.GetValue(context);

        if (curves == null || curves.ContourCount == 0)
        {
            Points.Value = null;
            return;
        }

        curves.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Part, out var partColors);
        curves.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Contour, out var contourColors);
        var parts = curves.Parts;

        _positions.Clear();
        _pointCount = 0;
        var partIndex = 0;
        for (var contourIndex = 0; contourIndex < curves.ContourCount; contourIndex++)
        {
            while (partIndex < parts.Length - 1 && contourIndex >= parts[partIndex].ContourStart + parts[partIndex].ContourCount)
                partIndex++;

            _positions.Clear();
            curves.Flatten(contourIndex, tolerance, _positions);
            if (_positions.Count == 0)
                continue;

            var color = Vector4.One;
            if (contourColors != null)
                color = contourColors.Values[contourIndex];
            else if (partColors != null && parts.Length > 0)
                color = partColors.Values[partIndex];

            var f2 = parts.Length > 0 ? partIndex : contourIndex;
            var closed = closeLoops && curves.ContourClosed[contourIndex];
            var count = _positions.Count + (closed ? 1 : 0);
            EnsureCapacity(_pointCount + count + 1);
            for (var i = 0; i < count; i++)
            {
                var position = _positions[i % _positions.Count];
                _pointList.TypedElements[_pointCount++] = new Point
                                                              {
                                                                  Position = position,
                                                                  F1 = 1,
                                                                  F2 = f2,
                                                                  Orientation = Quaternion.Identity,
                                                                  Scale = Vector3.One,
                                                                  Color = color,
                                                              };
            }

            _pointList.TypedElements[_pointCount++] = Point.Separator();
        }

        if (_pointList.NumElements != _pointCount)
            _pointList.SetLength(_pointCount);

        Points.Value = _pointList;
    }

    private void EnsureCapacity(int count)
    {
        if (_pointList.NumElements < count)
            _pointList.SetLength(Math.Max(count, _pointList.NumElements * 2));
    }

    private readonly StructuredList<Point> _pointList = new(1);
    private readonly List<Vector3> _positions = [];
    private int _pointCount;

    [Input(Guid = "b7f2d9c4-5a13-4e86-9d0b-3c6e8a1f5d27")]
    public readonly InputSlot<CurveGeometry> Curves = new();

    [Input(Guid = "4e9a6c1b-d872-4f35-b0e8-7a2d5f9c3e61")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "d2c8e5a3-1f64-4b97-8e3a-c5b0d7f4a916")]
    public readonly InputSlot<bool> CloseLoops = new();
}

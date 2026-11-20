using System;
using System.Collections.Generic;
using Lib.Utils;

namespace Lib.geometry.convert;

/// <summary>
/// Flattens curves into a CPU point list, one polyline per contour separated by
/// separator points - the bridge from CurveGeometry to every existing line and
/// point op. A Color attribute on the part or contour domain lands in Point.Color;
/// F2 carries the part index. Each point's +Z axis faces away from the filled area in the
/// XY plane, +Y is the plane's normal and +X runs along the contour.
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
        CurveWinding.GetOutsideSides(curves, _contourSides);

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
            var isContourClosed = curves.ContourClosed[contourIndex];
            var closed = closeLoops && isContourClosed;
            var count = _positions.Count + (closed ? 1 : 0);
            EnsureCapacity(_pointCount + count + 1);
            for (var i = 0; i < count; i++)
            {
                var index = i % _positions.Count;
                var tangent = GetTangent(_positions, index, isContourClosed);
                _pointList.TypedElements[_pointCount++] = new Point
                                                              {
                                                                  Position = _positions[index],
                                                                  F1 = 1,
                                                                  F2 = f2,
                                                                  Orientation = GetOutwardOrientation(tangent, _contourSides[contourIndex]),
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

    /// <summary>Average of the unit directions of both adjacent edges, so corners get the bisecting normal.</summary>
    private static Vector3 GetTangent(List<Vector3> polyline, int index, bool closed)
    {
        var count = polyline.Count;
        var previous = closed ? (index - 1 + count) % count : Math.Max(index - 1, 0);
        var next = closed ? (index + 1) % count : Math.Min(index + 1, count - 1);
        var incoming = SafeNormalize(polyline[index] - polyline[previous]);
        var outgoing = SafeNormalize(polyline[next] - polyline[index]);
        var tangent = incoming + outgoing;
        return tangent.LengthSquared() > 1e-12f ? tangent : incoming;
    }

    /// <param name="outsideSide">+1 when the outside lies left of the tangent, -1 when right.</param>
    private static Quaternion GetOutwardOrientation(Vector3 tangent, float outsideSide)
    {
        var outward = new Vector3(-tangent.Y, tangent.X, 0) * outsideSide;
        var length = outward.Length();
        if (length < 1e-6f)
            return Quaternion.Identity;

        var forward = outward / length;
        var up = Vector3.UnitZ;
        var right = Vector3.Cross(up, forward);
        var basis = new Matrix4x4(right.X, right.Y, right.Z, 0,
                                  up.X, up.Y, up.Z, 0,
                                  forward.X, forward.Y, forward.Z, 0,
                                  0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(basis);
    }

    private static Vector3 SafeNormalize(Vector3 v)
    {
        var length = v.Length();
        return length > 1e-9f ? v / length : Vector3.Zero;
    }

    private void EnsureCapacity(int count)
    {
        if (_pointList.NumElements < count)
            _pointList.SetLength(Math.Max(count, _pointList.NumElements * 2));
    }

    private readonly StructuredList<Point> _pointList = new(1);
    private readonly List<Vector3> _positions = [];
    private readonly List<float> _contourSides = [];
    private int _pointCount;

    [Input(Guid = "b7f2d9c4-5a13-4e86-9d0b-3c6e8a1f5d27")]
    public readonly InputSlot<CurveGeometry> Curves = new();

    [Input(Guid = "4e9a6c1b-d872-4f35-b0e8-7a2d5f9c3e61")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "d2c8e5a3-1f64-4b97-8e3a-c5b0d7f4a916")]
    public readonly InputSlot<bool> CloseLoops = new();
}

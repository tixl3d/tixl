#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>Tells the outside of closed curves in the XY plane from their winding, per part.</summary>
internal static class CurveWinding
{
    /// <summary>
    /// Fills one sign per contour: +1 when the outside lies to the left of the contour's direction,
    /// -1 when it lies to the right. Decided per part from the summed area, so holes get the
    /// opposite side of their outline whatever winding the source uses. Open contours follow their part.
    /// </summary>
    public static void GetOutsideSides(CurveGeometry source, List<float> sides)
    {
        var contourCount = source.ContourCount;
        sides.Clear();
        var totalArea = 0f;
        for (var contourIndex = 0; contourIndex < contourCount; contourIndex++)
        {
            totalArea += GetSignedArea(source, contourIndex);
        }

        var defaultSide = SideForArea(totalArea);
        for (var contourIndex = 0; contourIndex < contourCount; contourIndex++)
        {
            sides.Add(defaultSide);
        }

        foreach (var part in source.Parts)
        {
            var start = Math.Max(part.ContourStart, 0);
            var end = Math.Min(part.ContourStart + part.ContourCount, contourCount);
            var partArea = 0f;
            for (var contourIndex = start; contourIndex < end; contourIndex++)
            {
                partArea += GetSignedArea(source, contourIndex);
            }

            var side = SideForArea(partArea);
            for (var contourIndex = start; contourIndex < end; contourIndex++)
            {
                sides[contourIndex] = side;
            }
        }
    }

    /// <summary>Shoelace area of the closed control polygon: positive for counter-clockwise, 0 for open contours.</summary>
    public static float GetSignedArea(CurveGeometry source, int contourIndex)
    {
        if (!source.ContourClosed[contourIndex])
            return 0;

        var start = source.ContourOffsets[contourIndex];
        var count = source.GetContourPointCount(contourIndex);
        var area = 0f;
        for (var i = 0; i < count; i++)
        {
            var a = start + i;
            var b = start + (i + 1) % count;
            area += Cross(source.Positions[a], source.HandlesOut[a])
                    + Cross(source.HandlesOut[a], source.HandlesIn[b])
                    + Cross(source.HandlesIn[b], source.Positions[b]);
        }

        return area * 0.5f;
    }

    /// <summary>
    /// The <see cref="Geometry"/> form of <see cref="GetOutsideSides(CurveGeometry, List{float})"/>: one sign per
    /// primitive, decided per part; polygons don't count towards the area and get the part's side.
    /// </summary>
    public static void GetOutsideSides(Geometry source, List<float> sides)
    {
        var primitiveCount = source.PrimitiveCount;
        var curves = source.Curves;
        sides.Clear();
        var totalArea = 0f;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
            totalArea += GetSignedArea(source, curves, primitive);

        var defaultSide = SideForArea(totalArea);
        for (var primitive = 0; primitive < primitiveCount; primitive++)
            sides.Add(defaultSide);

        foreach (var part in source.Parts)
        {
            var start = Math.Max(part.PrimitiveStart, 0);
            var end = Math.Min(part.PrimitiveStart + part.PrimitiveCount, primitiveCount);
            var partArea = 0f;
            for (var primitive = start; primitive < end; primitive++)
                partArea += GetSignedArea(source, curves, primitive);

            var side = SideForArea(partArea);
            for (var primitive = start; primitive < end; primitive++)
                sides[primitive] = side;
        }
    }

    /// <summary>Shoelace area of a closed curve's control polygon: positive for counter-clockwise, 0 for open curves and polygons.</summary>
    public static float GetSignedArea(Geometry source, Geometry.CurveEnumerable curves, int primitive)
    {
        if (source.PrimitiveKind[primitive] != PrimitiveKinds.Curve || !source.PrimitiveClosed[primitive])
            return 0;

        var curve = curves[primitive];
        var count = curve.VertexCount;
        var area = 0f;
        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            area += Cross(curve.GetPosition(i), curve.GetHandleOutPosition(i))
                    + Cross(curve.GetHandleOutPosition(i), curve.GetHandleInPosition(next))
                    + Cross(curve.GetHandleInPosition(next), curve.GetPosition(next));
        }

        return area * 0.5f;
    }

    /** Counter-clockwise outlines have their outside on the right. */
    private static float SideForArea(float area) => area > 0 ? -1f : 1f;

    private static float Cross(Vector3 a, Vector3 b) => a.X * b.Y - a.Y * b.X;
}

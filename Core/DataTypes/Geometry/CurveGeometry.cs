#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Utils.Splines;

namespace T3.Core.DataTypes;

/// <summary>
/// CPU-side curve set: contours of cubic bezier segments over shared anchors, with
/// typed attributes per domain and an optional part table (e.g. one part per glyph).
/// Anchor i of a contour connects to anchor i+1 through HandleOut[i] and HandleIn[i+1];
/// closed contours add the segment from the last anchor back to the first. A polyline
/// is the degenerate case where every handle sits on its anchor.
///
/// Like <see cref="MeshGeometry"/>, instances flow by reference: ops build into their
/// own reused output and never mutate their inputs.
/// </summary>
public sealed class CurveGeometry
{
    /// <summary>Anchor positions; the ControlPoint domain's element count.</summary>
    public Vector3[] Positions = [];

    /// <summary>Absolute position of the incoming handle per anchor (== Positions[i] for a corner).</summary>
    public Vector3[] HandlesIn = [];

    /// <summary>Absolute position of the outgoing handle per anchor.</summary>
    public Vector3[] HandlesOut = [];

    /// <summary>Per contour: start anchor index. Length ContourCount + 1 (last entry = anchor count).</summary>
    public int[] ContourOffsets = [0];

    /// <summary>Per contour: whether the last anchor connects back to the first.</summary>
    public bool[] ContourClosed = [];

    /// <summary>
    /// Contiguous contour ranges forming semantic pieces (glyphs, paths, ...). Empty means
    /// one implicit part covering all contours.
    /// </summary>
    public CurvePart[] Parts = [];

    public GeometryAttributes Attributes { get; } = new();

    public int PointCount => Positions.Length;
    public int ContourCount => ContourOffsets.Length - 1;

    public int GetContourPointCount(int contourIndex) => ContourOffsets[contourIndex + 1] - ContourOffsets[contourIndex];

    /// <summary>Segments of a contour: one less than its anchors, or as many when closed.</summary>
    public int GetContourSegmentCount(int contourIndex)
    {
        var points = GetContourPointCount(contourIndex);
        if (points < 2)
            return 0;

        return ContourClosed[contourIndex] ? points : points - 1;
    }

    /// <summary>Total segment count; the Segment domain's element count.</summary>
    public int GetSegmentCount()
    {
        var count = 0;
        for (var contourIndex = 0; contourIndex < ContourCount; contourIndex++)
        {
            count += GetContourSegmentCount(contourIndex);
        }

        return count;
    }

    /// <summary>Position on a contour's segment at t in 0..1.</summary>
    public Vector3 Evaluate(int contourIndex, int segmentIndex, float t)
    {
        GetSegmentAnchors(contourIndex, segmentIndex, out var a, out var b);
        return Bezier.GetPoint(Positions[a], HandlesOut[a], HandlesIn[b], Positions[b], t);
    }

    /// <summary>Tangent (first derivative) on a contour's segment at t in 0..1.</summary>
    public Vector3 EvaluateTangent(int contourIndex, int segmentIndex, float t)
    {
        GetSegmentAnchors(contourIndex, segmentIndex, out var a, out var b);
        return Bezier.GetFirstDerivative(Positions[a], HandlesOut[a], HandlesIn[b], Positions[b], t);
    }

    /// <summary>
    /// Appends a polyline approximation of a contour: segments are subdivided until the
    /// chord deviates less than <paramref name="tolerance"/> (straight segments stay one
    /// line). The first anchor is included; the closing point of a closed contour is not,
    /// so the result can be drawn as a loop without a duplicate.
    /// </summary>
    public void Flatten(int contourIndex, float tolerance, List<Vector3> target)
    {
        var start = ContourOffsets[contourIndex];
        var pointCount = GetContourPointCount(contourIndex);
        if (pointCount == 0)
            return;

        target.Add(Positions[start]);
        var segmentCount = GetContourSegmentCount(contourIndex);
        for (var segment = 0; segment < segmentCount; segment++)
        {
            GetSegmentAnchors(contourIndex, segment, out var a, out var b);
            var isLast = segment == segmentCount - 1;
            BezierFlattening.FlattenSegment(Positions[a], HandlesOut[a], HandlesIn[b], Positions[b], tolerance, target,
                           includeEnd: !(isLast && ContourClosed[contourIndex]));
        }
    }

    public void InvalidateCaches()
    {
        Version++;
    }

    /// <summary>Bumped on every <see cref="InvalidateCaches"/>; reused instances can't signal change by identity.</summary>
    public int Version { get; private set; }

    private void GetSegmentAnchors(int contourIndex, int segmentIndex, out int a, out int b)
    {
        var start = ContourOffsets[contourIndex];
        var pointCount = GetContourPointCount(contourIndex);
        a = start + segmentIndex;
        b = start + (segmentIndex + 1) % pointCount;
    }
}

/// <summary>A semantic piece of a <see cref="CurveGeometry"/>: a contiguous contour range with placement metadata.</summary>
/// <param name="SeedIndex">Index of the generating element (e.g. the character index) - lets consumers map parts back to their source.</param>
public readonly record struct CurvePart(int ContourStart, int ContourCount, Vector3 Pivot, int Id, int SeedIndex);

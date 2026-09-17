#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;
using T3.Core.Utils;

namespace Lib.geometry;

/// <summary>
/// Subdivides a CurveGeometry by splitting cubics whose approximated length exceeds
/// MaxEdgeLength into shorter sub-cubics. The split is an exact De Casteljau
/// subdivision, so the curve shape is preserved; only the anchor/segment density
/// changes. When EvenSpacing is on, sub-segments are equal in arc length rather
/// than equal in parameter.
/// </summary>
[Guid("f7a2c9e1-4b63-4d08-9e5a-2c7f8b1d6a34")]
[ExportDependencies("WaterTrans.GlyphLoader.dll")]
internal sealed class SubdivideCurves : Instance<SubdivideCurves>
{
    [Output(Guid = "3e8b5f2a-9c14-4d67-a0e3-7b1f6c9a2e85")]
    public readonly Slot<CurveGeometry?> Result = new();

    public SubdivideCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var curves = Curves.GetValue(context);
        var maxEdgeLength = MathF.Max(MaxEdgeLength.GetValue(context), 0f);
        var evenSpacing = EvenSpacing.GetValue(context);

        if (curves == null || maxEdgeLength <= 0f || curves.PointCount == 0)
        {
            // Nothing to subdivide; pass the input through unchanged.
            Result.Value = curves;
            return;
        }

        SubdivideInto(curves, maxEdgeLength, evenSpacing, _output);
        Result.Value = _output;
    }

    /// <summary>
    /// Rewrites <paramref name="source"/>'s anchors, handles, contour offsets and closed
    /// flags into <paramref name="target"/>, inserting intermediate anchors wherever a
    /// segment is longer than <paramref name="maxEdgeLength"/>. Parts are copied across
    /// with their ContourStart remapped to the new offsets. Attributes on the source are
    /// not carried over, because the point/segment element counts change.
    /// </summary>
    private static void SubdivideInto(CurveGeometry source, float maxEdgeLength, bool evenSpacing, CurveGeometry target)
    {
        var positions = source.Positions;
        var handlesIn = source.HandlesIn;
        var handlesOut = source.HandlesOut;
        var contourOffsets = source.ContourOffsets;
        var contourClosed = source.ContourClosed;

        var newPositions = new List<Vector3>();
        var newHandlesIn = new List<Vector3>();
        var newHandlesOut = new List<Vector3>();
        var newContourOffsets = new List<int> { 0 };
        var newContourClosed = new List<bool>();

        // Per-anchor handles may be rewritten by the segments that touch them
        // (a shared anchor's outgoing/incoming handle is altered by the split
        // of the segment that ends/starts at it). Copy first, then mutate.
        var updatedHOut = (Vector3[])handlesOut.Clone();
        var updatedHIn = (Vector3[])handlesIn.Clone();

        var contourCount = contourOffsets.Length - 1;
        var oldToNew = new int[contourCount];

        for (var c = 0; c < contourCount; c++)
        {
            oldToNew[c] = newContourOffsets.Count - 1;

            var start = contourOffsets[c];
            var end = contourOffsets[c + 1];
            var count = end - start;
            var closed = contourClosed[c];

            // Degenerate contour
            if (count < 2)
            {
                for (var i = start; i < end; i++)
                {
                    newPositions.Add(positions[i]);
                    newHandlesIn.Add(handlesIn[i]);
                    newHandlesOut.Add(handlesOut[i]);
                }
                newContourOffsets.Add(newPositions.Count);
                newContourClosed.Add(closed);
                continue;
            }

            // For each original segment decide whether to split and remember the
            // intermediate anchors to insert after anchor `start + i`.
            var intermediates = new Dictionary<int, List<(Vector3 Pos, Vector3 HIn, Vector3 HOut)>>();
            var segmentCount = closed ? count : count - 1;

            for (var i = 0; i < segmentCount; i++)
            {
                var aIdx = start + i;
                var bIdx = (i + 1 < count) ? start + i + 1 : start;

                var p0 = positions[aIdx];
                var p1 = handlesOut[aIdx];
                var p2 = handlesIn[bIdx];
                var p3 = positions[bIdx];

                var len = ApproximateCubicLength(p0, p1, p2, p3);
                if (len <= maxEdgeLength)
                    continue;

                var n = Math.Max(1, (int)Math.Ceiling(len / maxEdgeLength));
                if (n == 1)
                    continue;

                var sub = evenSpacing
                              ? SplitCubicNEvenSpacing(p0, p1, p2, p3, n)
                              : SplitCubicN(p0, p1, p2, p3, n);

                // Rewrite the two shared handles of this segment
                updatedHOut[aIdx] = sub[0].P1;
                updatedHIn[bIdx] = sub[n - 1].P2;

                // Every sub-segment except the last contributes one intermediate anchor
                var list = new List<(Vector3, Vector3, Vector3)>(n - 1);
                for (var k = 0; k < n - 1; k++)
                {
                    list.Add((
                        sub[k].P3,        // position (== sub[k+1].P0)
                        sub[k].P2,        // incoming handle
                        sub[k + 1].P1));  // outgoing handle
                }
                intermediates[aIdx] = list;
            }

            // Emit anchors + their trailing intermediates
            for (var i = 0; i < count; i++)
            {
                var idx = start + i;
                newPositions.Add(positions[idx]);
                newHandlesIn.Add(updatedHIn[idx]);
                newHandlesOut.Add(updatedHOut[idx]);

                if (intermediates.TryGetValue(idx, out var list))
                {
                    foreach (var (p, hi, ho) in list)
                    {
                        newPositions.Add(p);
                        newHandlesIn.Add(hi);
                        newHandlesOut.Add(ho);
                    }
                }
            }

            newContourOffsets.Add(newPositions.Count);
            newContourClosed.Add(closed);
        }

        target.Positions = newPositions.ToArray();
        target.HandlesIn = newHandlesIn.ToArray();
        target.HandlesOut = newHandlesOut.ToArray();
        target.ContourOffsets = newContourOffsets.ToArray();
        target.ContourClosed = newContourClosed.ToArray();

        // Remap Parts' ContourStart to the new offsets. Contours are neither
        // removed nor reordered here, but we route through oldToNew to stay
        // correct if that ever changes (e.g. degenerate-contour culling).
        var srcParts = source.Parts;
        if (srcParts.Length > 0)
        {
            var newParts = new CurvePart[srcParts.Length];
            for (var i = 0; i < srcParts.Length; i++)
            {
                var part = srcParts[i];
                var newStart = part.ContourStart >= 0 && part.ContourStart < oldToNew.Length
                                   ? oldToNew[part.ContourStart]
                                   : part.ContourStart;
                newParts[i] = new CurvePart(newStart, part.ContourCount, part.Pivot, part.Id, part.SeedIndex);
            }
            target.Parts = newParts;
        }
        else
        {
            target.Parts = [];
        }

        target.InvalidateCaches();
    }

    /// <summary>Rough length of a cubic from its control polygon and chord.</summary>
    private static float ApproximateCubicLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        var polyLen = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p3);
        var chordLen = Vector3.Distance(p0, p3);
        return (polyLen + chordLen) * 0.5f;
    }

    /// <summary>De Casteljau split of one cubic at parameter t.</summary>
    private static ((Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3) L,
                    (Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3) R)
        SplitCubicAt((Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3) c, float t)
    {
        var q0 = Vector3.Lerp(c.P0, c.P1, t);
        var q1 = Vector3.Lerp(c.P1, c.P2, t);
        var q2 = Vector3.Lerp(c.P2, c.P3, t);
        var r0 = Vector3.Lerp(q0, q1, t);
        var r1 = Vector3.Lerp(q1, q2, t);
        var s = Vector3.Lerp(r0, r1, t);
        return ((c.P0, q0, r0, s), (s, r1, q2, c.P3));
    }

    /// <summary>Splits a cubic into n equal-parameter sub-cubics (exact, shape-preserving).</summary>
    private static List<(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3)>
        SplitCubicN(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int n)
    {
        var result = new List<(Vector3, Vector3, Vector3, Vector3)>(n);
        var current = (p0, p1, p2, p3);
        for (var k = 0; k < n; k++)
        {
            var remaining = n - k;
            if (remaining == 1)
            {
                result.Add(current);
                break;
            }

            var (left, right) = SplitCubicAt(current, 1f / remaining);
            result.Add(left);
            current = right;
        }
        return result;
    }

    private static Vector3 CubicPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var u = 1f - t;
        var uu = u * u;
        var tt = t * t;
        return uu * u * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + tt * t * p3;
    }

    /// <summary>
    /// Splits a cubic into n sub-cubics whose arc-lengths are equal. A small
    /// arc-length LUT maps target lengths back to t values, then De Casteljau
    /// splits at those t's so the original shape is exactly preserved.
    /// </summary>
    private static List<(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3)>
        SplitCubicNEvenSpacing(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int n)
    {
        const int lutSize = 64;
        var lutT = new float[lutSize + 1];
        var lutLen = new float[lutSize + 1];
        var prev = p0;
        lutLen[0] = 0f;
        for (var i = 1; i <= lutSize; i++)
        {
            var t = i / (float)lutSize;
            var pt = CubicPoint(p0, p1, p2, p3, t);
            lutLen[i] = lutLen[i - 1] + Vector3.Distance(prev, pt);
            lutT[i] = t;
            prev = pt;
        }

        var totalLen = lutLen[lutSize];
        if (totalLen <= 1e-9f)
            return SplitCubicN(p0, p1, p2, p3, n);

        float TForLen(float target)
        {
            int lo = 0, hi = lutSize;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (lutLen[mid] < target) lo = mid; else hi = mid;
            }
            var segLen = lutLen[hi] - lutLen[lo];
            var frac = segLen > 0f ? (target - lutLen[lo]) / segLen : 0f;
            return lutT[lo] + frac * (lutT[hi] - lutT[lo]);
        }

        var tValues = new float[n + 1];
        tValues[0] = 0f;
        tValues[n] = 1f;
        for (var k = 1; k < n; k++)
            tValues[k] = TForLen(totalLen * k / n);

        // Sequentially split: each SplitCubicAt is applied to the *remaining* right part,
        // so the local t is remapped from the original interval [remainingT0, 1].
        var result = new List<(Vector3, Vector3, Vector3, Vector3)>(n);
        var remaining = (P0: p0, P1: p1, P2: p2, P3: p3);
        var remainingT0 = 0f;
        for (var k = 0; k < n; k++)
        {
            if (k == n - 1)
            {
                result.Add(remaining);
                break;
            }

            var tEnd = tValues[k + 1];
            var localT = (tEnd - remainingT0) / (1f - remainingT0);
            if (localT <= 0f) localT = 1e-7f;
            if (localT >= 1f) localT = 1f - 1e-7f;

            var (left, right) = SplitCubicAt(remaining, localT);
            result.Add(left);
            remaining = right;
            remainingT0 = tEnd;
        }
        return result;
    }

    [Input(Guid = "4a7e2c9f-6d15-4b83-a0e6-9c3f8b1d5e27")]
    public readonly InputSlot<CurveGeometry> Curves = new();

    [Input(Guid = "85c26820-f36b-4f44-9e78-4b63be33a65d")]
    public readonly InputSlot<float> MaxEdgeLength = new();

    [Input(Guid = "b8d3f5a2-9e41-4c76-8a0b-5d2f7c9e1a34")]
    public readonly InputSlot<bool> EvenSpacing = new();

    private readonly CurveGeometry _output = new();
}
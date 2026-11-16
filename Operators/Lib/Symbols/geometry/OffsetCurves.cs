#nullable enable
using Lib.Utils;
using T3.Core.Utils;
using T3.Core.Utils.Splines;

namespace Lib.geometry;

/// <summary>
/// Offsets every contour of a CurveGeometry sideways in the XY plane by a fixed distance.
/// Each cubic is offset by moving its anchors along the normal and scaling its handles by
/// the curvature there, split until it stays within Tolerance of the exact offset. Corners
/// are joined on the outside (miter, round or bevel) and trimmed to the intersection on the
/// inside, so closed contours stay closed and sharp corners stay sharp.
/// </summary>
[Guid("89ddc35c-ba3b-49a1-b766-a7356a12fe40")]
internal sealed class OffsetCurves : Instance<OffsetCurves>
{
    [Output(Guid = "eb51b623-b0bc-4686-986b-0148262ced85")]
    public readonly Slot<CurveGeometry?> Result = new();

    public OffsetCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var curves = Curves.GetValue(context);
        var offset = Offset.GetValue(context);
        var join = (Joins)Join.GetValue(context).Clamp(0, 2);
        var miterLimit = MathF.Max(MiterLimit.GetValue(context), 1f);
        var tolerance = MathF.Max(Tolerance.GetValue(context), 1e-6f);

        if (curves == null || curves.PointCount == 0 || offset == 0)
        {
            Result.Value = curves;
            return;
        }

        _builder.Build(curves, offset, join, miterLimit, tolerance, _output);
        Result.Value = _output;
    }

    private enum Joins
    {
        Miter,
        Round,
        Bevel,
    }

    /// <summary>A cubic bezier by its four control points.</summary>
    private readonly record struct Cubic(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3);

    /// <summary>One non-degenerate source segment and the range of offset pieces it produced.</summary>
    private readonly record struct SegmentInfo(int PieceStart, int PieceCount, Vector3 TangentStart, Vector3 TangentEnd, Vector3 EndAnchor);

    private sealed class OffsetBuilder
    {
        public void Build(CurveGeometry source, float offset, Joins join, float miterLimit, float tolerance, CurveGeometry target)
        {
            _positions.Clear();
            _handlesIn.Clear();
            _handlesOut.Clear();
            _contourOffsets.Clear();
            _contourOffsets.Add(0);

            _join = join;
            _miterLimit = miterLimit;
            _tolerance = tolerance;

            CurveWinding.GetOutsideSides(source, _contourSides);

            var contourCount = source.ContourCount;
            for (var contourIndex = 0; contourIndex < contourCount; contourIndex++)
            {
                OffsetContour(source, contourIndex, offset * _contourSides[contourIndex]);
                _contourOffsets.Add(_positions.Count);
            }

            target.Positions = _positions.ToArray();
            target.HandlesIn = _handlesIn.ToArray();
            target.HandlesOut = _handlesOut.ToArray();
            target.ContourOffsets = _contourOffsets.ToArray();
            target.ContourClosed = (bool[])source.ContourClosed.Clone();
            target.Parts = source.Parts;

            // Contours and parts keep their count and order, so their attributes stay valid and are shared
            target.Attributes.Clear();
            foreach (var attribute in source.Attributes)
            {
                if (attribute.Domain is AttributeDomain.Part or AttributeDomain.Contour)
                    target.Attributes.Add(attribute);
            }

            target.InvalidateCaches();
        }

        /// <param name="distance">Signed distance along the left normal of the contour's direction.</param>
        private void OffsetContour(CurveGeometry source, int contourIndex, float distance)
        {
            var start = source.ContourOffsets[contourIndex];
            var pointCount = source.GetContourPointCount(contourIndex);
            var segmentCount = source.GetContourSegmentCount(contourIndex);
            var closed = source.ContourClosed[contourIndex];

            _pieces.Clear();
            _segments.Clear();
            for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                var a = start + segmentIndex;
                var b = start + (segmentIndex + 1) % pointCount;
                var cubic = new Cubic(source.Positions[a], source.HandlesOut[a], source.HandlesIn[b], source.Positions[b]);
                if (!TryGetTangents(cubic, out var tangentStart, out var tangentEnd))
                    continue; // zero-length segment: its neighbors join directly

                var pieceStart = _pieces.Count;
                OffsetCubic(cubic, distance, depth: 0);
                _segments.Add(new SegmentInfo(pieceStart, _pieces.Count - pieceStart, tangentStart, tangentEnd, cubic.P3));
            }

            _contourStart = _positions.Count;
            var contourStart = _contourStart;
            if (_segments.Count == 0)
            {
                // Nothing with a direction to offset; keep the anchors so contour and part ranges stay valid
                for (var i = start; i < start + pointCount; i++)
                {
                    AddAnchor(source.Positions[i], source.HandlesIn[i], source.HandlesOut[i]);
                }

                return;
            }

            Vector3? trimmedStart = null;
            for (var segmentIndex = 0; segmentIndex < _segments.Count; segmentIndex++)
            {
                var segment = _segments[segmentIndex];
                for (var pieceIndex = segment.PieceStart; pieceIndex < segment.PieceStart + segment.PieceCount; pieceIndex++)
                {
                    var piece = _pieces[pieceIndex];
                    if (pieceIndex == segment.PieceStart && trimmedStart is { } newStart)
                    {
                        piece = piece with { P0 = newStart, P1 = piece.P1 + (newStart - piece.P0) };
                    }

                    AppendCubic(piece);
                }

                var isLast = segmentIndex == _segments.Count - 1;
                if (isLast && !closed)
                    break;

                var next = _segments[isLast ? 0 : segmentIndex + 1];
                trimmedStart = AppendJoin(segment, next, distance);
            }

            if (!closed)
                return;

            // The closing join may have trimmed the contour's first anchor
            if (trimmedStart is { } closingStart)
            {
                var delta = closingStart - _positions[contourStart];
                _positions[contourStart] = closingStart;
                _handlesOut[contourStart] += delta;
                _handlesIn[contourStart] += delta;
            }

            var last = _positions.Count - 1;
            if (last > contourStart && Vector3.DistanceSquared(_positions[last], _positions[contourStart]) <= _tolerance * _tolerance)
            {
                _handlesIn[contourStart] = _handlesIn[last] + (_positions[contourStart] - _positions[last]);
                RemoveLastAnchor();
            }
        }

        /// <summary>
        /// Connects the offset end of <paramref name="segment"/> to the offset start of <paramref name="next"/>.
        /// Returns a replacement start for <paramref name="next"/> when the corner trims or extends it.
        /// </summary>
        private Vector3? AppendJoin(in SegmentInfo segment, in SegmentInfo next, float distance)
        {
            var lastPiece = _pieces[segment.PieceStart + segment.PieceCount - 1];
            var firstPiece = _pieces[next.PieceStart];
            var end = lastPiece.P3;
            var start = firstPiece.P0;
            if (Vector3.DistanceSquared(end, start) <= _tolerance * _tolerance)
                return null; // tangent-continuous anchor

            var turn = Cross(segment.TangentEnd, next.TangentStart);
            var hasIntersection = TryIntersect(end, segment.TangentEnd, start, next.TangentStart, out var alongEnd, out var alongStart);
            var isInside = turn * distance > 0;

            if (isInside)
            {
                // Trim both pieces back to where their tangent lines cross, unless that overshoots them
                var canTrim = hasIntersection
                              && alongEnd <= 0 && -alongEnd <= Vector3.Distance(lastPiece.P0, lastPiece.P3)
                              && alongStart >= 0 && alongStart <= Vector3.Distance(firstPiece.P0, firstPiece.P3);
                if (!canTrim)
                    return null; // falls back to a straight connection

                var corner = end + segment.TangentEnd * alongEnd;
                var lastIndex = _positions.Count - 1;
                var delta = corner - _positions[lastIndex];
                _positions[lastIndex] = corner;
                _handlesIn[lastIndex] += delta;
                _handlesOut[lastIndex] = corner;
                return corner;
            }

            var anchor = segment.EndAnchor;
            switch (_join)
            {
                case Joins.Miter:
                {
                    if (!hasIntersection || alongEnd < 0 || alongStart > 0)
                        break;

                    var miter = end + segment.TangentEnd * alongEnd;
                    if (Vector3.Distance(miter, anchor) > _miterLimit * MathF.Abs(distance))
                        break;

                    // Straight pieces are extended into the miter instead of getting a collinear extra anchor
                    if (IsStraight(lastPiece))
                    {
                        var lastIndex = _positions.Count - 1;
                        _positions[lastIndex] = miter;
                        _handlesIn[lastIndex] = miter;
                        _handlesOut[lastIndex] = miter;
                    }
                    else
                    {
                        LineTo(miter);
                    }

                    return IsStraight(firstPiece) ? miter : null;
                }
                case Joins.Round:
                    AppendArc(anchor, end, start);
                    break;
            }

            // Bevel, and the remaining gap of the other joins, is the straight line to the next piece's start
            return null;
        }

        /** Circular arc around center from one point to another the short way, as cubics of up to 90 degrees. */
        private void AppendArc(Vector3 center, Vector3 from, Vector3 to)
        {
            var a = from - center;
            var b = to - center;
            var angle = MathF.Atan2(Cross(a, b), a.X * b.X + a.Y * b.Y);
            var pieceCount = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(angle) / (MathF.PI * 0.5f)));
            var pieceAngle = angle / pieceCount;
            var handleFactor = 4f / 3f * MathF.Tan(pieceAngle * 0.25f);
            var (sin, cos) = MathF.SinCos(pieceAngle);

            var v0 = new Vector3(a.X, a.Y, 0);
            for (var i = 0; i < pieceCount; i++)
            {
                var v1 = new Vector3(v0.X * cos - v0.Y * sin, v0.X * sin + v0.Y * cos, 0);
                var p0 = i == 0 ? from : center + v0;
                var p3 = i == pieceCount - 1 ? to : center + v1;
                var arc = new Cubic(p0,
                                    p0 + Perpendicular(v0) * handleFactor,
                                    p3 - Perpendicular(v1) * handleFactor,
                                    p3);
                AppendCubic(arc);
                v0 = v1;
            }
        }

        /// <summary>
        /// Offsets a cubic by matching the exact offset's position and tangent at both ends,
        /// splitting in half while the middle deviates by more than the tolerance.
        /// </summary>
        private void OffsetCubic(in Cubic c, float distance, int depth)
        {
            TryGetTangents(c, out var tangentStart, out var tangentEnd);
            var q0 = c.P0 + LeftNormal(tangentStart) * distance;
            var q3 = c.P3 + LeftNormal(tangentEnd) * distance;
            var q1 = q0 + (c.P1 - c.P0) * GetSpeedFactor(3 * (c.P1 - c.P0), 6 * (c.P2 - 2 * c.P1 + c.P0), distance);
            var q2 = q3 + (c.P2 - c.P3) * GetSpeedFactor(3 * (c.P3 - c.P2), 6 * (c.P3 - 2 * c.P2 + c.P1), distance);
            var result = new Cubic(q0, q1, q2, q3);

            if (depth < MaxSplitDepth && !IsWithinTolerance(c, result, distance))
            {
                Split(c, out var left, out var right);
                OffsetCubic(left, distance, depth + 1);
                OffsetCubic(right, distance, depth + 1);
                return;
            }

            _pieces.Add(result);
        }

        private bool IsWithinTolerance(in Cubic source, in Cubic offsetCubic, float distance)
        {
            var toleranceSq = _tolerance * _tolerance;
            for (var sample = 1; sample <= 3; sample++)
            {
                var t = sample * 0.25f;
                var derivative = Bezier.GetFirstDerivative(source.P0, source.P1, source.P2, source.P3, t);
                if (derivative.X * derivative.X + derivative.Y * derivative.Y < 1e-14f)
                    continue;

                var exact = Bezier.GetPoint(source.P0, source.P1, source.P2, source.P3, t) + LeftNormal(Normalize2D(derivative)) * distance;
                var approximated = Bezier.GetPoint(offsetCubic.P0, offsetCubic.P1, offsetCubic.P2, offsetCubic.P3, t);
                if (Vector3.DistanceSquared(exact, approximated) > toleranceSq)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// How much the offset curve's speed differs from the source at an end: the offset point
        /// sits at radius r - d from the center of curvature, so it moves (r - d) / r as fast.
        /// </summary>
        private static float GetSpeedFactor(Vector3 firstDerivative, Vector3 secondDerivative, float distance)
        {
            var speedSq = firstDerivative.X * firstDerivative.X + firstDerivative.Y * firstDerivative.Y;
            if (speedSq < 1e-14f)
                return 1;

            var curvature = Cross(firstDerivative, secondDerivative) / (speedSq * MathF.Sqrt(speedSq));
            return MathF.Max(1 - distance * curvature, 0);
        }

        /** Unit tangents in the XY plane at both ends, skipping handles that sit on their anchor. False for a zero-length segment. */
        private static bool TryGetTangents(in Cubic c, out Vector3 tangentStart, out Vector3 tangentEnd)
        {
            tangentStart = FirstDirection(c.P1 - c.P0, c.P2 - c.P0, c.P3 - c.P0);
            tangentEnd = FirstDirection(c.P3 - c.P2, c.P3 - c.P1, c.P3 - c.P0);
            return tangentStart != Vector3.Zero && tangentEnd != Vector3.Zero;
        }

        private static Vector3 FirstDirection(Vector3 a, Vector3 b, Vector3 c)
        {
            if (a.X * a.X + a.Y * a.Y > MinDirectionLengthSq)
                return Normalize2D(a);

            if (b.X * b.X + b.Y * b.Y > MinDirectionLengthSq)
                return Normalize2D(b);

            if (c.X * c.X + c.Y * c.Y > MinDirectionLengthSq)
                return Normalize2D(c);

            return Vector3.Zero;
        }

        private bool IsStraight(in Cubic c)
        {
            var toleranceSq = _tolerance * _tolerance;
            return Vector3.DistanceSquared(c.P0, c.P1) <= toleranceSq && Vector3.DistanceSquared(c.P2, c.P3) <= toleranceSq;
        }

        /** Parameters along both lines (unit directions) of their crossing point; false when parallel. */
        private static bool TryIntersect(Vector3 pointA, Vector3 directionA, Vector3 pointB, Vector3 directionB, out float alongA, out float alongB)
        {
            var denominator = Cross(directionA, directionB);
            if (MathF.Abs(denominator) < 1e-6f)
            {
                alongA = alongB = 0;
                return false;
            }

            var between = pointB - pointA;
            alongA = Cross(between, directionB) / denominator;
            alongB = Cross(between, directionA) / denominator;
            return true;
        }

        private static void Split(in Cubic c, out Cubic left, out Cubic right)
        {
            var q0 = (c.P0 + c.P1) * 0.5f;
            var q1 = (c.P1 + c.P2) * 0.5f;
            var q2 = (c.P2 + c.P3) * 0.5f;
            var r0 = (q0 + q1) * 0.5f;
            var r1 = (q1 + q2) * 0.5f;
            var middle = (r0 + r1) * 0.5f;
            left = new Cubic(c.P0, q0, r0, middle);
            right = new Cubic(middle, r1, q2, c.P3);
        }

        /** Appends a cubic, merging its start into the previous anchor of the contour when they meet. */
        private void AppendCubic(in Cubic c)
        {
            var last = _positions.Count - 1;
            if (last >= _contourStart && Vector3.DistanceSquared(_positions[last], c.P0) <= _tolerance * _tolerance)
            {
                _handlesOut[last] = c.P1 + (_positions[last] - c.P0);
            }
            else
            {
                AddAnchor(c.P0, c.P0, c.P1);
            }

            AddAnchor(c.P3, c.P2, c.P3);
        }

        private void LineTo(Vector3 point)
        {
            var last = _positions.Count - 1;
            if (Vector3.DistanceSquared(_positions[last], point) <= _tolerance * _tolerance)
                return;

            AddAnchor(point, point, point);
        }

        private void AddAnchor(Vector3 position, Vector3 handleIn, Vector3 handleOut)
        {
            _positions.Add(position);
            _handlesIn.Add(handleIn);
            _handlesOut.Add(handleOut);
        }

        private void RemoveLastAnchor()
        {
            var last = _positions.Count - 1;
            _positions.RemoveAt(last);
            _handlesIn.RemoveAt(last);
            _handlesOut.RemoveAt(last);
        }

        private static float Cross(Vector3 a, Vector3 b) => a.X * b.Y - a.Y * b.X;
        private static Vector3 LeftNormal(Vector3 tangent) => new(-tangent.Y, tangent.X, 0);
        private static Vector3 Perpendicular(Vector3 v) => new(-v.Y, v.X, 0);

        private static Vector3 Normalize2D(Vector3 v)
        {
            var length = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            return new Vector3(v.X / length, v.Y / length, 0);
        }

        /** Splitting a cubic in half up to this often yields at most 128 pieces. */
        private const int MaxSplitDepth = 7;

        private const float MinDirectionLengthSq = 1e-14f;

        private Joins _join;
        private int _contourStart;
        private float _miterLimit;
        private float _tolerance;

        private readonly List<Vector3> _positions = [];
        private readonly List<Vector3> _handlesIn = [];
        private readonly List<Vector3> _handlesOut = [];
        private readonly List<int> _contourOffsets = [0];
        private readonly List<float> _contourSides = [];
        private readonly List<Cubic> _pieces = [];
        private readonly List<SegmentInfo> _segments = [];
    }

    private readonly OffsetBuilder _builder = new();
    private readonly CurveGeometry _output = new();

    [Input(Guid = "83e19891-6ac2-41c2-bf59-800811d28d79")]
    public readonly InputSlot<CurveGeometry> Curves = new();

    [Input(Guid = "b7803f2e-b869-4181-aa44-1b4bbc3935a9")]
    public readonly InputSlot<float> Offset = new();

    [Input(Guid = "1aa775f1-56e3-4ff3-9f1b-756ea64f9120", MappedType = typeof(Joins))]
    public readonly InputSlot<int> Join = new();

    [Input(Guid = "832f326f-c1d6-47b7-a543-680e40d5ffba")]
    public readonly InputSlot<float> MiterLimit = new();

    [Input(Guid = "1d3a918c-cdb2-4fa5-a74d-57c7743e5140")]
    public readonly InputSlot<float> Tolerance = new();
}

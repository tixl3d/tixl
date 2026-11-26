#nullable enable
using Lib.Utils;
using T3.Core.Utils;
using T3.Core.Utils.Splines;

namespace Lib.geometry.modify;

/// <summary>
/// Offsets every curve primitive of a Geometry sideways in the XY plane by a fixed distance.
/// Each cubic is offset by moving its anchors along the normal and scaling its handles by
/// the curvature there, split until it stays within Tolerance of the exact offset. Corners
/// are joined on the outside (miter, round or bevel) and trimmed to the intersection on the
/// inside, so closed contours stay closed and sharp corners stay sharp. Polygons pass through; primitive and part
/// attributes are kept, and the points of an offset curve take the point and vertex attributes of its first point.
/// A float selection (by default the flowing Selection, if any) scales the offset per curve; curves weighted 0 pass
/// through unchanged.
/// </summary>
[Guid("89ddc35c-ba3b-49a1-b766-a7356a12fe40")]
internal sealed class OffsetCurves : Instance<OffsetCurves>, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "eb51b623-b0bc-4686-986b-0148262ced85")]
    public readonly Slot<Geometry?> Result = new();

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
        var selectionName = GeometryAttributeChoices.ToSelectionValue(Selection.GetValue(context) ?? string.Empty);
        _lastSource = curves;
        _statusMessage = string.Empty;

        if (curves == null || curves.PointCount == 0 || offset == 0)
        {
            Result.Value = curves;
            return;
        }

        // Without a name the flowing Selection applies when there is one; <All> ignores any selection
        var ignoresSelection = GeometryAttributeChoices.IsAll(selectionName);
        var isNamed = !ignoresSelection && !string.IsNullOrWhiteSpace(selectionName);
        var hasSelection = !ignoresSelection && TryCollectCurveWeights(curves, isNamed ? selectionName : GeometryAttributeNames.Selection);
        if (isNamed && !hasSelection)
        {
            _statusMessage = $"No float attribute '{selectionName}'; nothing is offset.";
            Result.Value = curves;
            return;
        }

        _builder.Build(curves, offset, hasSelection ? _curveWeights : null, join, miterLimit, tolerance, _output);
        Result.Value = _output;
    }

    private bool TryCollectCurveWeights(Geometry curves, string name)
    {
        if (_curveWeights.Length < curves.PrimitiveCount)
            _curveWeights = new float[curves.PrimitiveCount];

        if (_pointWeights.Length < curves.PointCount)
            _pointWeights = new float[curves.PointCount];

        return GeometryElements.TryGetWeights(curves, AttributeDomain.Primitive, name, _curveWeights, _pointWeights);
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    public string GetStatusMessage() => _statusMessage;

    #region ICustomDropdownHolder - the float attributes of the last input as selections
    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => GeometryAttributeChoices.GetSelectionDisplayValue(Selection.Value, _lastSource);

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) => GeometryAttributeChoices.GetSelectionOptions(_lastSource);

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId == Selection.Input.InputDefinition.Id && selected != null)
            Selection.SetTypedInputValue(GeometryAttributeChoices.ToSelectionValue(selected));
    }
    #endregion

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
        /// <param name="curveWeights">Per primitive: scales the offset; null offsets every curve fully.</param>
        public void Build(Geometry source, float offset, float[]? curveWeights, Joins join, float miterLimit, float tolerance, Geometry target)
        {
            _positions.Clear();
            _handlesIn.Clear();
            _handlesOut.Clear();
            _vertexPoints.Clear();
            _pointBlend.Clear();
            _vertexBlend.Clear();

            _join = join;
            _miterLimit = miterLimit;
            _tolerance = tolerance;

            CurveWinding.GetOutsideSides(source, _contourSides);

            var sourcePointCount = source.PointCount;
            if (_pointMap.Length < sourcePointCount)
                _pointMap = new int[sourcePointCount];

            Array.Fill(_pointMap, -1, 0, sourcePointCount);

            var primitiveCount = source.PrimitiveCount;
            if (_primitiveOffsets.Length != primitiveCount + 1)
                _primitiveOffsets = new int[primitiveCount + 1];

            var curves = source.Curves;
            for (var primitive = 0; primitive < primitiveCount; primitive++)
            {
                _primitiveOffsets[primitive] = _vertexPoints.Count;
                var vertexStart = source.PrimitiveVertexOffsets[primitive];
                if (source.PrimitiveKind[primitive] == PrimitiveKinds.Curve)
                {
                    var firstPoint = _positions.Count;
                    var curve = curves[primitive];
                    var distance = offset * _contourSides[primitive] * (curveWeights?[primitive] ?? 1f);
                    if (distance == 0)
                    {
                        CopyContour(curve);
                    }
                    else
                    {
                        OffsetContour(curve, distance);
                    }

                    var sourcePoint = curve.VertexCount > 0 ? curve.GetPointIndex(0) : 0;
                    for (var point = firstPoint; point < _positions.Count; point++)
                    {
                        _vertexPoints.Add(point);
                        _pointBlend.AddCopy(sourcePoint);
                        _vertexBlend.AddCopy(vertexStart);
                    }

                    continue;
                }

                // Polygons pass through and keep sharing their points
                for (var vertex = vertexStart; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
                {
                    var point = source.VertexPoints[vertex];
                    if (_pointMap[point] < 0)
                    {
                        _pointMap[point] = _positions.Count;
                        var position = source.Positions[point];
                        AddAnchor(position, position, position);
                        _pointBlend.AddCopy(point);
                    }

                    _vertexPoints.Add(_pointMap[point]);
                    _vertexBlend.AddCopy(vertex);
                }
            }

            _primitiveOffsets[primitiveCount] = _vertexPoints.Count;
            WriteTarget(source, target);
        }

        private void WriteTarget(Geometry source, Geometry target)
        {
            var pointCount = _positions.Count;
            var primitiveCount = source.PrimitiveCount;
            target.Resize(pointCount, primitiveCount, _vertexPoints.Count);
            target.Attributes.Clear();

            _positions.CopyTo(target.Positions);
            _vertexPoints.CopyTo(target.VertexPoints);
            Array.Copy(_primitiveOffsets, target.PrimitiveVertexOffsets, primitiveCount + 1);
            Array.Copy(source.PrimitiveKind, target.PrimitiveKind, primitiveCount);
            Array.Copy(source.PrimitiveClosed, target.PrimitiveClosed, primitiveCount);
            target.Parts = source.Parts;

            _pointBlend.Apply(source.Attributes, target.Attributes, AttributeDomain.Point, HandleNames);
            _vertexBlend.Apply(source.Attributes, target.Attributes, AttributeDomain.Vertex);

            var handlesIn = target.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.HandleIn, AttributeDomain.Point, pointCount).Values;
            var handlesOut = target.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.HandleOut, AttributeDomain.Point, pointCount).Values;
            for (var point = 0; point < pointCount; point++)
            {
                handlesIn[point] = _handlesIn[point] - _positions[point];
                handlesOut[point] = _handlesOut[point] - _positions[point];
            }

            // Primitives and parts keep their count and order, so their attributes stay valid and are shared
            for (var index = 0; index < source.Attributes.Count; index++)
            {
                var attribute = source.Attributes[index];
                if (attribute.Domain is AttributeDomain.Primitive or AttributeDomain.Part)
                    target.Attributes.Add(attribute);
            }

            target.InvalidateTopologyCaches();
        }

        /// <param name="distance">Signed distance along the left normal of the contour's direction.</param>
        private void OffsetContour(CurveView curve, float distance)
        {
            var pointCount = curve.VertexCount;
            var segmentCount = curve.SegmentCount;
            var closed = curve.IsClosed;

            _pieces.Clear();
            _segments.Clear();
            for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                var a = segmentIndex;
                var b = (segmentIndex + 1) % pointCount;
                var cubic = new Cubic(curve.GetPosition(a), curve.GetHandleOutPosition(a), curve.GetHandleInPosition(b), curve.GetPosition(b));
                if (!TryGetTangents(cubic, out var tangentStart, out var tangentEnd))
                    continue; // zero-length segment: its neighbors join directly

                var pieceStart = _pieces.Count;
                OffsetCubic(cubic, distance, depth: 0);
                _segments.Add(new SegmentInfo(pieceStart, _pieces.Count - pieceStart, tangentStart, tangentEnd, cubic.P3));
            }

            if (_segments.Count == 0)
            {
                // Nothing with a direction to offset; keep the anchors so contour and part ranges stay valid
                CopyContour(curve);
                return;
            }

            _contourStart = _positions.Count;
            var contourStart = _contourStart;

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

        private void CopyContour(CurveView curve)
        {
            _contourStart = _positions.Count;
            for (var i = 0; i < curve.VertexCount; i++)
                AddAnchor(curve.GetPosition(i), curve.GetHandleInPosition(i), curve.GetHandleOutPosition(i));
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
        private readonly List<int> _vertexPoints = [];
        private readonly GeometryAttributeBlend _pointBlend = new();
        private readonly GeometryAttributeBlend _vertexBlend = new();
        private int[] _pointMap = [];
        private int[] _primitiveOffsets = [];
        private readonly List<float> _contourSides = [];
        private readonly List<Cubic> _pieces = [];
        private readonly List<SegmentInfo> _segments = [];
    }

    private Geometry? _lastSource;
    private string _statusMessage = string.Empty;

    /** Per primitive: its selection weight, read only when a selection applies. */
    private float[] _curveWeights = [];

    /** Scratch for point weights while deriving the curve weights. */
    private float[] _pointWeights = [];

    private readonly OffsetBuilder _builder = new();
    private static readonly string[] HandleNames = [GeometryAttributeNames.HandleIn, GeometryAttributeNames.HandleOut];
    private readonly Geometry _output = new();

        [Input(Guid = "83e19891-6ac2-41c2-bf59-800811d28d79")]
        public readonly InputSlot<T3.Core.DataTypes.Geometry> Curves = new InputSlot<T3.Core.DataTypes.Geometry>();

        [Input(Guid = "b7803f2e-b869-4181-aa44-1b4bbc3935a9")]
        public readonly InputSlot<float> Offset = new InputSlot<float>();

        [Input(Guid = "8c1d1e2c-77cf-4e63-8215-f70bcee8230c")]
        public readonly InputSlot<string> Selection = new InputSlot<string>();

        [Input(Guid = "1aa775f1-56e3-4ff3-9f1b-756ea64f9120", MappedType = typeof(Joins))]
        public readonly InputSlot<int> Join = new InputSlot<int>();

        [Input(Guid = "832f326f-c1d6-47b7-a543-680e40d5ffba")]
        public readonly InputSlot<float> MiterLimit = new InputSlot<float>();

        [Input(Guid = "1d3a918c-cdb2-4fa5-a74d-57c7743e5140")]
        public readonly InputSlot<float> Tolerance = new InputSlot<float>();
}

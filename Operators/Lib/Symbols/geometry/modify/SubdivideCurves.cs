#nullable enable
using System;
using System.Collections.Generic;
using Lib.Utils;
using T3.Core.DataTypes;
using T3.Core.Utils.Splines;

namespace Lib.geometry.modify;

/// <summary>
/// Subdivides the curve primitives of a Geometry by splitting cubics whose approximated length exceeds
/// MaxEdgeLength into shorter sub-cubics. The split is an exact De Casteljau subdivision, so the curve shape is
/// preserved; only the point and segment density changes. When EvenSpacing is on, sub-segments are equal in arc
/// length rather than equal in parameter.
///
/// Curve points are renumbered in order along each curve, so index-based selections step along the outline.
/// Polygons and points without primitives pass through. Point and vertex attributes are interpolated onto new
/// points; primitive and part attributes are kept.
/// </summary>
[Guid("f7a2c9e1-4b63-4d08-9e5a-2c7f8b1d6a34")]
internal sealed class SubdivideCurves : Instance<SubdivideCurves>
{
    [Output(Guid = "3e8b5f2a-9c14-4d67-a0e3-7b1f6c9a2e85")]
    public readonly Slot<Geometry?> Result = new();

    public SubdivideCurves()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var geometry = Curves.GetValue(context);
        var maxEdgeLength = MathF.Max(MaxEdgeLength.GetValue(context), 0f);
        var evenSpacing = EvenSpacing.GetValue(context);

        if (geometry == null || maxEdgeLength <= 0f || geometry.PointCount == 0)
        {
            // Nothing to subdivide; pass the input through unchanged.
            Result.Value = geometry;
            return;
        }

        SubdivideInto(geometry, maxEdgeLength, evenSpacing, _output);
        Result.Value = _output;
    }

    private void SubdivideInto(Geometry source, float maxEdgeLength, bool evenSpacing, Geometry target)
    {
        _positions.Clear();
        _handlesIn.Clear();
        _handlesOut.Clear();
        _vertexPoints.Clear();
        _pointBlend.Clear();
        _vertexBlend.Clear();

        var sourcePointCount = source.PointCount;
        if (_pointMap.Length < sourcePointCount)
        {
            _pointMap = new int[sourcePointCount];
            _isPointUsed = new bool[sourcePointCount];
        }

        Array.Fill(_pointMap, -1, 0, sourcePointCount);
        Array.Clear(_isPointUsed, 0, sourcePointCount);

        var primitiveCount = source.PrimitiveCount;
        if (_primitiveOffsets.Length != primitiveCount + 1)
            _primitiveOffsets = new int[primitiveCount + 1];

        var curves = source.Curves;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            _primitiveOffsets[primitive] = _vertexPoints.Count;
            if (source.PrimitiveKind[primitive] == PrimitiveKinds.Curve)
            {
                EmitCurve(curves[primitive], maxEdgeLength, evenSpacing);
            }
            else
            {
                EmitSharedVertices(source, primitive);
            }
        }

        _primitiveOffsets[primitiveCount] = _vertexPoints.Count;

        // Points no primitive references (e.g. a point cloud) keep their place at the end.
        for (var point = 0; point < sourcePointCount; point++)
        {
            if (_isPointUsed[point])
                continue;

            AddPointWithoutHandles(source.Positions[point]);
            _pointBlend.AddCopy(point);
        }

        WriteTarget(source, target);
    }

    /// <summary>Polygon vertices keep sharing their points; each source point is emitted once, at first use.</summary>
    private void EmitSharedVertices(Geometry source, int primitive)
    {
        var start = source.PrimitiveVertexOffsets[primitive];
        var end = source.PrimitiveVertexOffsets[primitive + 1];
        for (var vertex = start; vertex < end; vertex++)
        {
            var point = source.VertexPoints[vertex];
            if (_pointMap[point] < 0)
            {
                _pointMap[point] = _positions.Count;
                _isPointUsed[point] = true;
                AddPointWithoutHandles(source.Positions[point]);
                _pointBlend.AddCopy(point);
            }

            _vertexPoints.Add(_pointMap[point]);
            _vertexBlend.AddCopy(vertex);
        }
    }

    /// <summary>
    /// Emits one new point per curve vertex followed by the intermediate points of its outgoing segment, so the
    /// points run along the curve. Handles are collected as absolute positions and turned into offsets at the end.
    /// </summary>
    private void EmitCurve(CurveView curve, float maxEdgeLength, bool evenSpacing)
    {
        var vertexCount = curve.VertexCount;
        var segmentCount = curve.SegmentCount;
        var firstNewPoint = _positions.Count;

        for (var index = 0; index < vertexCount; index++)
        {
            var sourcePoint = curve.GetPointIndex(index);
            var sourceVertex = curve.VertexStart + index;
            _isPointUsed[sourcePoint] = true;

            var anchorPoint = _positions.Count;
            AddPoint(curve.GetPosition(index), curve.GetHandleInPosition(index), curve.GetHandleOutPosition(index));
            _pointBlend.AddCopy(sourcePoint);
            _vertexPoints.Add(anchorPoint);
            _vertexBlend.AddCopy(sourceVertex);

            if (index >= segmentCount)
                continue;

            var nextIndex = (index + 1) % vertexCount;
            var p0 = curve.GetPosition(index);
            var p1 = curve.GetHandleOutPosition(index);
            var p2 = curve.GetHandleInPosition(nextIndex);
            var p3 = curve.GetPosition(nextIndex);

            var subdivisions = Math.Max(1, (int)MathF.Ceiling(ApproximateCubicLength(p0, p1, p2, p3) / maxEdgeLength));
            if (subdivisions == 1)
                continue;

            Split(p0, p1, p2, p3, subdivisions, evenSpacing);

            // The segment's own handles shrink to the first and last sub-cubic.
            _handlesOut[anchorPoint] = _subCubics[0].P1;
            var nextSourcePoint = curve.GetPointIndex(nextIndex);
            var nextSourceVertex = curve.VertexStart + nextIndex;
            for (var k = 0; k < subdivisions - 1; k++)
            {
                AddPoint(_subCubics[k].P3, _subCubics[k].P2, _subCubics[k + 1].P1);
                _pointBlend.Add(sourcePoint, nextSourcePoint, _splitParameters[k]);
                _vertexPoints.Add(_positions.Count - 1);
                _vertexBlend.Add(sourceVertex, nextSourceVertex, _splitParameters[k]);
            }

            // The incoming handle of the segment's end belongs to a point emitted later, or for the closing segment
            // to the curve's first point.
            var lastSubCubicHandleIn = _subCubics[subdivisions - 1].P2;
            if (nextIndex == 0)
            {
                _handlesIn[firstNewPoint] = lastSubCubicHandleIn;
            }
            else
            {
                _pendingHandleIn = lastSubCubicHandleIn;
                _hasPendingHandleIn = true;
            }
        }
    }

    private void AddPointWithoutHandles(Vector3 position) => AddPoint(position, position, position);

    /** Handles are absolute positions here; a handle at the point itself means none. */
    private void AddPoint(Vector3 position, Vector3 handleIn, Vector3 handleOut)
    {
        if (_hasPendingHandleIn)
        {
            handleIn = _pendingHandleIn;
            _hasPendingHandleIn = false;
        }

        _positions.Add(position);
        _handlesIn.Add(handleIn);
        _handlesOut.Add(handleOut);
    }

    private void WriteTarget(Geometry source, Geometry target)
    {
        var pointCount = _positions.Count;
        var vertexCount = _vertexPoints.Count;
        var primitiveCount = source.PrimitiveCount;
        target.Resize(pointCount, primitiveCount, vertexCount);
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
            var position = _positions[point];
            handlesIn[point] = _handlesIn[point] - position;
            handlesOut[point] = _handlesOut[point] - position;
        }

        // Primitive count and order are unchanged, so these attributes are shared as they are. Edge attributes are
        // dropped: renumbered points change the derived edge order.
        var attributes = source.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain is AttributeDomain.Primitive or AttributeDomain.Part)
                target.Attributes.Add(attribute);
        }

        target.InvalidateTopologyCaches();
    }

    /// <summary>Rough length of a cubic from its control polygon and chord.</summary>
    private static float ApproximateCubicLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        var polyLen = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p3);
        var chordLen = Vector3.Distance(p0, p3);
        return (polyLen + chordLen) * 0.5f;
    }

    /// <summary>
    /// Fills <see cref="_subCubics"/> with <paramref name="count"/> sub-cubics of the segment and
    /// <see cref="_splitParameters"/> with the segment parameter at the end of each but the last.
    /// </summary>
    private void Split(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int count, bool evenSpacing)
    {
        _subCubics.Clear();
        _splitParameters.Clear();
        for (var k = 1; k < count; k++)
            _splitParameters.Add(evenSpacing ? 0 : k / (float)count);

        if (evenSpacing && !TryFillEvenSpacingParameters(p0, p1, p2, p3, count))
        {
            for (var k = 1; k < count; k++)
                _splitParameters[k - 1] = k / (float)count;
        }

        // Sequential De Casteljau splits of the remaining right part, remapping each global parameter into it.
        var remaining = new Cubic(p0, p1, p2, p3);
        var remainingStart = 0f;
        for (var k = 0; k < count - 1; k++)
        {
            var localT = Math.Clamp((_splitParameters[k] - remainingStart) / (1f - remainingStart), 1e-7f, 1f - 1e-7f);
            SplitAt(remaining, localT, out var left, out remaining);
            _subCubics.Add(left);
            remainingStart = _splitParameters[k];
        }

        _subCubics.Add(remaining);
    }

    /// <summary>Maps equal arc-length steps to parameters with a small length table. False for a zero-length segment.</summary>
    private bool TryFillEvenSpacingParameters(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int count)
    {
        var previous = p0;
        _lengthTable[0] = 0f;
        for (var i = 1; i <= LengthTableSize; i++)
        {
            var point = Bezier.GetPoint(p0, p1, p2, p3, i / (float)LengthTableSize);
            _lengthTable[i] = _lengthTable[i - 1] + Vector3.Distance(previous, point);
            previous = point;
        }

        var totalLength = _lengthTable[LengthTableSize];
        if (totalLength <= 1e-9f)
            return false;

        for (var k = 1; k < count; k++)
        {
            var targetLength = totalLength * k / count;
            int lo = 0, hi = LengthTableSize;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (_lengthTable[mid] < targetLength)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            var span = _lengthTable[hi] - _lengthTable[lo];
            var fraction = span > 0f ? (targetLength - _lengthTable[lo]) / span : 0f;
            _splitParameters[k - 1] = (lo + fraction) / LengthTableSize;
        }

        return true;
    }

    private static void SplitAt(in Cubic c, float t, out Cubic left, out Cubic right)
    {
        var q0 = Vector3.Lerp(c.P0, c.P1, t);
        var q1 = Vector3.Lerp(c.P1, c.P2, t);
        var q2 = Vector3.Lerp(c.P2, c.P3, t);
        var r0 = Vector3.Lerp(q0, q1, t);
        var r1 = Vector3.Lerp(q1, q2, t);
        var s = Vector3.Lerp(r0, r1, t);
        left = new Cubic(c.P0, q0, r0, s);
        right = new Cubic(s, r1, q2, c.P3);
    }

    private readonly record struct Cubic(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3);

    private const int LengthTableSize = 64;
    private static readonly string[] HandleNames = [GeometryAttributeNames.HandleIn, GeometryAttributeNames.HandleOut];

    [Input(Guid = "4a7e2c9f-6d15-4b83-a0e6-9c3f8b1d5e27")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "85c26820-f36b-4f44-9e78-4b63be33a65d")]
    public readonly InputSlot<float> MaxEdgeLength = new();

    [Input(Guid = "b8d3f5a2-9e41-4c76-8a0b-5d2f7c9e1a34")]
    public readonly InputSlot<bool> EvenSpacing = new();

    private readonly Geometry _output = new();
    private readonly GeometryAttributeBlend _pointBlend = new();
    private readonly GeometryAttributeBlend _vertexBlend = new();
    private readonly List<Vector3> _positions = [];
    private readonly List<Vector3> _handlesIn = [];
    private readonly List<Vector3> _handlesOut = [];
    private readonly List<int> _vertexPoints = [];
    private readonly List<Cubic> _subCubics = [];
    private readonly List<float> _splitParameters = [];
    private readonly float[] _lengthTable = new float[LengthTableSize + 1];
    private int[] _primitiveOffsets = [];
    private int[] _pointMap = [];
    private bool[] _isPointUsed = [];
    private Vector3 _pendingHandleIn;
    private bool _hasPendingHandleIn;
}

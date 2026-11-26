#nullable enable
using T3.Core.Utils.Splines;

namespace Lib.Utils;

/// <summary>
/// Flattens the curve primitives of a Geometry into a point list, one polyline per curve separated by separator
/// points, for [CurvesToCPoints] and [CurvesToPoints]. Polygons are skipped unless their edges are requested. With
/// Sampling = Points only the curves' own points are emitted, without separators, e.g. to mark or instance on them. A
/// Color attribute on the primitive or part domain lands in Point.Color. F1 and F2 default to 1 and the part index, or
/// take an index, a normalized position along the curve or a float or int attribute (see <see cref="FieldSource"/>).
/// Each point's +Z axis faces away from the filled area in the XY plane, +Y is the plane's normal and +X runs along the
/// contour.
/// </summary>
internal sealed class CurvePointSampler
{
    internal enum FieldSources
    {
        Default,
        Index,
        NormalizedIndex,
        NormalizedLength,
        CurveIndex,
        PartIndex,
        Attribute,
    }

    /// <summary>
    /// What to write into F1 or F2. Stored as text: empty for Default, a bracketed keyword such as &lt;Index&gt;, or
    /// the name of a float or int attribute.
    /// </summary>
    internal readonly record struct FieldSource(FieldSources Kind, string AttributeName)
    {
        public static FieldSource Parse(string? value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
                return default;

            for (var index = 0; index < KeywordLabels.Length; index++)
            {
                if (string.Equals(trimmed, KeywordLabels[index], StringComparison.OrdinalIgnoreCase))
                    return new FieldSource((FieldSources)index, string.Empty);
            }

            return new FieldSource(FieldSources.Attribute, trimmed);
        }

        /// <summary>The dropdown entry that shows a stored value; empty shows as &lt;Default&gt;.</summary>
        public static string GetDisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? KeywordLabels[0] : value;

        /// <summary>Maps a picked or typed entry back to the stored value.</summary>
        public static string ToStoredValue(string selected)
        {
            var trimmed = selected.Trim();
            return string.Equals(trimmed, KeywordLabels[0], StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
        }

        /// <summary>The keywords, then the distinct float and int attribute names of the geometry.</summary>
        public static IEnumerable<string> GetOptions(Geometry? geometry)
        {
            foreach (var label in KeywordLabels)
                yield return label;

            if (geometry == null)
                yield break;

            var attributes = geometry.Attributes;
            for (var index = 0; index < attributes.Count; index++)
            {
                var attribute = attributes[index];
                if (!IsScalar(attribute) || IsScalarNameListedBefore(attributes, index, attribute.Name))
                    continue;

                yield return attribute.Name;
            }
        }

        private static bool IsScalarNameListedBefore(GeometryAttributes attributes, int end, string name)
        {
            for (var index = 0; index < end; index++)
            {
                if (IsScalar(attributes[index]) && string.Equals(attributes[index].Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /** Indexed by <see cref="FieldSources"/>, without Attribute. */
        private static readonly string[] KeywordLabels = ["<Default>", "<Index>", "<NormalizedIndex>", "<NormalizedLength>", "<CurveIndex>", "<PartIndex>"];
    }

    internal enum Samplings
    {
        Flattened,
        Points,
        AllPoints,
    }

    internal enum PolygonEdgeModes
    {
        None,
        All,
        Sharp,
    }

    /// <summary>The attribute name of an F1 or F2 source that the last sampled geometry lacks, else null.</summary>
    public string? MissingAttributeName { get; private set; }

    /// <summary>Flattens the curves into the reused point list.</summary>
    /// <returns>Null without curves, or without any primitive unless sampling AllPoints.</returns>
    public StructuredList<Point>? Sample(Geometry? curves, float tolerance, bool closeLoops, Samplings sampling, PolygonEdgeModes edgeMode,
                                         float sharpAngle, FieldSource f1Source, FieldSource f2Source)
    {
        tolerance = MathF.Max(tolerance, 1e-5f);
        sharpAngle = Math.Clamp(sharpAngle, 0f, 180f);
        var samplesPoints = sampling == Samplings.Points;
        MissingAttributeName = null;

        if (curves == null || (curves.PrimitiveCount == 0 && sampling != Samplings.AllPoints))
            return null;

        _f1.Resolve(curves, f1Source);
        _f2.Resolve(curves, f2Source);
        MissingAttributeName = _f1.IsMissing ? f1Source.AttributeName : _f2.IsMissing ? f2Source.AttributeName : null;
        if (_f1.Domain == AttributeDomain.Vertex || _f2.Domain == AttributeDomain.Vertex)
            BuildFirstVertexOfPoint(curves);

        if (sampling == Samplings.AllPoints)
        {
            WriteAllPoints(curves);
            return _pointList;
        }

        curves.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Part, out var partColors);
        curves.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Primitive, out var primitiveColors);
        curves.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Point, out var pointColors);
        var parts = curves.Parts;
        var curveViews = curves.Curves;
        CurveWinding.GetOutsideSides(curves, _contourSides);

        _positions.Clear();
        _pointCount = 0;
        var partIndex = 0;
        for (var contourIndex = 0; contourIndex < curves.PrimitiveCount; contourIndex++)
        {
            while (partIndex < parts.Length - 1 && contourIndex >= parts[partIndex].PrimitiveStart + parts[partIndex].PrimitiveCount)
                partIndex++;

            if (curves.PrimitiveKind[contourIndex] != PrimitiveKinds.Curve)
                continue;

            var curve = curveViews[contourIndex];
            var vertexStart = curves.PrimitiveVertexOffsets[contourIndex];
            ClearSamples();
            if (samplesPoints)
            {
                for (var index = 0; index < curve.VertexCount; index++)
                    AddSample(curve.GetPosition(index), vertexStart + index, vertexStart + index, 0);
            }
            else
            {
                FlattenCurve(curve, vertexStart, tolerance);
            }

            if (_positions.Count == 0)
                continue;

            var color = Vector4.One;
            if (primitiveColors != null)
                color = primitiveColors.Values[contourIndex];
            else if (partColors != null && parts.Length > 0)
                color = partColors.Values[partIndex];
            else if (pointColors != null)
                color = pointColors.Values[curves.VertexPoints[curves.PrimitiveVertexOffsets[contourIndex]]];

            // Sampled points match the curve's vertices one-to-one, so they can take their point's color
            var usesPointColors = samplesPoints && pointColors != null && primitiveColors == null && (partColors == null || parts.Length == 0);

            var f2 = parts.Length > 0 ? partIndex : contourIndex;
            var isContourClosed = curves.PrimitiveClosed[contourIndex];
            var closed = closeLoops && isContourClosed && !samplesPoints;
            var count = _positions.Count + (closed ? 1 : 0);
            var curvePart = parts.Length > 0 ? partIndex : 0;
            ComputeNormalizedLengths(count);
            EnsureCapacity(_pointCount + count + 1);
            for (var i = 0; i < count; i++)
            {
                var index = i % _positions.Count;
                var tangent = GetTangent(_positions, index, isContourClosed);
                _pointList.TypedElements[_pointCount++] = new Point
                                                              {
                                                                  Position = _positions[index],
                                                                  F1 = GetCurveValue(_f1, curves, 1, i, count, index, contourIndex, curvePart),
                                                                  F2 = GetCurveValue(_f2, curves, f2, i, count, index, contourIndex, curvePart),
                                                                  Orientation = GetOutwardOrientation(tangent, _contourSides[contourIndex]),
                                                                  Scale = Vector3.One,
                                                                  Color = usesPointColors ? pointColors!.Values[curves.VertexPoints[vertexStart + index]] : color,
                                                              };
            }

            if (!samplesPoints)
                _pointList.TypedElements[_pointCount++] = Point.Separator();
        }

        if (edgeMode != PolygonEdgeModes.None)
            AppendPolygonEdges(curves, edgeMode == PolygonEdgeModes.Sharp ? MathF.Cos(sharpAngle * MathF.PI / 180f) : 2f,
                               primitiveColors, partColors);

        if (_pointList.NumElements != _pointCount)
            _pointList.SetLength(_pointCount);

        return _pointList;
    }


    /// <summary>
    /// Polygon edges as two-point lines with a separator after each, e.g. to outline extruded text. With a cosine
    /// below 1 only sharp edges are emitted: open borders and edges whose two faces' normals differ by more than the
    /// angle, so flat regions (like a subdivided cap) stay clean.
    /// </summary>
    private void AppendPolygonEdges(Geometry geometry, float maxCos, GeometryAttribute<Vector4>? primitiveColors, GeometryAttribute<Vector4>? partColors)
    {
        var edges = geometry.Edges.Edges;
        if (edges.Length == 0)
            return;

        var checksAngle = maxCos < 1;
        if (checksAngle)
            ComputeFaceNormals(geometry);

        var partOfPrimitive = partColors != null || _f1.NeedsParts || _f2.NeedsParts ? GetPartOfPrimitive(geometry) : null;
        EnsureCapacity(_pointCount + edges.Length * 3);
        foreach (var edge in edges)
        {
            if (checksAngle && edge.Face1 >= 0 && Vector3.Dot(_faceNormals[edge.Face0], _faceNormals[edge.Face1]) >= maxCos)
                continue;

            var part = partOfPrimitive != null ? partOfPrimitive[edge.Face0] : -1;
            var color = primitiveColors != null ? primitiveColors.Values[edge.Face0]
                        : partColors != null && part >= 0 ? partColors.Values[part]
                        : Vector4.One;
            AddEdgePoint(geometry, edge.PointA, 0, edge.Face0, part, color);
            AddEdgePoint(geometry, edge.PointB, 1, edge.Face0, part, color);
            _pointList.TypedElements[_pointCount++] = Point.Separator();
        }
    }

    /// <summary>Every point of the geometry once, in point order, with its point Color (white without); no separators.</summary>
    private void WriteAllPoints(Geometry geometry)
    {
        geometry.Attributes.TryGet<Vector4>(GeometryAttributeNames.Color, AttributeDomain.Point, out var pointColors);
        var needsPrimitives = _f1.NeedsPrimitives || _f2.NeedsPrimitives;
        if (needsPrimitives)
            BuildFirstPrimitiveOfPoint(geometry);

        var partOfPrimitive = _f1.NeedsParts || _f2.NeedsParts ? GetPartOfPrimitive(geometry) : null;
        var pointCount = geometry.PointCount;
        _pointCount = 0;
        EnsureCapacity(Math.Max(pointCount, 1));
        for (var point = 0; point < pointCount; point++)
        {
            var primitive = needsPrimitives ? _firstPrimitiveOfPoint[point] : -1;
            var part = partOfPrimitive != null && primitive >= 0 ? partOfPrimitive[primitive] : -1;
            _pointList.TypedElements[_pointCount++] = new Point
                                                          {
                                                              Position = geometry.Positions[point],
                                                              F1 = GetPointValue(_f1, 1, point, pointCount, point, primitive, part),
                                                              F2 = GetPointValue(_f2, point, point, pointCount, point, primitive, part),
                                                              Orientation = Quaternion.Identity,
                                                              Scale = Vector3.One,
                                                              Color = pointColors != null ? pointColors.Values[point] : Vector4.One,
                                                          };
        }

        if (_pointList.NumElements != _pointCount)
            _pointList.SetLength(_pointCount);
    }

    /// <param name="end">0 for the edge's first point, 1 for its second.</param>
    private void AddEdgePoint(Geometry geometry, int point, int end, int face, int part, Vector4 color)
    {
        _pointList.TypedElements[_pointCount++] = new Point
                                                      {
                                                          Position = geometry.Positions[point],
                                                          F1 = GetPointValue(_f1, 1, end, 2, point, face, part),
                                                          F2 = GetPointValue(_f2, face, end, 2, point, face, part),
                                                          Orientation = Quaternion.Identity,
                                                          Scale = Vector3.One,
                                                          Color = color,
                                                      };
    }

    /// <summary>F1 or F2 of a point sampled from a curve.</summary>
    /// <param name="sample">The point's index in the sample lists; the closing point of a loop repeats sample 0.</param>
    private float GetCurveValue(FieldChannel channel, Geometry geometry, float defaultValue, int index, int count, int sample, int curve, int part)
    {
        switch (channel.Kind)
        {
            case FieldSources.Index:
                return index;
            case FieldSources.NormalizedIndex:
                return count > 1 ? index / (float)(count - 1) : 0;
            case FieldSources.NormalizedLength:
                return _normalizedLengths[index];
            case FieldSources.CurveIndex:
                return curve;
            case FieldSources.PartIndex:
                return part;
            case FieldSources.Attribute when !channel.IsMissing:
            {
                var vertexA = _sampleVertexA[sample];
                var vertexB = _sampleVertexB[sample];
                var t = _sampleT[sample];
                return channel.Domain switch
                           {
                               AttributeDomain.Point => Lerp(channel.Get(geometry.VertexPoints[vertexA]), channel.Get(geometry.VertexPoints[vertexB]), t),
                               AttributeDomain.Vertex => Lerp(channel.Get(vertexA), channel.Get(vertexB), t),
                               AttributeDomain.Primitive => channel.Get(curve),
                               _ => channel.Get(part),
                           };
            }
            default:
                return defaultValue;
        }
    }

    /// <summary>F1 or F2 of a point emitted for a geometry point (AllPoints sampling and polygon edges).</summary>
    /// <param name="primitive">A primitive using the point, or -1.</param>
    /// <param name="part">The part of that primitive, or -1.</param>
    private float GetPointValue(FieldChannel channel, float defaultValue, int index, int count, int point, int primitive, int part)
    {
        switch (channel.Kind)
        {
            case FieldSources.Index:
                return index;
            case FieldSources.NormalizedIndex:
            case FieldSources.NormalizedLength:
                return count > 1 ? index / (float)(count - 1) : 0;
            case FieldSources.CurveIndex:
                return primitive;
            case FieldSources.PartIndex:
                return Math.Max(part, 0); // without parts, everything is the one implicit part
            case FieldSources.Attribute when !channel.IsMissing:
                return channel.Domain switch
                           {
                               AttributeDomain.Point => channel.Get(point),
                               AttributeDomain.Vertex => channel.Get(_firstVertexOfPoint[point]),
                               AttributeDomain.Primitive => channel.Get(primitive),
                               _ => channel.Get(part),
                           };
            default:
                return defaultValue;
        }
    }

    /// <summary>
    /// Flattens like <see cref="CurveView.Flatten"/> and records for every sample the vertices of its segment and how
    /// far along the segment it lies, measured along the flattened chords.
    /// </summary>
    private void FlattenCurve(CurveView curve, int vertexStart, float tolerance)
    {
        var vertexCount = curve.VertexCount;
        if (vertexCount == 0)
            return;

        AddSample(curve.GetPosition(0), vertexStart, vertexStart, 0);
        var segmentCount = curve.SegmentCount;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            var a = segment;
            var b = (segment + 1) % vertexCount;
            var includeEnd = !(segment == segmentCount - 1 && curve.IsClosed);
            var firstNew = _positions.Count;
            BezierFlattening.FlattenSegment(curve.GetPosition(a), curve.GetHandleOutPosition(a), curve.GetHandleInPosition(b), curve.GetPosition(b),
                                            tolerance, _positions, includeEnd);

            var segmentLength = 0f;
            for (var sample = firstNew; sample < _positions.Count; sample++)
                segmentLength += Vector3.Distance(_positions[sample - 1], _positions[sample]);

            if (!includeEnd)
                segmentLength += Vector3.Distance(_positions[^1], curve.GetPosition(b));

            var along = 0f;
            for (var sample = firstNew; sample < _positions.Count; sample++)
            {
                along += Vector3.Distance(_positions[sample - 1], _positions[sample]);
                _sampleVertexA.Add(vertexStart + a);
                _sampleVertexB.Add(vertexStart + b);
                _sampleT.Add(segmentLength > 0 ? along / segmentLength : 1);
            }
        }
    }

    /// <summary>Arc length from the start, divided by the total, for the samples plus the closing point if any.</summary>
    private void ComputeNormalizedLengths(int count)
    {
        _normalizedLengths.Clear();
        var length = 0f;
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                length += Vector3.Distance(_positions[(i - 1) % _positions.Count], _positions[i % _positions.Count]);

            _normalizedLengths.Add(length);
        }

        if (length <= 0)
            return;

        for (var i = 0; i < count; i++)
            _normalizedLengths[i] /= length;
    }

    private void ClearSamples()
    {
        _positions.Clear();
        _sampleVertexA.Clear();
        _sampleVertexB.Clear();
        _sampleT.Clear();
    }

    private void AddSample(Vector3 position, int vertexA, int vertexB, float t)
    {
        _positions.Add(position);
        _sampleVertexA.Add(vertexA);
        _sampleVertexB.Add(vertexB);
        _sampleT.Add(t);
    }

    private void BuildFirstVertexOfPoint(Geometry geometry)
    {
        if (_firstVertexOfPoint.Length < geometry.PointCount)
            _firstVertexOfPoint = new int[geometry.PointCount];

        Array.Fill(_firstVertexOfPoint, -1, 0, geometry.PointCount);
        for (var vertex = geometry.VertexCount - 1; vertex >= 0; vertex--)
            _firstVertexOfPoint[geometry.VertexPoints[vertex]] = vertex;
    }

    private void BuildFirstPrimitiveOfPoint(Geometry geometry)
    {
        if (_firstPrimitiveOfPoint.Length < geometry.PointCount)
            _firstPrimitiveOfPoint = new int[geometry.PointCount];

        Array.Fill(_firstPrimitiveOfPoint, -1, 0, geometry.PointCount);
        for (var primitive = geometry.PrimitiveCount - 1; primitive >= 0; primitive--)
        {
            for (var vertex = geometry.PrimitiveVertexOffsets[primitive]; vertex < geometry.PrimitiveVertexOffsets[primitive + 1]; vertex++)
                _firstPrimitiveOfPoint[geometry.VertexPoints[vertex]] = primitive;
        }
    }

    private static bool IsScalar(GeometryAttribute attribute) => attribute is GeometryAttribute<float> or GeometryAttribute<int>;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /** Unit normals of the polygons (Newell's method); curves get zero. */
    private void ComputeFaceNormals(Geometry geometry)
    {
        if (_faceNormals.Length < geometry.PrimitiveCount)
            _faceNormals = new Vector3[geometry.PrimitiveCount];

        for (var primitive = 0; primitive < geometry.PrimitiveCount; primitive++)
        {
            var normal = Vector3.Zero;
            if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Polygon)
            {
                var start = geometry.PrimitiveVertexOffsets[primitive];
                var end = geometry.PrimitiveVertexOffsets[primitive + 1];
                for (var vertex = start; vertex < end; vertex++)
                {
                    var p0 = geometry.Positions[geometry.VertexPoints[vertex]];
                    var p1 = geometry.Positions[geometry.VertexPoints[vertex + 1 == end ? start : vertex + 1]];
                    normal += new Vector3((p0.Y - p1.Y) * (p0.Z + p1.Z), (p0.Z - p1.Z) * (p0.X + p1.X), (p0.X - p1.X) * (p0.Y + p1.Y));
                }
            }

            _faceNormals[primitive] = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.Zero;
        }
    }

    private int[] GetPartOfPrimitive(Geometry geometry)
    {
        if (_partOfPrimitive.Length < geometry.PrimitiveCount)
            _partOfPrimitive = new int[geometry.PrimitiveCount];

        Array.Fill(_partOfPrimitive, -1, 0, geometry.PrimitiveCount);
        for (var part = 0; part < geometry.Parts.Length; part++)
        {
            var range = geometry.Parts[part];
            for (var primitive = range.PrimitiveStart; primitive < Math.Min(range.PrimitiveStart + range.PrimitiveCount, geometry.PrimitiveCount); primitive++)
                _partOfPrimitive[primitive] = part;
        }

        return _partOfPrimitive;
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

    /// <summary>One F1 or F2 source, resolved against a geometry: the attribute values as floats and their domain.</summary>
    private sealed class FieldChannel
    {
        public FieldSources Kind { get; private set; }

        /// <summary>The domain of the found attribute; null for keywords and missing attributes.</summary>
        public AttributeDomain? Domain { get; private set; }

        public bool IsMissing { get; private set; }

        public bool NeedsPrimitives => Kind is FieldSources.CurveIndex or FieldSources.PartIndex || Domain is AttributeDomain.Primitive or AttributeDomain.Part;
        public bool NeedsParts => Kind == FieldSources.PartIndex || Domain == AttributeDomain.Part;

        public void Resolve(Geometry geometry, FieldSource source)
        {
            Kind = source.Kind;
            Domain = null;
            IsMissing = false;
            if (Kind != FieldSources.Attribute)
                return;

            foreach (var domain in LookupOrder)
            {
                if (TryCopy(geometry, source.AttributeName, domain))
                {
                    Domain = domain;
                    return;
                }
            }

            IsMissing = true;
        }

        /** The value of an element, 0 outside the attribute (e.g. index -1 for points without a primitive). */
        public float Get(int element) => (uint)element < (uint)_count ? _values[element] : 0;

        private bool TryCopy(Geometry geometry, string name, AttributeDomain domain)
        {
            if (geometry.Attributes.TryGet<float>(name, domain, out var floats))
            {
                _values = floats.Values;
                _count = floats.Values.Length;
                return true;
            }

            if (!geometry.Attributes.TryGet<int>(name, domain, out var ints))
                return false;

            _count = ints.Values.Length;
            if (_converted.Length < _count)
                _converted = new float[_count];

            for (var index = 0; index < _count; index++)
                _converted[index] = ints.Values[index];

            _values = _converted;
            return true;
        }

        private static readonly AttributeDomain[] LookupOrder = [AttributeDomain.Point, AttributeDomain.Vertex, AttributeDomain.Primitive, AttributeDomain.Part];

        private float[] _values = [];
        private int _count;
        private float[] _converted = [];
    }

    private readonly FieldChannel _f1 = new();
    private readonly FieldChannel _f2 = new();
    private readonly List<int> _sampleVertexA = [];
    private readonly List<int> _sampleVertexB = [];
    private readonly List<float> _sampleT = [];
    private readonly List<float> _normalizedLengths = [];
    private int[] _firstVertexOfPoint = [];
    private int[] _firstPrimitiveOfPoint = [];

    private readonly StructuredList<Point> _pointList = new(1);
    private readonly List<Vector3> _positions = [];
    private readonly List<float> _contourSides = [];
    private Vector3[] _faceNormals = [];
    private int[] _partOfPrimitive = [];
    private int _pointCount;
}

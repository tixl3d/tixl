#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Utils.Splines;

namespace T3.Core.DataTypes;

/// <summary>The kind of a <see cref="Geometry"/> primitive.</summary>
public enum PrimitiveKinds : byte
{
    /// <summary>A closed N-gon face over its vertices' points.</summary>
    Polygon,

    /// <summary>A cubic bezier curve through its vertices' points, open or closed. Handles are point attributes.</summary>
    Curve,
}

/// <summary>
/// CPU-side geometry: points, and primitives (polygons and curves) whose vertices reference the points, with typed
/// attributes per domain and an optional part table. Modeled on Houdini's points, vertices and primitives.
///
/// Topology is CSR-style: primitive p uses vertices PrimitiveVertexOffsets[p] .. PrimitiveVertexOffsets[p+1], and each
/// vertex references a point via VertexPoints. A curve's segment from vertex a to the next vertex b runs through
/// Positions[a] + HandleOut[a] and Positions[b] + HandleIn[b] (point attributes, offsets); missing handles make it a
/// polyline. Arrays are authoritative (Length == element count).
///
/// Instances flow through the graph by reference: ops must not mutate their inputs. They build into their own reused
/// output and reallocate only when sizes change. Use <see cref="Polygons"/> and <see cref="Curves"/> to iterate one
/// kind without reading <see cref="PrimitiveKind"/> directly.
/// </summary>
public sealed class Geometry
{
    /// <summary>Point positions; the Point domain's element count.</summary>
    public Vector3[] Positions = [];

    /// <summary>Per primitive: start offset into VertexPoints. Length PrimitiveCount + 1 (last entry = vertex count).</summary>
    public int[] PrimitiveVertexOffsets = [0];

    /// <summary>Per vertex: the point it references. The Vertex domain's element count.</summary>
    public int[] VertexPoints = [];

    /// <summary>Per primitive: polygon or curve.</summary>
    public PrimitiveKinds[] PrimitiveKind = [];

    /// <summary>Per primitive: whether a curve connects its last vertex back to the first. Polygons are always closed.</summary>
    public bool[] PrimitiveClosed = [];

    /// <summary>
    /// Contiguous primitive ranges forming semantic pieces (glyphs, fracture cells, ...). Empty means one implicit
    /// part covering all primitives.
    /// </summary>
    public PrimitivePart[] Parts = [];

    public GeometryAttributes Attributes { get; } = new();

    public int PointCount => Positions.Length;
    public int PrimitiveCount => PrimitiveVertexOffsets.Length - 1;
    public int VertexCount => VertexPoints.Length;

    public int GetPrimitiveVertexCount(int primitiveIndex) => PrimitiveVertexOffsets[primitiveIndex + 1] - PrimitiveVertexOffsets[primitiveIndex];

    /// <summary>
    /// Sets the element counts, reallocating only arrays whose length changes. Contents are undefined afterwards and
    /// the topology caches are dropped. Attributes are not resized.
    /// </summary>
    public void Resize(int pointCount, int primitiveCount, int vertexCount)
    {
        if (Positions.Length != pointCount)
            Positions = new Vector3[pointCount];

        if (PrimitiveVertexOffsets.Length != primitiveCount + 1)
            PrimitiveVertexOffsets = new int[primitiveCount + 1];

        if (VertexPoints.Length != vertexCount)
            VertexPoints = new int[vertexCount];

        if (PrimitiveKind.Length != primitiveCount)
            PrimitiveKind = new PrimitiveKinds[primitiveCount];

        if (PrimitiveClosed.Length != primitiveCount)
            PrimitiveClosed = new bool[primitiveCount];

        InvalidateTopologyCaches();
    }

    /// <summary>The polygon primitives, as allocation-free views.</summary>
    public PolygonEnumerable Polygons => new(this);

    /// <summary>The curve primitives, as allocation-free views. Looks up the handle attributes once per call.</summary>
    public CurveEnumerable Curves => new(this);

    /// <summary>
    /// Unique edges of the polygons, derived on first access and cached. Call <see cref="InvalidateTopologyCaches"/>
    /// after changing primitives or vertices.
    /// </summary>
    public EdgeTopology Edges => _edgeTopology ??= EdgeTopology.Build(this);

    /// <summary>Call after changing positions or attributes only.</summary>
    public void InvalidateCaches()
    {
        Version++;
    }

    /// <summary>Call after changing primitives or vertices.</summary>
    public void InvalidateTopologyCaches()
    {
        _edgeTopology = null;
        Version++;
    }

    /// <summary>
    /// Bumped on every invalidation. Instances are reused across rebuilds, so reference identity can't signal change;
    /// consumers that cache derived data compare this counter instead.
    /// </summary>
    public int Version { get; private set; }

    private EdgeTopology? _edgeTopology;

    #region typed views
    public readonly struct PolygonEnumerable
    {
        internal PolygonEnumerable(Geometry geometry) => _geometry = geometry;
        public PolygonEnumerator GetEnumerator() => new(_geometry);
        private readonly Geometry _geometry;
    }

    public struct PolygonEnumerator
    {
        internal PolygonEnumerator(Geometry geometry)
        {
            _geometry = geometry;
            _primitiveIndex = -1;
        }

        public bool MoveNext()
        {
            var kinds = _geometry.PrimitiveKind;
            while (++_primitiveIndex < kinds.Length)
            {
                if (kinds[_primitiveIndex] == PrimitiveKinds.Polygon)
                    return true;
            }

            return false;
        }

        public PolygonView Current => new(_geometry, _primitiveIndex);

        private readonly Geometry _geometry;
        private int _primitiveIndex;
    }

    public readonly struct CurveEnumerable
    {
        internal CurveEnumerable(Geometry geometry)
        {
            _geometry = geometry;
            var attributes = geometry.Attributes;
            _handlesIn = attributes.TryGet<Vector3>(GeometryAttributeNames.HandleIn, AttributeDomain.Point, out var handlesIn) ? handlesIn.Values : null;
            _handlesOut = attributes.TryGet<Vector3>(GeometryAttributeNames.HandleOut, AttributeDomain.Point, out var handlesOut) ? handlesOut.Values : null;
        }

        public CurveEnumerator GetEnumerator() => new(_geometry, _handlesIn, _handlesOut);

        /// <summary>The curve view of one primitive, which must be a curve.</summary>
        public CurveView this[int primitiveIndex] => new(_geometry, primitiveIndex, _handlesIn, _handlesOut);

        private readonly Geometry _geometry;
        private readonly Vector3[]? _handlesIn;
        private readonly Vector3[]? _handlesOut;
    }

    public struct CurveEnumerator
    {
        internal CurveEnumerator(Geometry geometry, Vector3[]? handlesIn, Vector3[]? handlesOut)
        {
            _geometry = geometry;
            _handlesIn = handlesIn;
            _handlesOut = handlesOut;
            _primitiveIndex = -1;
        }

        public bool MoveNext()
        {
            var kinds = _geometry.PrimitiveKind;
            while (++_primitiveIndex < kinds.Length)
            {
                if (kinds[_primitiveIndex] == PrimitiveKinds.Curve)
                    return true;
            }

            return false;
        }

        public CurveView Current => new(_geometry, _primitiveIndex, _handlesIn, _handlesOut);

        private readonly Geometry _geometry;
        private readonly Vector3[]? _handlesIn;
        private readonly Vector3[]? _handlesOut;
        private int _primitiveIndex;
    }
    #endregion
}

/// <summary>A polygon primitive of a <see cref="Geometry"/>.</summary>
public readonly struct PolygonView
{
    internal PolygonView(Geometry geometry, int primitiveIndex)
    {
        _geometry = geometry;
        PrimitiveIndex = primitiveIndex;
    }

    public int PrimitiveIndex { get; }
    public int VertexStart => _geometry.PrimitiveVertexOffsets[PrimitiveIndex];
    public int VertexCount => _geometry.GetPrimitiveVertexCount(PrimitiveIndex);

    /// <param name="index">0 .. VertexCount - 1, in winding order.</param>
    public int GetPointIndex(int index) => _geometry.VertexPoints[VertexStart + index];

    public Vector3 GetPosition(int index) => _geometry.Positions[GetPointIndex(index)];

    private readonly Geometry _geometry;
}

/// <summary>A cubic bezier curve primitive of a <see cref="Geometry"/>.</summary>
public readonly struct CurveView
{
    internal CurveView(Geometry geometry, int primitiveIndex, Vector3[]? handlesIn, Vector3[]? handlesOut)
    {
        _geometry = geometry;
        _handlesIn = handlesIn;
        _handlesOut = handlesOut;
        PrimitiveIndex = primitiveIndex;
    }

    public int PrimitiveIndex { get; }
    public int VertexStart => _geometry.PrimitiveVertexOffsets[PrimitiveIndex];
    public int VertexCount => _geometry.GetPrimitiveVertexCount(PrimitiveIndex);
    public bool IsClosed => _geometry.PrimitiveClosed[PrimitiveIndex];

    /// <summary>One less than the vertices, or as many when closed.</summary>
    public int SegmentCount
    {
        get
        {
            var vertexCount = VertexCount;
            if (vertexCount < 2)
                return 0;

            return IsClosed ? vertexCount : vertexCount - 1;
        }
    }

    /// <param name="index">0 .. VertexCount - 1, along the curve.</param>
    public int GetPointIndex(int index) => _geometry.VertexPoints[VertexStart + index];

    public Vector3 GetPosition(int index) => _geometry.Positions[GetPointIndex(index)];

    /// <summary>Absolute position of the incoming handle; the point itself when there are no handles.</summary>
    public Vector3 GetHandleInPosition(int index)
    {
        var point = GetPointIndex(index);
        return _handlesIn == null ? _geometry.Positions[point] : _geometry.Positions[point] + _handlesIn[point];
    }

    /// <summary>Absolute position of the outgoing handle; the point itself when there are no handles.</summary>
    public Vector3 GetHandleOutPosition(int index)
    {
        var point = GetPointIndex(index);
        return _handlesOut == null ? _geometry.Positions[point] : _geometry.Positions[point] + _handlesOut[point];
    }

    /// <summary>Position on a segment at t in 0..1.</summary>
    public Vector3 Evaluate(int segmentIndex, float t)
    {
        GetSegmentVertices(segmentIndex, out var a, out var b);
        return Bezier.GetPoint(GetPosition(a), GetHandleOutPosition(a), GetHandleInPosition(b), GetPosition(b), t);
    }

    /// <summary>Tangent (first derivative) on a segment at t in 0..1.</summary>
    public Vector3 EvaluateTangent(int segmentIndex, float t)
    {
        GetSegmentVertices(segmentIndex, out var a, out var b);
        return Bezier.GetFirstDerivative(GetPosition(a), GetHandleOutPosition(a), GetHandleInPosition(b), GetPosition(b), t);
    }

    /// <summary>
    /// Appends a polyline approximation: segments are subdivided until the chord deviates less than
    /// <paramref name="tolerance"/>. The first point is included; the closing point of a closed curve is not.
    /// </summary>
    public void Flatten(float tolerance, List<Vector3> target)
    {
        if (VertexCount == 0)
            return;

        target.Add(GetPosition(0));
        var segmentCount = SegmentCount;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            GetSegmentVertices(segment, out var a, out var b);
            var isLast = segment == segmentCount - 1;
            BezierFlattening.FlattenSegment(GetPosition(a), GetHandleOutPosition(a), GetHandleInPosition(b), GetPosition(b),
                                            tolerance, target, includeEnd: !(isLast && IsClosed));
        }
    }

    /** Vertex indices within this curve at the start and end of a segment. */
    private void GetSegmentVertices(int segmentIndex, out int a, out int b)
    {
        a = segmentIndex;
        b = (segmentIndex + 1) % VertexCount;
    }

    private readonly Geometry _geometry;
    private readonly Vector3[]? _handlesIn;
    private readonly Vector3[]? _handlesOut;
}

/// <summary>A semantic piece of a <see cref="Geometry"/>: a contiguous primitive range with placement metadata.</summary>
/// <param name="SeedIndex">Index of the generating element (e.g. the character index) - lets consumers map parts back to their source.</param>
public readonly record struct PrimitivePart(int PrimitiveStart, int PrimitiveCount, Vector3 Pivot, int Id, int SeedIndex);

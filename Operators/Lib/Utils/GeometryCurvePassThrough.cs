#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// The counterpart of <see cref="GeometryPolygonFilter"/>: after an op has worked on the polygons only, appends the
/// source's curve primitives (or, for ops on closed curves, the open curves and polygons) to its result, so text outlines or paths in a mixed geometry survive the op. The curves
/// get copies of their points and keep their point, vertex and primitive attributes (handles included); attributes
/// only one side has are filled with zeros on the other. They come after all parts, since an op like a fracture
/// renumbers parts in ways a curve can't follow. Returns a new instance, cached while result and source are unchanged,
/// so ops that compute asynchronously can keep using earlier results. Reuse one per op.
/// </summary>
internal sealed class GeometryCurvePassThrough
{
    /// <summary>Which source primitives are appended.</summary>
    public enum Selections
    {
        /// <summary>All curves, for ops that work on polygons.</summary>
        Curves,

        /// <summary>Open curves and polygons, for ops that work on closed curves (e.g. merging their outlines).</summary>
        OpenCurvesAndPolygons,
    }

    public Geometry? Combine(Geometry? result, Geometry? source, Selections selection = Selections.Curves)
    {
        if (result == null || source == null || ReferenceEquals(result, source) || (selection == Selections.Curves && !source.HasCurves()))
            return result;

        if (_combined != null
            && ReferenceEquals(result, _lastResult) && result.Version == _lastResultVersion
            && ReferenceEquals(source, _lastSource) && source.Version == _lastSourceVersion
            && selection == _lastSelection)
            return _combined;

        CollectPrimitives(source, selection);
        if (_primitives.Count == 0)
            return result;

        var pointOffset = result.PointCount;
        var vertexOffset = result.VertexCount;
        var primitiveOffset = result.PrimitiveCount;
        var combined = new Geometry
                           {
                               Positions = new Vector3[pointOffset + _points.Count],
                               PrimitiveVertexOffsets = new int[primitiveOffset + _primitives.Count + 1],
                               VertexPoints = new int[vertexOffset + _vertices.Count],
                               PrimitiveKind = new PrimitiveKinds[primitiveOffset + _primitives.Count],
                               PrimitiveClosed = new bool[primitiveOffset + _primitives.Count],
                               Parts = result.Parts,
                           };

        Array.Copy(result.Positions, combined.Positions, pointOffset);
        for (var index = 0; index < _points.Count; index++)
            combined.Positions[pointOffset + index] = source.Positions[_points[index]];

        Array.Copy(result.PrimitiveVertexOffsets, combined.PrimitiveVertexOffsets, primitiveOffset);
        Array.Copy(result.VertexPoints, combined.VertexPoints, vertexOffset);
        Array.Copy(result.PrimitiveKind, combined.PrimitiveKind, primitiveOffset);
        Array.Copy(result.PrimitiveClosed, combined.PrimitiveClosed, primitiveOffset);

        var vertex = vertexOffset;
        for (var index = 0; index < _primitives.Count; index++)
        {
            var primitive = _primitives[index];
            combined.PrimitiveVertexOffsets[primitiveOffset + index] = vertex;
            combined.PrimitiveKind[primitiveOffset + index] = source.PrimitiveKind[primitive];
            combined.PrimitiveClosed[primitiveOffset + index] = source.PrimitiveClosed[primitive];
            for (var sourceVertex = source.PrimitiveVertexOffsets[primitive]; sourceVertex < source.PrimitiveVertexOffsets[primitive + 1]; sourceVertex++)
                combined.VertexPoints[vertex++] = pointOffset + _pointMap[source.VertexPoints[sourceVertex]];
        }

        combined.PrimitiveVertexOffsets[^1] = vertex;
        CombineAttributes(result, source, combined);
        combined.InvalidateTopologyCaches();

        _combined = combined;
        _lastResult = result;
        _lastResultVersion = result.Version;
        _lastSource = source;
        _lastSourceVersion = source.Version;
        _lastSelection = selection;
        return combined;
    }

    /** The selected primitives, their vertices and their points (each once, in first-use order) as source indices. */
    private void CollectPrimitives(Geometry source, Selections selection)
    {
        _primitives.Clear();
        _vertices.Clear();
        _points.Clear();
        if (_pointMap.Length < source.PointCount)
            _pointMap = new int[source.PointCount];

        Array.Fill(_pointMap, -1, 0, source.PointCount);
        for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
        {
            var isCurve = source.PrimitiveKind[primitive] == PrimitiveKinds.Curve;
            var isSelected = selection == Selections.Curves
                                 ? isCurve
                                 : !isCurve || !source.PrimitiveClosed[primitive];
            if (!isSelected)
                continue;

            _primitives.Add(primitive);
            for (var vertex = source.PrimitiveVertexOffsets[primitive]; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
            {
                _vertices.Add(vertex);
                var point = source.VertexPoints[vertex];
                if (_pointMap[point] >= 0)
                    continue;

                _pointMap[point] = _points.Count;
                _points.Add(point);
            }
        }
    }

    private void CombineAttributes(Geometry result, Geometry source, Geometry combined)
    {
        for (var index = 0; index < result.Attributes.Count; index++)
        {
            var attribute = result.Attributes[index];
            var sourceIndices = GetSourceIndices(attribute.Domain);
            if (sourceIndices == null)
            {
                // Parts are unchanged; edges belong to the polygons only
                combined.Attributes.Add(attribute);
                continue;
            }

            var match = Find(source.Attributes, attribute.Name, attribute.Domain, attribute.ValueType);
            combined.Attributes.Add(Concat(attribute, GetCount(result, attribute.Domain), match, sourceIndices));
        }

        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            var sourceIndices = GetSourceIndices(attribute.Domain);
            if (sourceIndices == null || Find(result.Attributes, attribute.Name, attribute.Domain, null) != null)
                continue;

            combined.Attributes.Add(Concat(attribute, GetCount(result, attribute.Domain), attribute, sourceIndices, firstIsEmpty: true));
        }
    }

    private List<int>? GetSourceIndices(AttributeDomain domain)
    {
        return domain switch
                   {
                       AttributeDomain.Point     => _points,
                       AttributeDomain.Vertex    => _vertices,
                       AttributeDomain.Primitive => _primitives,
                       _                         => null,
                   };
    }

    private static int GetCount(Geometry geometry, AttributeDomain domain)
    {
        return domain switch
                   {
                       AttributeDomain.Point  => geometry.PointCount,
                       AttributeDomain.Vertex => geometry.VertexCount,
                       _                      => geometry.PrimitiveCount,
                   };
    }

    /// <summary>The first <paramref name="firstCount"/> values from <paramref name="first"/> (zeros when it is empty), then the source's values at the curve indices (zeros without a match).</summary>
    private static GeometryAttribute Concat(GeometryAttribute first, int firstCount, GeometryAttribute? second, List<int> indices, bool firstIsEmpty = false)
    {
        return first switch
                   {
                       GeometryAttribute<float> a   => Concat(a, firstCount, second as GeometryAttribute<float>, indices, firstIsEmpty),
                       GeometryAttribute<int> a     => Concat(a, firstCount, second as GeometryAttribute<int>, indices, firstIsEmpty),
                       GeometryAttribute<Vector2> a => Concat(a, firstCount, second as GeometryAttribute<Vector2>, indices, firstIsEmpty),
                       GeometryAttribute<Vector3> a => Concat(a, firstCount, second as GeometryAttribute<Vector3>, indices, firstIsEmpty),
                       GeometryAttribute<Vector4> a => Concat(a, firstCount, second as GeometryAttribute<Vector4>, indices, firstIsEmpty),
                       _                            => first,
                   };
    }

    private static GeometryAttribute<T> Concat<T>(GeometryAttribute<T> first, int firstCount, GeometryAttribute<T>? second, List<int> indices, bool firstIsEmpty)
        where T : unmanaged
    {
        var combined = new GeometryAttribute<T>(first.Name, first.Domain, firstCount + indices.Count, first.TypeInfo);
        if (!firstIsEmpty)
            Array.Copy(first.Values, combined.Values, Math.Min(firstCount, first.Values.Length));

        if (second == null)
            return combined;

        for (var index = 0; index < indices.Count; index++)
            combined.Values[firstCount + index] = second.Values[indices[index]];

        return combined;
    }

    private static GeometryAttribute? Find(GeometryAttributes attributes, string name, AttributeDomain domain, Type? valueType)
    {
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain == domain
                && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase)
                && (valueType == null || attribute.ValueType == valueType))
                return attribute;
        }

        return null;
    }

    private readonly List<int> _primitives = [];
    private readonly List<int> _vertices = [];
    private readonly List<int> _points = [];
    private int[] _pointMap = [];
    private Geometry? _combined;
    private Geometry? _lastResult;
    private int _lastResultVersion;
    private Geometry? _lastSource;
    private int _lastSourceVersion;
    private Selections _lastSelection;
}

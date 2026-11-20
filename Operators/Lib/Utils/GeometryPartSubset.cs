#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Rebuilds a Geometry from a subset of its parts: faces, points and every
/// attribute domain are compacted and remapped, part ranges renumbered. Shared by
/// the part-filter ops. Reuse one instance per op - it keeps its scratch buffers.
/// </summary>
internal sealed class GeometryPartSubset
{
    /// <summary>Builds <paramref name="target"/> from the given parts of <paramref name="source"/>.</summary>
    public void Build(Geometry source, List<PrimitivePart> keptParts, Geometry target)
    {
        var offsets = source.PrimitiveVertexOffsets;
        var corners = source.VertexPoints;

        if (_pointRemap.Length != source.PointCount)
            _pointRemap = new int[source.PointCount];
        Array.Fill(_pointRemap, -1);

        _faceList.Clear();
        _cornerList.Clear();
        _pointList.Clear();
        _outFaceOffsets.Clear();
        _outCorners.Clear();
        _outFaceOffsets.Add(0);
        var outParts = new PrimitivePart[keptParts.Count];

        for (var partIndex = 0; partIndex < keptParts.Count; partIndex++)
        {
            var part = keptParts[partIndex];
            var faceStart = _outFaceOffsets.Count - 1;
            var faceEnd = Math.Min(part.PrimitiveStart + part.PrimitiveCount, source.PrimitiveCount);
            for (var faceIndex = part.PrimitiveStart; faceIndex < faceEnd; faceIndex++)
            {
                _faceList.Add(faceIndex);
                for (var c = offsets[faceIndex]; c < offsets[faceIndex + 1]; c++)
                {
                    var pointId = corners[c];
                    if (_pointRemap[pointId] < 0)
                    {
                        _pointRemap[pointId] = _pointList.Count;
                        _pointList.Add(pointId);
                    }

                    _cornerList.Add(c);
                    _outCorners.Add(_pointRemap[pointId]);
                }

                _outFaceOffsets.Add(_outCorners.Count);
            }

            outParts[partIndex] = part with { PrimitiveStart = faceStart, PrimitiveCount = _outFaceOffsets.Count - 1 - faceStart };
        }

        var positions = new Vector3[_pointList.Count];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = source.Positions[_pointList[i]];
        }

        target.Positions = positions;
        target.PrimitiveVertexOffsets = _outFaceOffsets.ToArray();
        var kinds = new PrimitiveKinds[_faceList.Count];
        var closed = new bool[_faceList.Count];
        for (var i = 0; i < _faceList.Count; i++)
        {
            kinds[i] = source.PrimitiveKind[_faceList[i]];
            closed[i] = source.PrimitiveClosed[_faceList[i]];
        }

        target.PrimitiveKind = kinds;
        target.PrimitiveClosed = closed;
        target.VertexPoints = _outCorners.ToArray();
        target.Parts = outParts;

        // Every attribute domain is a subset of the source's - remap by the index lists
        target.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            var indices = attribute.Domain switch
                              {
                                  AttributeDomain.Vertex => _cornerList,
                                  AttributeDomain.Primitive   => _faceList,
                                  AttributeDomain.Point  => _pointList,
                                  _                      => null,
                              };
            if (indices == null)
                continue;

            switch (attribute)
            {
                case GeometryAttribute<float> a:
                    Remap(a.Values, target.Attributes.GetOrCreate<float>(a.Name, a.Domain, indices.Count).Values, indices);
                    break;
                case GeometryAttribute<int> a:
                    Remap(a.Values, target.Attributes.GetOrCreate<int>(a.Name, a.Domain, indices.Count).Values, indices);
                    break;
                case GeometryAttribute<Vector2> a:
                    Remap(a.Values, target.Attributes.GetOrCreate<Vector2>(a.Name, a.Domain, indices.Count).Values, indices);
                    break;
                case GeometryAttribute<Vector3> a:
                    Remap(a.Values, target.Attributes.GetOrCreate<Vector3>(a.Name, a.Domain, indices.Count).Values, indices);
                    break;
                case GeometryAttribute<Vector4> a:
                    Remap(a.Values, target.Attributes.GetOrCreate<Vector4>(a.Name, a.Domain, indices.Count).Values, indices);
                    break;
            }
        }

        target.InvalidateTopologyCaches();
    }

    /// <summary>Part list of a geometry, where part-less geometry counts as one part pivoting on its bounds center.</summary>
    public PrimitivePart[] PartsOrWhole(Geometry source)
    {
        if (source.Parts.Length > 0)
            return source.Parts;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in source.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        _implicitPart[0] = new PrimitivePart(0, source.PrimitiveCount, (min + max) * 0.5f, 0, 0);
        return _implicitPart;
    }

    private static void Remap<T>(T[] source, T[] target, List<int> indices) where T : unmanaged
    {
        for (var i = 0; i < indices.Count; i++)
        {
            target[i] = source[indices[i]];
        }
    }

    private readonly PrimitivePart[] _implicitPart = new PrimitivePart[1];
    private readonly List<int> _faceList = [];
    private readonly List<int> _cornerList = [];
    private readonly List<int> _pointList = [];
    private readonly List<int> _outFaceOffsets = [];
    private readonly List<int> _outCorners = [];
    private int[] _pointRemap = [];
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Rebuilds a MeshGeometry from a subset of its parts: faces, points and every
/// attribute domain are compacted and remapped, part ranges renumbered. Shared by
/// the part-filter ops. Reuse one instance per op - it keeps its scratch buffers.
/// </summary>
internal sealed class GeometryPartSubset
{
    /// <summary>Builds <paramref name="target"/> from the given parts of <paramref name="source"/>.</summary>
    public void Build(MeshGeometry source, List<GeometryPart> keptParts, MeshGeometry target)
    {
        var offsets = source.FaceCornerOffsets;
        var corners = source.CornerPointIndices;

        if (_pointRemap.Length != source.PointCount)
            _pointRemap = new int[source.PointCount];
        Array.Fill(_pointRemap, -1);

        _faceList.Clear();
        _cornerList.Clear();
        _pointList.Clear();
        _outFaceOffsets.Clear();
        _outCorners.Clear();
        _outFaceOffsets.Add(0);
        var outParts = new GeometryPart[keptParts.Count];

        for (var partIndex = 0; partIndex < keptParts.Count; partIndex++)
        {
            var part = keptParts[partIndex];
            var faceStart = _outFaceOffsets.Count - 1;
            var faceEnd = Math.Min(part.FaceStart + part.FaceCount, source.FaceCount);
            for (var faceIndex = part.FaceStart; faceIndex < faceEnd; faceIndex++)
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

            outParts[partIndex] = part with { FaceStart = faceStart, FaceCount = _outFaceOffsets.Count - 1 - faceStart };
        }

        var positions = new Vector3[_pointList.Count];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = source.Positions[_pointList[i]];
        }

        target.Positions = positions;
        target.FaceCornerOffsets = _outFaceOffsets.ToArray();
        target.CornerPointIndices = _outCorners.ToArray();
        target.Parts = outParts;

        // Every attribute domain is a subset of the source's - remap by the index lists
        target.Attributes.Clear();
        foreach (var attribute in source.Attributes)
        {
            var indices = attribute.Domain switch
                              {
                                  AttributeDomain.Corner => _cornerList,
                                  AttributeDomain.Face   => _faceList,
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
    public GeometryPart[] PartsOrWhole(MeshGeometry source)
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

        _implicitPart[0] = new GeometryPart(0, source.FaceCount, (min + max) * 0.5f, 0, 0);
        return _implicitPart;
    }

    private static void Remap<T>(T[] source, T[] target, List<int> indices) where T : unmanaged
    {
        for (var i = 0; i < indices.Count; i++)
        {
            target[i] = source[indices[i]];
        }
    }

    private readonly GeometryPart[] _implicitPart = new GeometryPart[1];
    private readonly List<int> _faceList = [];
    private readonly List<int> _cornerList = [];
    private readonly List<int> _pointList = [];
    private readonly List<int> _outFaceOffsets = [];
    private readonly List<int> _outCorners = [];
    private int[] _pointRemap = [];
}

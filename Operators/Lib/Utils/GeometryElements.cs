#nullable enable
using System;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>Counts, positions and selection weights of the elements of one <see cref="Geometry"/> domain.</summary>
internal static class GeometryElements
{
    public static int GetCount(Geometry geometry, AttributeDomain domain)
    {
        return domain switch
                   {
                       AttributeDomain.Point     => geometry.PointCount,
                       AttributeDomain.Vertex    => geometry.VertexCount,
                       AttributeDomain.Primitive => geometry.PrimitiveCount,
                       AttributeDomain.Part      => geometry.Parts.Length,
                       _                         => 0,
                   };
    }

    /// <summary>Points: their position. Vertices: their point's. Primitives: the mean of their points. Parts: the pivot.</summary>
    public static Vector3 GetPosition(Geometry geometry, AttributeDomain domain, int index)
    {
        switch (domain)
        {
            case AttributeDomain.Point:
                return geometry.Positions[index];
            case AttributeDomain.Vertex:
                return geometry.Positions[geometry.VertexPoints[index]];
            case AttributeDomain.Primitive:
                var start = geometry.PrimitiveVertexOffsets[index];
                var end = geometry.PrimitiveVertexOffsets[index + 1];
                if (end == start)
                    return Vector3.Zero;

                var sum = Vector3.Zero;
                for (var vertex = start; vertex < end; vertex++)
                    sum += geometry.Positions[geometry.VertexPoints[vertex]];

                return sum / (end - start);
            default:
                return geometry.Parts[index].Pivot;
        }
    }

    /// <summary>
    /// One weight per element of <paramref name="domain"/> from the named float attribute: taken directly when it is
    /// on the same domain, otherwise through its point weights (an element takes the largest weight of its points).
    /// </summary>
    /// <param name="pointWeights">Scratch for the point weights, at least PointCount long.</param>
    /// <returns>False if no such attribute exists; the weights are then all zero.</returns>
    public static bool TryGetWeights(Geometry geometry, AttributeDomain domain, string name, float[] weights, float[] pointWeights)
    {
        var count = GetCount(geometry, domain);
        Array.Clear(weights, 0, count);
        if (geometry.Attributes.TryGet<float>(name, domain, out var direct))
        {
            Array.Copy(direct.Values, weights, Math.Min(count, direct.Values.Length));
            return true;
        }

        if (!geometry.TryGetPointWeights(name, pointWeights))
            return false;

        switch (domain)
        {
            case AttributeDomain.Point:
                Array.Copy(pointWeights, weights, count);
                break;
            case AttributeDomain.Vertex:
                for (var vertex = 0; vertex < count; vertex++)
                    weights[vertex] = pointWeights[geometry.VertexPoints[vertex]];

                break;
            case AttributeDomain.Primitive:
                for (var primitive = 0; primitive < count; primitive++)
                    weights[primitive] = GetPrimitiveWeight(geometry, primitive, pointWeights);

                break;
            case AttributeDomain.Part:
                for (var part = 0; part < count; part++)
                {
                    var range = geometry.Parts[part];
                    for (var primitive = range.PrimitiveStart; primitive < range.PrimitiveStart + range.PrimitiveCount; primitive++)
                        weights[part] = MathF.Max(weights[part], GetPrimitiveWeight(geometry, primitive, pointWeights));
                }

                break;
        }

        return true;
    }

    private static float GetPrimitiveWeight(Geometry geometry, int primitive, float[] pointWeights)
    {
        var weight = 0f;
        for (var vertex = geometry.PrimitiveVertexOffsets[primitive]; vertex < geometry.PrimitiveVertexOffsets[primitive + 1]; vertex++)
            weight = MathF.Max(weight, pointWeights[geometry.VertexPoints[vertex]]);

        return weight;
    }
}

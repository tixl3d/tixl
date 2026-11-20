#nullable enable
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Gives ops that only handle polygons a Geometry without its curve primitives. Geometry without curves is passed
/// through as is. Otherwise points and point and part attributes are shared, vertex and primitive attributes are
/// filtered to the polygons, part ranges are renumbered and edge attributes dropped. The filtered geometry is a new
/// instance, so ops that compute asynchronously can keep using it. Reuse one filter per op.
/// </summary>
internal sealed class GeometryPolygonFilter
{
    public Geometry GetPolygons(Geometry source)
    {
        if (!source.HasCurves())
            return source;

        _vertexBlend.Clear();
        _primitiveBlend.Clear();
        var primitiveCount = source.PrimitiveCount;
        if (_polygonsBefore.Length < primitiveCount + 1)
            _polygonsBefore = new int[primitiveCount + 1];

        var polygonCount = 0;
        var vertexCount = 0;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            _polygonsBefore[primitive] = polygonCount;
            if (source.PrimitiveKind[primitive] != PrimitiveKinds.Polygon)
                continue;

            polygonCount++;
            vertexCount += source.GetPrimitiveVertexCount(primitive);
        }

        _polygonsBefore[primitiveCount] = polygonCount;

        // A new instance each time: async ops may still read the previous one on another thread
        var target = new Geometry
                         {
                             PrimitiveVertexOffsets = new int[polygonCount + 1],
                             VertexPoints = new int[vertexCount],
                             Parts = new PrimitivePart[source.Parts.Length],
                         };

        var outPrimitive = 0;
        var outVertex = 0;
        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            if (source.PrimitiveKind[primitive] != PrimitiveKinds.Polygon)
                continue;

            target.PrimitiveVertexOffsets[outPrimitive] = outVertex;
            _primitiveBlend.AddCopy(primitive);
            for (var vertex = source.PrimitiveVertexOffsets[primitive]; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
            {
                target.VertexPoints[outVertex++] = source.VertexPoints[vertex];
                _vertexBlend.AddCopy(vertex);
            }

            outPrimitive++;
        }

        target.PrimitiveVertexOffsets[polygonCount] = outVertex;
        target.Positions = source.Positions;
        target.SetAllPrimitiveKinds(PrimitiveKinds.Polygon);

        for (var part = 0; part < source.Parts.Length; part++)
        {
            var range = source.Parts[part];
            var start = _polygonsBefore[Math.Min(range.PrimitiveStart, primitiveCount)];
            var end = _polygonsBefore[Math.Min(range.PrimitiveStart + range.PrimitiveCount, primitiveCount)];
            target.Parts[part] = range with { PrimitiveStart = start, PrimitiveCount = end - start };
        }

        target.Attributes.Clear();
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain is AttributeDomain.Point or AttributeDomain.Part)
                target.Attributes.Add(attribute);
        }

        _vertexBlend.Apply(source.Attributes, target.Attributes, AttributeDomain.Vertex);
        _primitiveBlend.Apply(source.Attributes, target.Attributes, AttributeDomain.Primitive);
        target.InvalidateTopologyCaches();
        return target;
    }

    private readonly GeometryAttributeBlend _vertexBlend = new();
    private readonly GeometryAttributeBlend _primitiveBlend = new();
    private int[] _polygonsBefore = [];
}

#nullable enable
namespace Lib.geometry.modify;

/// <summary>
/// Appends any number of geometries into one, keeping their points, primitives and point, vertex, primitive and part
/// attributes. Each input keeps its parts; an input without parts becomes one part. Attributes missing on some inputs
/// are zero there. Edge attributes are dropped, since edges are rebuilt from the combined topology.
/// </summary>
[Guid("1e43e19f-8fcd-40e7-8e02-928dd1d0df73")]
internal sealed class CombineGeometry : Instance<CombineGeometry>
{
    [Output(Guid = "b8b6b0ff-7d5c-4daa-ba8e-8642e000b882")]
    public readonly Slot<Geometry?> Result = new();

    public CombineGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _sources.Clear();
        var inputs = Geometries.GetCollectedTypedInputs();
        for (var index = 0; index < inputs.Count; index++)
        {
            var geometry = inputs[index].GetValue(context);
            if (geometry != null && geometry.PointCount > 0)
                _sources.Add(geometry);
        }

        Geometries.DirtyFlag.Clear();

        if (_sources.Count == 0)
        {
            Result.Value = null;
            return;
        }

        // Geometry is immutable in the graph, so a single input can pass through unchanged.
        if (_sources.Count == 1)
        {
            Result.Value = _sources[0];
            return;
        }

        CombineTopology();
        CombineParts();
        CombineAttributes();
        if (HasStaleAttributes())
        {
            _output.Attributes.Clear();
            CombineAttributes();
        }

        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    private void CombineTopology()
    {
        int pointCount = 0, primitiveCount = 0, vertexCount = 0;
        foreach (var source in _sources)
        {
            pointCount += source.PointCount;
            primitiveCount += source.PrimitiveCount;
            vertexCount += source.VertexCount;
        }

        _output.Resize(pointCount, primitiveCount, vertexCount);

        int pointStart = 0, primitiveStart = 0, vertexStart = 0;
        foreach (var source in _sources)
        {
            Array.Copy(source.Positions, 0, _output.Positions, pointStart, source.PointCount);
            Array.Copy(source.PrimitiveKind, 0, _output.PrimitiveKind, primitiveStart, source.PrimitiveCount);
            Array.Copy(source.PrimitiveClosed, 0, _output.PrimitiveClosed, primitiveStart, source.PrimitiveCount);
            for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
                _output.PrimitiveVertexOffsets[primitiveStart + primitive] = source.PrimitiveVertexOffsets[primitive] + vertexStart;

            for (var vertex = 0; vertex < source.VertexCount; vertex++)
                _output.VertexPoints[vertexStart + vertex] = source.VertexPoints[vertex] + pointStart;

            pointStart += source.PointCount;
            primitiveStart += source.PrimitiveCount;
            vertexStart += source.VertexCount;
        }

        _output.PrimitiveVertexOffsets[primitiveCount] = vertexCount;
    }

    /// <summary>
    /// Ids and seed indices are renumbered in output order, because each input counts its parts from zero and
    /// per-part variations would otherwise repeat.
    /// </summary>
    private void CombineParts()
    {
        var partCount = 0;
        foreach (var source in _sources)
            partCount += GetPartCount(source);

        if (_output.Parts.Length != partCount)
            _output.Parts = new PrimitivePart[partCount];

        var partIndex = 0;
        var primitiveStart = 0;
        foreach (var source in _sources)
        {
            if (source.Parts.Length > 0)
            {
                foreach (var part in source.Parts)
                {
                    _output.Parts[partIndex] = new PrimitivePart(part.PrimitiveStart + primitiveStart, part.PrimitiveCount, part.Pivot, partIndex, partIndex);
                    partIndex++;
                }
            }
            else if (source.PrimitiveCount > 0)
            {
                _output.Parts[partIndex] = new PrimitivePart(primitiveStart, source.PrimitiveCount, GetBoundsCenter(source), partIndex, partIndex);
                partIndex++;
            }

            primitiveStart += source.PrimitiveCount;
        }
    }

    private void CombineAttributes()
    {
        for (var sourceIndex = 0; sourceIndex < _sources.Count; sourceIndex++)
        {
            var attributes = _sources[sourceIndex].Attributes;
            for (var attributeIndex = 0; attributeIndex < attributes.Count; attributeIndex++)
            {
                var attribute = attributes[attributeIndex];
                if (attribute.Domain is not (AttributeDomain.Point or AttributeDomain.Vertex or AttributeDomain.Primitive or AttributeDomain.Part))
                    continue;

                if (IsDefinedByEarlierSource(sourceIndex, attribute))
                    continue;

                switch (attribute)
                {
                    case GeometryAttribute<float> floats:
                        CombineAttribute(floats);
                        break;
                    case GeometryAttribute<int> ints:
                        CombineAttribute(ints);
                        break;
                    case GeometryAttribute<Vector2> vectors2:
                        CombineAttribute(vectors2);
                        break;
                    case GeometryAttribute<Vector3> vectors3:
                        CombineAttribute(vectors3);
                        break;
                    case GeometryAttribute<Vector4> vectors4:
                        CombineAttribute(vectors4);
                        break;
                }
            }
        }
    }

    /** The first input that has an attribute decides its type; same-named attributes of another type stay zero. */
    private bool IsDefinedByEarlierSource(int sourceIndex, GeometryAttribute attribute)
    {
        for (var earlier = 0; earlier < sourceIndex; earlier++)
        {
            if (FindAttribute(_sources[earlier].Attributes, attribute.Name, attribute.Domain) != null)
                return true;
        }

        return false;
    }

    private void CombineAttribute<T>(GeometryAttribute<T> first) where T : unmanaged
    {
        var domain = first.Domain;
        var target = _output.Attributes.GetOrCreate<T>(first.Name, domain, GetElementCount(_output, domain), first.TypeInfo).Values;
        Array.Clear(target);
        var start = 0;
        foreach (var source in _sources)
        {
            var count = GetElementCount(source, domain);
            if (source.Attributes.TryGet<T>(first.Name, domain, out var values))
                Array.Copy(values.Values, 0, target, start, Math.Min(count, values.Values.Length));

            start += count;
        }
    }

    /** True when the output still holds an attribute from an earlier update that no input provides any more. */
    private bool HasStaleAttributes()
    {
        var attributes = _output.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            var isProvided = false;
            foreach (var source in _sources)
            {
                var match = FindAttribute(source.Attributes, attribute.Name, attribute.Domain);
                if (match == null)
                    continue;

                isProvided = match.ValueType == attribute.ValueType;
                break;
            }

            if (!isProvided)
                return true;
        }

        return false;
    }

    private static GeometryAttribute? FindAttribute(GeometryAttributes attributes, string name, AttributeDomain domain)
    {
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain == domain && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
                return attribute;
        }

        return null;
    }

    private static int GetElementCount(Geometry geometry, AttributeDomain domain)
    {
        return domain switch
                   {
                       AttributeDomain.Point     => geometry.PointCount,
                       AttributeDomain.Vertex    => geometry.VertexCount,
                       AttributeDomain.Primitive => geometry.PrimitiveCount,
                       AttributeDomain.Part      => GetPartCount(geometry),
                       _                         => 0,
                   };
    }

    private static int GetPartCount(Geometry geometry)
    {
        return geometry.Parts.Length > 0 ? geometry.Parts.Length : geometry.PrimitiveCount > 0 ? 1 : 0;
    }

    private static Vector3 GetBoundsCenter(Geometry geometry)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in geometry.Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return (min + max) * 0.5f;
    }

    private readonly Geometry _output = new();
    private readonly List<Geometry> _sources = [];

    [Input(Guid = "c0340fa2-bf70-4cd2-9779-32db528c6ef3")]
    public readonly MultiInputSlot<Geometry> Geometries = new();
}

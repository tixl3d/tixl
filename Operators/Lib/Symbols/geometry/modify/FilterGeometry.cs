#nullable enable
using Lib.Utils;

namespace Lib.geometry.modify;

/// <summary>
/// Keeps the points, primitives or parts whose float attribute passes a threshold, like Houdini's Blast with the
/// test inverted: a primitive stays only with all its points, and kept points that lose their primitives stay as loose
/// points. Parts are rebuilt from the kept primitives; attributes of points, vertices, primitives and parts are kept
/// for the kept elements, edge attributes are dropped.
/// </summary>
[Guid("c8554f60-1c63-4d29-bea0-9f243e2044f9")]
internal sealed class FilterGeometry : Instance<FilterGeometry>, IStatusProvider, ICustomDropdownHolder
{
    private enum Domains
    {
        Auto,
        Points,
        Primitives,
        Parts,
    }

    private enum MissingModes
    {
        KeepAll,
        RemoveAll,
    }

    [Output(Guid = "5f31df04-58f6-4029-b722-3666068d39ea")]
    public readonly Slot<Geometry?> Result = new();

    public FilterGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var domainChoice = (Domains)Math.Clamp(Domain.GetValue(context), 0, 3);
        var name = Attribute.GetValue(context);
        var threshold = Threshold.GetValue(context);
        var useAbsolute = UseAbsolute.GetValue(context);
        var invert = Invert.GetValue(context);
        var whenMissing = (MissingModes)Math.Clamp(WhenMissing.GetValue(context), 0, 1);
        _statusMessage = string.Empty;
        _lastSource = source;
        if (source == null)
        {
            Result.Value = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(name) || !TryFindWeights(source, name, domainChoice, out var weights, out var domain))
        {
            // Removing everything is a deliberate choice (e.g. showing a selection only when there is one), so no warning
            if (whenMissing == MissingModes.RemoveAll)
            {
                Result.Value = _empty;
                return;
            }

            _statusMessage = $"No float attribute '{name}'; nothing is removed.";
            Result.Value = source;
            return;
        }

        var pointCount = source.PointCount;
        var primitiveCount = source.PrimitiveCount;
        EnsureLength(ref _keepPoint, pointCount);
        EnsureLength(ref _keepPrimitive, primitiveCount);
        Array.Fill(_keepPoint, domain == AttributeDomain.Point ? false : true, 0, pointCount);
        Array.Fill(_keepPrimitive, true, 0, primitiveCount);

        switch (domain)
        {
            case AttributeDomain.Point:
                for (var point = 0; point < pointCount; point++)
                    _keepPoint[point] = Passes(Read(weights, point), threshold, useAbsolute, invert);

                // A primitive needs all its points
                for (var primitive = 0; primitive < primitiveCount; primitive++)
                {
                    for (var vertex = source.PrimitiveVertexOffsets[primitive]; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
                    {
                        if (_keepPoint[source.VertexPoints[vertex]])
                            continue;

                        _keepPrimitive[primitive] = false;
                        break;
                    }
                }

                break;
            case AttributeDomain.Vertex:
                // A primitive stays when any of its vertices passes
                for (var primitive = 0; primitive < primitiveCount; primitive++)
                {
                    var keep = false;
                    for (var vertex = source.PrimitiveVertexOffsets[primitive]; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
                        keep |= Passes(Read(weights, vertex), threshold, useAbsolute, invert);

                    _keepPrimitive[primitive] = keep;
                }

                break;
            case AttributeDomain.Primitive:
                for (var primitive = 0; primitive < primitiveCount; primitive++)
                    _keepPrimitive[primitive] = Passes(Read(weights, primitive), threshold, useAbsolute, invert);

                break;
            case AttributeDomain.Part:
                Array.Fill(_keepPrimitive, false, 0, primitiveCount);
                for (var part = 0; part < source.Parts.Length; part++)
                {
                    if (!Passes(Read(weights, part), threshold, useAbsolute, invert))
                        continue;

                    var range = source.Parts[part];
                    for (var primitive = range.PrimitiveStart; primitive < Math.Min(range.PrimitiveStart + range.PrimitiveCount, primitiveCount); primitive++)
                        _keepPrimitive[primitive] = true;
                }

                break;
        }

        // Points of kept primitives stay; with a point attribute, so do kept loose points
        if (domain != AttributeDomain.Point)
            Array.Fill(_keepPoint, false, 0, pointCount);

        for (var primitive = 0; primitive < primitiveCount; primitive++)
        {
            if (!_keepPrimitive[primitive])
                continue;

            for (var vertex = source.PrimitiveVertexOffsets[primitive]; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
                _keepPoint[source.VertexPoints[vertex]] = true;
        }

        Result.Value = Build(source);
    }

    private static bool Passes(float weight, float threshold, bool useAbsolute, bool invert)
    {
        return ((useAbsolute ? MathF.Abs(weight) : weight) > threshold) != invert;
    }

    private static float Read(GeometryAttribute attribute, int index)
    {
        return attribute switch
                   {
                       GeometryAttribute<float> floats when index < floats.Values.Length => floats.Values[index],
                       GeometryAttribute<int> ints when index < ints.Values.Length       => ints.Values[index],
                       _                                                                 => 0,
                   };
    }

    /** The named float or int attribute on the chosen domain, or with Auto on its own domain (points first). */
    private static bool TryFindWeights(Geometry geometry, string name, Domains choice, out GeometryAttribute attribute, out AttributeDomain domain)
    {
        foreach (var candidate in Candidates)
        {
            if (choice != Domains.Auto && candidate != ToAttributeDomain(choice))
                continue;

            for (var index = 0; index < geometry.Attributes.Count; index++)
            {
                var a = geometry.Attributes[index];
                if (a.Domain == candidate && a is GeometryAttribute<float> or GeometryAttribute<int>
                                          && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    attribute = a;
                    domain = candidate;
                    return true;
                }
            }
        }

        attribute = null!;
        domain = AttributeDomain.Point;
        return false;
    }

    private static AttributeDomain ToAttributeDomain(Domains domain)
    {
        return domain switch
                   {
                       Domains.Primitives => AttributeDomain.Primitive,
                       Domains.Parts      => AttributeDomain.Part,
                       _                  => AttributeDomain.Point,
                   };
    }

    /** A new geometry of the kept elements; new each time, so async consumers keep a valid one. */
    private Geometry Build(Geometry source)
    {
        _pointStencil.Clear();
        _vertexStencil.Clear();
        _primitiveStencil.Clear();
        _partStencil.Clear();
        EnsureLength(ref _pointMap, source.PointCount);
        var keptPoints = 0;
        for (var point = 0; point < source.PointCount; point++)
        {
            _pointMap[point] = _keepPoint[point] ? keptPoints++ : -1;
            if (_keepPoint[point])
                _pointStencil.AddCopy(point);
        }

        var keptPrimitives = 0;
        var keptVertices = 0;
        for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
        {
            if (!_keepPrimitive[primitive])
                continue;

            keptPrimitives++;
            keptVertices += source.PrimitiveVertexOffsets[primitive + 1] - source.PrimitiveVertexOffsets[primitive];
        }

        var result = new Geometry();
        result.Resize(keptPoints, keptPrimitives, keptVertices);
        for (var point = 0; point < source.PointCount; point++)
        {
            if (_pointMap[point] >= 0)
                result.Positions[_pointMap[point]] = source.Positions[point];
        }

        EnsureLength(ref _primitiveMap, source.PrimitiveCount + 1);
        var outPrimitive = 0;
        var outVertex = 0;
        for (var primitive = 0; primitive < source.PrimitiveCount; primitive++)
        {
            _primitiveMap[primitive] = outPrimitive;
            if (!_keepPrimitive[primitive])
                continue;

            result.PrimitiveVertexOffsets[outPrimitive] = outVertex;
            result.PrimitiveKind[outPrimitive] = source.PrimitiveKind[primitive];
            result.PrimitiveClosed[outPrimitive] = source.PrimitiveClosed[primitive];
            _primitiveStencil.AddCopy(primitive);
            for (var vertex = source.PrimitiveVertexOffsets[primitive]; vertex < source.PrimitiveVertexOffsets[primitive + 1]; vertex++)
            {
                result.VertexPoints[outVertex++] = _pointMap[source.VertexPoints[vertex]];
                _vertexStencil.AddCopy(vertex);
            }

            outPrimitive++;
        }

        _primitiveMap[source.PrimitiveCount] = outPrimitive;
        result.PrimitiveVertexOffsets[keptPrimitives] = keptVertices;

        // Parts keep their kept primitives; empty ones are dropped
        _parts.Clear();
        for (var part = 0; part < source.Parts.Length; part++)
        {
            var range = source.Parts[part];
            var start = _primitiveMap[Math.Min(range.PrimitiveStart, source.PrimitiveCount)];
            var end = _primitiveMap[Math.Min(range.PrimitiveStart + range.PrimitiveCount, source.PrimitiveCount)];
            if (end <= start)
                continue;

            _parts.Add(range with { PrimitiveStart = start, PrimitiveCount = end - start });
            _partStencil.AddCopy(part);
        }

        result.Parts = _parts.ToArray();
        _pointStencil.Apply(source.Attributes, result.Attributes, AttributeDomain.Point);
        _vertexStencil.Apply(source.Attributes, result.Attributes, AttributeDomain.Vertex);
        _primitiveStencil.Apply(source.Attributes, result.Attributes, AttributeDomain.Primitive);
        _partStencil.Apply(source.Attributes, result.Attributes, AttributeDomain.Part);
        result.InvalidateTopologyCaches();
        return result;
    }

    private static void EnsureLength<T>(ref T[] array, int length)
    {
        if (array.Length < length)
            array = new T[length];
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    #region ICustomDropdownHolder - float attributes of the last input
    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => Attribute.Value;

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) => GeometryAttributeChoices.GetAttributeNames(_lastSource, null, floatsOnly: true);

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId == Attribute.Input.InputDefinition.Id && selected != null)
            Attribute.SetTypedInputValue(selected);
    }
    #endregion

    private static readonly AttributeDomain[] Candidates = [AttributeDomain.Point, AttributeDomain.Vertex, AttributeDomain.Primitive, AttributeDomain.Part];

    private readonly GeometryAttributeStencil _pointStencil = new();
    private readonly GeometryAttributeStencil _vertexStencil = new();
    private readonly GeometryAttributeStencil _primitiveStencil = new();
    private readonly GeometryAttributeStencil _partStencil = new();
    private readonly List<PrimitivePart> _parts = [];
    private bool[] _keepPoint = [];
    private bool[] _keepPrimitive = [];
    private int[] _pointMap = [];
    private int[] _primitiveMap = [];
    private Geometry? _lastSource;
    private readonly Geometry _empty = new();
    private string _statusMessage = string.Empty;

    [Input(Guid = "561e6e45-b630-4d6a-9a90-09240385145f")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "b521b655-d7ae-4839-8cf4-c91d6f6d1bf0", MappedType = typeof(Domains))]
    public readonly InputSlot<int> Domain = new();

    [Input(Guid = "c8509292-8e2b-4af2-9c51-6b4f7c4ebe2b")]
    public readonly InputSlot<string> Attribute = new();

    [Input(Guid = "feff21f7-6bde-4566-8040-f53388b798ea")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "6832a86a-074e-4f38-9bde-0497f38ec813")]
    public readonly InputSlot<bool> UseAbsolute = new();

    [Input(Guid = "3fcdb0e5-8432-423d-9343-c6adcbc12703")]
    public readonly InputSlot<bool> Invert = new();

    [Input(Guid = "0147ca4d-7cd7-4380-a08d-8648bcde9ee8", MappedType = typeof(MissingModes))]
    public readonly InputSlot<int> WhenMissing = new();
}

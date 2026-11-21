#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry.attribute;

/// <summary>
/// Colors the points, vertices, primitives or parts of a Geometry from a gradient, driven by an attribute, the element
/// order, a random value or a scalar field, mapped like [MapPointAttributes] (Mapping, Range, Phase) and shaped by
/// gain, bias and randomization. Writes the Color attribute of that domain.
/// </summary>
[Guid("c9273935-dc39-41a8-9855-2af6264ca38a")]
internal sealed class ColorGeometry : Instance<ColorGeometry>, IStatusProvider, ICustomDropdownHolder
{
    private enum Domains
    {
        Points,
        Vertices,
        Primitives,
        Parts,

        /** The domain of the Attribute, e.g. to color a selection wherever it was made. */
        Auto,
    }

    private enum InputModes
    {
        Attribute,
        Index,
        Random,
        Field,
    }

    private enum MappingModes
    {
        Centered,
        FromStart,
        PingPong,
        Repeat,
    }

    private enum WriteModes
    {
        Replace,
        Multiply,
    }

    [Output(Guid = "fc15916c-43c0-4377-8489-fd8624850343")]
    public readonly Slot<Geometry?> Result = new();

    public ColorGeometry()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var domainChoice = (Domains)Domain.GetValue(context).Clamp(0, 4);
        var inputMode = (InputModes)InputMode.GetValue(context).Clamp(0, 3);
        var attributeName = Attribute.GetValue(context);
        var domain = domainChoice == Domains.Auto
                         ? FindAttributeDomain(source, attributeName)
                         : ToAttributeDomain(domainChoice);
        var field = Field.GetValue(context);
        var mapping = (MappingModes)Mapping.GetValue(context).Clamp(0, 3);
        var range = Range.GetValue(context);
        var phase = Phase.GetValue(context);
        var gainAndBias = GainAndBias.GetValue(context);
        var randomize = Randomize.GetValue(context);
        var seed = Seed.GetValue(context);
        var gradient = Gradient.GetValue(context);
        var writeMode = (WriteModes)WriteMode.GetValue(context).Clamp(0, 1);
        var selectionName = GeometryAttributeChoices.ToSelectionValue(Selection.GetValue(context) ?? string.Empty);
        _statusMessage = string.Empty;
        _lastSource = source;
        _lastDomain = domain;

        if (source == null || gradient == null)
        {
            Result.Value = source;
            return;
        }

        if (MathF.Abs(range) < 1e-6f)
            range = 1e-6f;

        var count = GeometryElements.GetCount(source, domain);
        if (_weights.Length < count)
            _weights = new float[count];

        if (_pointWeights.Length < source.PointCount)
            _pointWeights = new float[source.PointCount];

        // Share topology; replace only the Color of the domain
        _output.Positions = source.Positions;
        _output.PrimitiveVertexOffsets = source.PrimitiveVertexOffsets;
        _output.VertexPoints = source.VertexPoints;
        _output.PrimitiveKind = source.PrimitiveKind;
        _output.PrimitiveClosed = source.PrimitiveClosed;
        _output.Parts = source.Parts;
        _output.Attributes.Clear();
        Vector4[]? oldColors = null;
        GeometryAttribute? input = null;
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain != domain)
            {
                _output.Attributes.Add(attribute);
                continue;
            }

            if (inputMode == InputModes.Attribute && string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase))
                input = attribute;

            if (string.Equals(attribute.Name, GeometryAttributeNames.Color, StringComparison.OrdinalIgnoreCase))
            {
                oldColors = (attribute as GeometryAttribute<Vector4>)?.Values;
                continue;
            }

            _output.Attributes.Add(attribute);
        }

        if (inputMode == InputModes.Attribute && input == null)
            _statusMessage = string.IsNullOrWhiteSpace(attributeName)
                                 ? "Choose an attribute to map, or another Input Mode."
                                 : $"No attribute '{attributeName}' on this domain.";

        if (inputMode == InputModes.Field && field == null)
            _statusMessage = "Connect a scalar field.";

        // Nothing to map: leave the colors as they are instead of painting everything with the gradient's start
        if (_statusMessage.Length > 0)
        {
            Result.Value = source;
            return;
        }

        var hasSelection = CollectWeights(source, domain, selectionName, count);

        if (_colors == null || _colors.Domain != domain || _colors.Values.Length != count)
            _colors = new GeometryAttribute<Vector4>(GeometryAttributeNames.Color, domain, count);

        var colors = _colors.Values;
        for (var i = 0; i < count; i++)
        {
            var f0 = inputMode switch
                         {
                             InputModes.Index  => i / (float)Math.Max(1, count - (mapping == MappingModes.Centered ? 1 : 0)),
                             InputModes.Random => Random01(i, seed),
                             InputModes.Field  => field?.Sample(GeometryElements.GetPosition(source, domain, i)) ?? 0,
                             _                 => ReadValue(input, i),
                         };

            if (randomize != 0)
                f0 += (Random01(i, seed + 1) - 0.5f) * randomize;

            var f = Map(f0, mapping, range, phase).Clamp(0, 1).ApplyGainAndBias(gainAndBias.X, gainAndBias.Y);
            var color = gradient.Sample(f);
            var old = oldColors != null && i < oldColors.Length ? oldColors[i] : Vector4.One;
            if (writeMode == WriteModes.Multiply)
                color *= old;

            colors[i] = hasSelection ? Vector4.Lerp(old, color, _weights[i]) : color;
        }

        _output.Attributes.Add(_colors);
        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    /// <summary>The mapping of [MapPointAttributes], so both ops map a value the same way.</summary>
    private static float Map(float f0, MappingModes mapping, float range, float phase)
    {
        switch (mapping)
        {
            case MappingModes.FromStart:
                return f0 / range - phase;
            case MappingModes.PingPong:
                var pingPong = (f0 * 2 - 1 - 2 * phase * range) / range % 2;
                return MathF.Abs(pingPong - 1);
            case MappingModes.Repeat:
                var repeat = (f0 / range - 0.5f - phase) % 1;
                return repeat < 0 ? repeat + 1 : repeat;
            default:
                return (f0 - 0.5f) / range + 0.5f - phase / range;
        }
    }

    private static float ReadValue(GeometryAttribute? attribute, int index)
    {
        return attribute switch
                   {
                       GeometryAttribute<float> floats when index < floats.Values.Length => floats.Values[index],
                       GeometryAttribute<int> ints when index < ints.Values.Length       => ints.Values[index],
                       GeometryAttribute<Vector2> v2 when index < v2.Values.Length       => v2.Values[index].X,
                       GeometryAttribute<Vector3> v3 when index < v3.Values.Length       => v3.Values[index].X,
                       GeometryAttribute<Vector4> v4 when index < v4.Values.Length       => v4.Values[index].X,
                       _                                                                 => 0,
                   };
    }

    private static float Random01(int index, int seed)
    {
        return MathUtils.Hash01(MathUtils.XxHash((uint)index) ^ (uint)seed * 2654435761u);
    }

    /// <returns>False when every element is colored.</returns>
    private bool CollectWeights(Geometry source, AttributeDomain domain, string? selectionName, int count)
    {
        if (GeometryAttributeChoices.IsAll(selectionName))
            return false;

        // Without a name the flowing Selection applies when there is one, as in TransformGeometry
        var isNamed = !string.IsNullOrWhiteSpace(selectionName);
        if (GeometryElements.TryGetWeights(source, domain, isNamed ? selectionName! : GeometryAttributeNames.Selection, _weights, _pointWeights))
            return true;

        if (!isNamed)
            return false;

        _statusMessage = $"No float attribute '{selectionName}'; nothing is colored.";
        Array.Clear(_weights, 0, count);
        return true;
    }

    /** The first domain (points, vertices, primitives, parts) with an attribute of that name; points without one. */
    private static AttributeDomain FindAttributeDomain(Geometry? geometry, string? name)
    {
        if (geometry == null || string.IsNullOrWhiteSpace(name))
            return AttributeDomain.Point;

        foreach (var domain in AutoDomains)
        {
            for (var index = 0; index < geometry.Attributes.Count; index++)
            {
                var attribute = geometry.Attributes[index];
                if (attribute.Domain == domain && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
                    return domain;
            }
        }

        return AttributeDomain.Point;
    }

    private static readonly AttributeDomain[] AutoDomains = [AttributeDomain.Point, AttributeDomain.Vertex, AttributeDomain.Primitive, AttributeDomain.Part];

    private static AttributeDomain ToAttributeDomain(Domains domain)
    {
        return domain switch
                   {
                       Domains.Points     => AttributeDomain.Point,
                       Domains.Vertices   => AttributeDomain.Vertex,
                       Domains.Primitives => AttributeDomain.Primitive,
                       _                  => AttributeDomain.Part,
                   };
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _statusMessage;

    #region ICustomDropdownHolder - lists the attributes of the colored domain on the last input
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return inputId == Selection.Input.InputDefinition.Id
                   ? GeometryAttributeChoices.GetSelectionDisplayValue(Selection.Value, _lastSource)
                   : Attribute.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        return inputId == Selection.Input.InputDefinition.Id
                   ? GeometryAttributeChoices.GetSelectionOptions(_lastSource)
                   : GetMappableAttributes();
    }

    private IEnumerable<string> GetMappableAttributes()
    {
        if (_lastSource == null)
            yield break;

        var attributes = _lastSource.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (attribute.Domain == _lastDomain && !string.Equals(attribute.Name, GeometryAttributeNames.Color, StringComparison.OrdinalIgnoreCase))
                yield return attribute.Name;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (selected == null)
            return;

        if (inputId == Selection.Input.InputDefinition.Id)
            Selection.SetTypedInputValue(GeometryAttributeChoices.ToSelectionValue(selected));
        else if (inputId == Attribute.Input.InputDefinition.Id)
            Attribute.SetTypedInputValue(selected);
    }
    #endregion

    private readonly Geometry _output = new();
    private GeometryAttribute<Vector4>? _colors;
    private float[] _weights = [];
    private float[] _pointWeights = [];
    private Geometry? _lastSource;
    private AttributeDomain _lastDomain;
    private string _statusMessage = string.Empty;

    [Input(Guid = "73465bee-c3e5-4a74-aa90-25544807d0db")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "f12169c2-ba7d-4e44-916d-1300a8297ac7", MappedType = typeof(Domains))]
    public readonly InputSlot<int> Domain = new();

    [Input(Guid = "c1d24e54-d36d-4bff-8d22-c036b3bbc7f3", MappedType = typeof(InputModes))]
    public readonly InputSlot<int> InputMode = new();

    [Input(Guid = "6b9644e5-7ccc-4939-acd7-1843ae13590f")]
    public readonly InputSlot<string> Attribute = new();

    [Input(Guid = "f68e29c5-a751-49d0-b1a5-7e8d51adb70e")]
    public readonly InputSlot<ScalarField> Field = new();

    [Input(Guid = "7a46fc42-ee5a-41e9-974e-f679d95c9be0", MappedType = typeof(MappingModes))]
    public readonly InputSlot<int> Mapping = new();

    [Input(Guid = "d45c60d5-a982-4b55-a7c7-13917c9c3fa9")]
    public readonly InputSlot<float> Range = new();

    [Input(Guid = "80562710-c60c-4748-8450-1a559f63789a")]
    public readonly InputSlot<float> Phase = new();

    [Input(Guid = "40d43e7d-ac00-4a01-8003-0c1994c9ca9d")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "00d822de-e7f9-4e67-b648-61292ac4a74e")]
    public readonly InputSlot<float> Randomize = new();

    [Input(Guid = "d819c1fa-92f9-4dcb-9cfa-f31255c25d9f")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "6698962e-2030-4d64-a188-46202dc05ee7")]
    public readonly InputSlot<Gradient> Gradient = new();

    [Input(Guid = "765026fe-c3ff-4c70-bd08-dbd2479bbec4", MappedType = typeof(WriteModes))]
    public readonly InputSlot<int> WriteMode = new();

    [Input(Guid = "aac052f7-1620-4b62-ad67-1caf37ae414f")]
    public readonly InputSlot<string> Selection = new();
}

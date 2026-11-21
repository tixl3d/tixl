#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry.attribute;

/// <summary>
/// Samples a scalar field at the position of every point, vertex, primitive or part of a Geometry and writes the
/// value as a float attribute. A float selection blends the new value over the old one. Topology, positions and
/// other attributes are shared.
/// </summary>
[Guid("0eb6da76-675d-4ef6-8f7d-f392b8ae5d51")]
internal sealed class AttributeFromField : Instance<AttributeFromField>, IStatusProvider, ICustomDropdownHolder
{
    private enum Domains
    {
        Points,
        Vertices,
        Primitives,
        Parts,
    }

    [Output(Guid = "0c51c7af-79ff-4578-a37f-86c897cadb81")]
    public readonly Slot<Geometry?> Result = new();

    public AttributeFromField()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var domain = ToAttributeDomain((Domains)Domain.GetValue(context).Clamp(0, 3));
        var name = Name.GetValue(context);
        var field = Field.GetValue(context);
        var selectionName = Selection.GetValue(context);
        _statusMessage = string.Empty;
        _lastSource = source;
        _lastDomain = domain;

        if (source == null)
        {
            Result.Value = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            _statusMessage = "Set a Name for the attribute.";
            Result.Value = source;
            return;
        }

        if (field == null)
        {
            _statusMessage = "Connect a field to sample.";
            Result.Value = source;
            return;
        }

        var count = GeometryElements.GetCount(source, domain);
        if (_weights.Length < count)
            _weights = new float[count];

        if (_pointWeights.Length < source.PointCount)
            _pointWeights = new float[source.PointCount];

        // Share topology; replace only the written attribute
        _output.Positions = source.Positions;
        _output.PrimitiveVertexOffsets = source.PrimitiveVertexOffsets;
        _output.VertexPoints = source.VertexPoints;
        _output.PrimitiveKind = source.PrimitiveKind;
        _output.PrimitiveClosed = source.PrimitiveClosed;
        _output.Parts = source.Parts;
        _output.Attributes.Clear();
        float[]? oldValues = null;
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain == domain && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                oldValues = (attribute as GeometryAttribute<float>)?.Values;
                continue;
            }

            _output.Attributes.Add(attribute);
        }

        var hasSelection = CollectWeights(source, domain, selectionName, count);
        if (_target == null || _target.Domain != domain || !string.Equals(_target.Name, name, StringComparison.Ordinal))
            _target = new GeometryAttribute<float>(name, domain, count);
        else
            _target.Resize(count);

        var values = _target.Values;
        for (var i = 0; i < count; i++)
        {
            var sample = field.Sample(GeometryElements.GetPosition(source, domain, i));
            var old = oldValues != null && i < oldValues.Length ? oldValues[i] : 0;
            values[i] = hasSelection ? old + (sample - old) * _weights[i] : sample;
        }

        _output.Attributes.Add(_target);
        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    /// <returns>False when every element gets the sampled value.</returns>
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

        _statusMessage = $"No float attribute '{selectionName}'; nothing is changed.";
        Array.Clear(_weights, 0, count);
        return true;
    }

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

    #region ICustomDropdownHolder - attribute names of the last input
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return inputId == Selection.Input.InputDefinition.Id
                   ? GeometryAttributeChoices.GetSelectionDisplayValue(Selection.Value, _lastSource)
                   : Name.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        return inputId == Selection.Input.InputDefinition.Id
                   ? GeometryAttributeChoices.GetSelectionOptions(_lastSource)
                   : GeometryAttributeChoices.GetAttributeNames(_lastSource, _lastDomain, floatsOnly: true);
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (selected == null)
            return;

        if (inputId == Selection.Input.InputDefinition.Id)
            Selection.SetTypedInputValue(GeometryAttributeChoices.ToSelectionValue(selected));
        else if (inputId == Name.Input.InputDefinition.Id)
            Name.SetTypedInputValue(selected);
    }
    #endregion

    private readonly Geometry _output = new();
    private GeometryAttribute<float>? _target;
    private float[] _weights = [];
    private float[] _pointWeights = [];
    private Geometry? _lastSource;
    private AttributeDomain _lastDomain;
    private string _statusMessage = string.Empty;

    [Input(Guid = "76b2efe5-434f-4c68-80b3-a7e1c74851f9")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "297addd8-6853-422c-abca-ae88e802e03e", MappedType = typeof(Domains))]
    public readonly InputSlot<int> Domain = new();

    [Input(Guid = "bfcb2463-3683-4404-a68f-d1ef258f081f")]
    public readonly InputSlot<string> Name = new();

    [Input(Guid = "51736f5a-a348-4550-8985-c60e61a15253")]
    public readonly InputSlot<ScalarField> Field = new();

    [Input(Guid = "53f453e6-4489-4795-93dc-5a59dbb21359")]
    public readonly InputSlot<string> Selection = new();
}

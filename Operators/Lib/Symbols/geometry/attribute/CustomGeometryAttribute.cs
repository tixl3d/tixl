#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.geometry.attribute;

/// <summary>
/// Writes a named attribute on one domain of a Geometry from a C# snippet evaluated per element, which can read other
/// attributes as @Name. A float selection blends the new value over the old one per element. Topology, positions and
/// other attributes are shared.
/// </summary>
[Guid("2e781d86-2a24-4ee9-95fd-26a051e0aeff")]
[ExportDependencies("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll")]
internal sealed class CustomGeometryAttribute : Instance<CustomGeometryAttribute>, IStatusProvider, ICustomDropdownHolder, IInputStatusProvider
{
    private enum Domains
    {
        Points,
        Vertices,
        Primitives,
        Parts,
    }

    private enum Types
    {
        Float,
        Int,
        Vector2,
        Vector3,
        Vector4,
    }

    [Output(Guid = "e90cfe0e-c1e8-461e-b755-431b9929f017")]
    public readonly Slot<Geometry?> Result = new();

    public CustomGeometryAttribute()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var source = Geometry.GetValue(context);
        var domain = ToAttributeDomain((Domains)Domain.GetValue(context).Clamp(0, 3));
        var name = Name.GetValue(context);
        var type = (Types)Type.GetValue(context).Clamp(0, 4);
        var code = Code.GetValue(context) ?? string.Empty;
        var a = A.GetValue(context);
        var b = B.GetValue(context);
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

        var count = GeometryElements.GetCount(source, domain);
        if (_values.Length < count)
        {
            _values = new Vector4[count];
            _oldValues = new Vector4[count];
            _weights = new float[count];
        }

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
        GeometryAttribute? existing = null;
        for (var index = 0; index < source.Attributes.Count; index++)
        {
            var attribute = source.Attributes[index];
            if (attribute.Domain == domain && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                existing = attribute;
                continue;
            }

            _output.Attributes.Add(attribute);
        }

        var isSnippetReady = PrepareSnippet(code, type, source, domain);
        var hasSelection = CollectWeights(source, domain, selectionName, count);
        ReadValues(existing, _oldValues, count);
        ComputeValues(source, domain, a, b, count, isSnippetReady);

        var target = GetTargetAttribute(name, domain, type, count);
        for (var i = 0; i < count; i++)
        {
            var newValue = _values[i];
            if (hasSelection)
            {
                var weight = _weights[i];
                newValue = type == Types.Int
                               ? weight >= 0.5f ? newValue : _oldValues[i]
                               : Vector4.Lerp(_oldValues[i], newValue, weight);
            }

            Write(target, i, newValue);
        }

        _output.Attributes.Add(target);
        _output.InvalidateTopologyCaches();
        Result.Value = _output;
    }

    /// <returns>False when every element gets the new value.</returns>
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

    /** Runs the snippet per element; without a working snippet the old values stay. */
    private void ComputeValues(Geometry source, AttributeDomain domain, float a, float b, int count, bool isSnippetReady)
    {
        var evaluate = isSnippetReady ? _compiler.Delegate : null;
        if (evaluate == null)
        {
            Array.Copy(_oldValues, _values, count);
            return;
        }

        for (var i = 0; i < count; i++)
            _values[i] = evaluate(i, count, GeometryElements.GetPosition(source, domain, i), a, b);
    }

    /** Any numeric attribute as Vector4 components; missing values are zero. */
    private static void ReadValues(GeometryAttribute? attribute, Vector4[] target, int count)
    {
        Array.Clear(target, 0, count);
        switch (attribute)
        {
            case GeometryAttribute<float> floats:
                for (var i = 0; i < Math.Min(count, floats.Values.Length); i++)
                    target[i] = new Vector4(floats.Values[i], 0, 0, 0);
                break;
            case GeometryAttribute<int> ints:
                for (var i = 0; i < Math.Min(count, ints.Values.Length); i++)
                    target[i] = new Vector4(ints.Values[i], 0, 0, 0);
                break;
            case GeometryAttribute<Vector2> vectors2:
                for (var i = 0; i < Math.Min(count, vectors2.Values.Length); i++)
                    target[i] = new Vector4(vectors2.Values[i], 0, 0);
                break;
            case GeometryAttribute<Vector3> vectors3:
                for (var i = 0; i < Math.Min(count, vectors3.Values.Length); i++)
                    target[i] = new Vector4(vectors3.Values[i], 0);
                break;
            case GeometryAttribute<Vector4> vectors4:
                Array.Copy(vectors4.Values, target, Math.Min(count, vectors4.Values.Length));
                break;
        }
    }

    private static void Write(GeometryAttribute target, int index, Vector4 value)
    {
        switch (target)
        {
            case GeometryAttribute<float> floats:
                floats.Values[index] = value.X;
                break;
            case GeometryAttribute<int> ints:
                ints.Values[index] = (int)MathF.Round(value.X);
                break;
            case GeometryAttribute<Vector2> vectors2:
                vectors2.Values[index] = new Vector2(value.X, value.Y);
                break;
            case GeometryAttribute<Vector3> vectors3:
                vectors3.Values[index] = new Vector3(value.X, value.Y, value.Z);
                break;
            case GeometryAttribute<Vector4> vectors4:
                vectors4.Values[index] = value;
                break;
        }
    }

    /** This op's own buffer, reused while name, domain and type stay the same. */
    private GeometryAttribute GetTargetAttribute(string name, AttributeDomain domain, Types type, int count)
    {
        if (_target != null && _targetType == type && _target.Domain == domain && string.Equals(_target.Name, name, StringComparison.Ordinal))
        {
            _target.Resize(count);
            return _target;
        }

        _targetType = type;
        _target = type switch
                      {
                          Types.Float   => new GeometryAttribute<float>(name, domain, count),
                          Types.Int     => new GeometryAttribute<int>(name, domain, count),
                          Types.Vector2 => new GeometryAttribute<Vector2>(name, domain, count),
                          Types.Vector3 => new GeometryAttribute<Vector3>(name, domain, count),
                          _             => new GeometryAttribute<Vector4>(name, domain, count),
                      };
        return _target;
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

    /// <summary>Parses @attributes, recompiles when the code, the type or their types changed and binds their values.</summary>
    private bool PrepareSnippet(string code, Types type, Geometry source, AttributeDomain domain)
    {
        if (code != _parsedCode)
        {
            _snippetAttributes.Parse(code);
            _parsedCode = code;
        }

        _attributeError = string.Empty;
        if (!_snippetAttributes.Resolve(source, domain, out var error))
        {
            // The last compile error belongs to earlier code; the missing attribute is the problem now
            _attributeError = error!;
            _compileError = string.Empty;
            _compiledCode = null;
            return false;
        }

        var signature = _snippetAttributes.GetTypeSignature();
        if (code != _compiledCode || type != _compiledType || signature != _compiledSignature)
        {
            _compiledSignature = signature;
            Compile(code, type);
        }

        return _snippetAttributes.Bind(source, domain);
    }

    /// <summary>The snippet returns the attribute's type (a float for Float and Int); it is wrapped into a Vector4.</summary>
    private void Compile(string code, Types type)
    {
        _compiledCode = code;
        _compiledType = type;
        _compileError = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
            return;

        var (returnType, wrap) = type switch
                                     {
                                         Types.Vector2 => ("Vector2", "var v = Snippet(i, count, p, A, B); return new Vector4(v.X, v.Y, 0, 0);"),
                                         Types.Vector3 => ("Vector3", "return new Vector4(Snippet(i, count, p, A, B), 0);"),
                                         Types.Vector4 => ("Vector4", "return Snippet(i, count, p, A, B);"),
                                         _             => ("float", "return new Vector4(Snippet(i, count, p, A, B), 0, 0, 0);"),
                                     };
        var source = $$"""
                       using System;
                       using System.Numerics;
                       using static System.MathF;

                       public static class __CustomGeometryAttribute
                       {
                       {{_snippetAttributes.GetFieldDeclarations()}}
                           public static Vector4 Evaluate(int i, int count, Vector3 p, float A, float B)
                           {
                               {{wrap}}
                           }

                           private static {{returnType}} Snippet(int i, int count, Vector3 p, float A, float B)
                           {
                       #line 1
                       {{_snippetAttributes.Code}}
                           }
                       }
                       """;
        if (_compiler.TryCompile(source, "__CustomGeometryAttribute", "Evaluate", "CustomGeometryAttributeSnippet", _compileErrors))
        {
            _snippetAttributes.SetCompiledType(_compiler.Delegate?.Method.DeclaringType);
            return;
        }

        _snippetAttributes.SetCompiledType(null);

        _compileErrorLine = _compiler.FirstErrorLine;
        _compileError = _compileErrors.Count > 0 ? $"Snippet {_compileErrors[0]}" : "Snippet failed to compile";
        foreach (var error in _compileErrors)
            Log.Warning($"CustomGeometryAttribute {error}", this);
    }

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(GetStatusMessage()) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        var snippetMessage = GetSnippetMessage();
        return snippetMessage ?? _statusMessage;
    }

    bool IInputStatusProvider.TryGetInputStatus(Guid inputId, out InputStatus status)
    {
        status = default;
        if (inputId != Code.Input.InputDefinition.Id)
            return false;

        var message = GetSnippetMessage();
        if (message != null)
        {
            status = new InputStatus(message, _attributeError.Length > 0 ? 0 : _compileErrorLine);
            return true;
        }

        // No problem: say which attributes come from another domain
        status = new InputStatus(_snippetAttributes.ConversionNote, IsError: false);
        return status.Message.Length > 0;
    }

    private string? GetSnippetMessage()
    {
        if (_attributeError.Length > 0)
            return _attributeError;

        return _compileError.Length > 0 ? _compileError : null;
    }

    #region ICustomDropdownHolder - attribute names of the last input
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == Selection.Input.InputDefinition.Id)
            return GeometryAttributeChoices.GetSelectionDisplayValue(Selection.Value, _lastSource);

        return Name.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        return inputId == Selection.Input.InputDefinition.Id
                   ? GeometryAttributeChoices.GetSelectionOptions(_lastSource)
                   : GeometryAttributeChoices.GetAttributeNames(_lastSource, _lastDomain, floatsOnly: false);
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

    private Geometry? _lastSource;
    private AttributeDomain _lastDomain;
    private delegate Vector4 SnippetFn(int i, int count, Vector3 p, float a, float b);

    private readonly Geometry _output = new();
    private readonly CSharpSnippetCompiler<SnippetFn> _compiler = new();
    private readonly List<string> _compileErrors = [];
    private GeometryAttribute? _target;
    private Types _targetType;
    private Vector4[] _values = [];
    private Vector4[] _oldValues = [];
    private float[] _weights = [];
    private float[] _pointWeights = [];
    private string? _compiledCode;
    private Types _compiledType;
    private string? _parsedCode;
    private int _compiledSignature;
    private readonly GeometrySnippetAttributes _snippetAttributes = new();
    private string _compileError = string.Empty;
    private int _compileErrorLine;
    private string _attributeError = string.Empty;
    private string _statusMessage = string.Empty;

    [Input(Guid = "e469fa5c-3838-4af1-9e48-7bcb0a687f33")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "6b66dbbd-7d8d-4e98-845c-d472bb1cf296", MappedType = typeof(Domains))]
    public readonly InputSlot<int> Domain = new();

    [Input(Guid = "945f7c9d-fef3-40fa-bd00-b612dc3a7979")]
    public readonly InputSlot<string> Name = new();

    [Input(Guid = "f1f1fde1-9f97-4618-a020-aa6c800b7466", MappedType = typeof(Types))]
    public readonly InputSlot<int> Type = new();

    [Input(Guid = "837cd3ee-d92e-46c3-8d42-69f438c81e50")]
    public readonly InputSlot<string> Code = new();

    [Input(Guid = "5fdbbd34-2144-4ba0-bea1-2866726ca09b")]
    public readonly InputSlot<float> A = new();

    [Input(Guid = "a9170934-849e-4dcf-9202-89385090e6de")]
    public readonly InputSlot<float> B = new();

    [Input(Guid = "19ea3de4-7a2a-4368-9360-a50123280689")]
    public readonly InputSlot<string> Selection = new();
}
